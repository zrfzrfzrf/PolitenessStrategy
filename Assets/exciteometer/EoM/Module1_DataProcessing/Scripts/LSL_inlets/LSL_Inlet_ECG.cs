using System.Collections.Generic;
using System.Globalization;
using Assets.LSL4Unity.Scripts;
using UnityEngine;

namespace ExciteOMeter
{
    public class LSL_Inlet_ECG : InletIntSamples
    {
        private enum EcgDecodingMode
        {
            AutoDetect,
            LegacyDevicesByteShift,
            NativeInt32,
        }

        private enum EcgTimestampMode
        {
            Unknown,
            LslSeconds,
            DeviceNanoseconds,
        }

        private const int MinSigned24Bit = -8388608;
        private const int MaxSigned24Bit = 8388607;
        private const double PreSessionTimestampToleranceSeconds = 0.001;
        private const double NanosecondTimestampThreshold = 1.0e12;
        private const double NanosecondsToSeconds = 1.0e-9;

        private readonly struct TimestampedRawSample
        {
            public TimestampedRawSample(int value, double lslTimestamp)
            {
                Value = value;
                LslTimestamp = lslTimestamp;
            }

            public int Value { get; }
            public double LslTimestamp { get; }
        }

        [Header("ECG decoding")]
        [SerializeField] private EcgDecodingMode decodingMode = EcgDecodingMode.AutoDetect;
        [SerializeField, Min(16)] private int autoDetectionSamples = 64;
        [SerializeField, Range(0.5f, 1f)] private float legacyPaddingRatio = 0.8f;
        [SerializeField] private bool logRejectedSamples;

        [Header("ECG timestamps")]
        [Tooltip("Nominal sampling frequency used for the first packet and as a fallback when a device packet interval is invalid.")]
        [SerializeField, Min(1f)] private float deviceTimestampSampleRateHz = 130f;
        [Tooltip("Discard the systematic zero marker observed at the start of every Polar device-timestamp packet.")]
        [SerializeField] private bool discardLeadingPacketZero = true;
        [Tooltip("Spread the real ECG values evenly between consecutive Polar packet timestamps. This removes artificial inter-packet gaps without inventing ECG samples.")]
        [SerializeField] private bool fitSamplesToDevicePacketInterval = true;
        [Tooltip("Only fit a packet when its effective sample rate is this close to the nominal rate. Larger differences are treated as real packet loss and keep a visible gap.")]
        [SerializeField, Range(0.01f, 0.5f)] private float maximumPacketRateDeviationFraction = 0.2f;

        private readonly List<TimestampedRawSample> pendingSamples = new List<TimestampedRawSample>();
        private readonly List<TimestampedRawSample> framingBuffer = new List<TimestampedRawSample>(4);
        private readonly List<int> pendingDevicePacketValues = new List<int>(128);
        private EcgDecodingMode activeMode;
        private EcgTimestampMode timestampMode;
        private bool modeResolved;
        private int rejectedSamples;
        private int rejectedChunkHeaders;
        private int rejectedPreSessionSamples;
        private int nonIncreasingLslTimestamps;
        private int discardedLeadingPacketZeros;
        private int fittedDevicePacketIntervals;
        private int rejectedDevicePacketIntervals;
        private double sessionStartLslTimestamp;
        private double lastPublishedLslTimestamp = double.NaN;
        private double pendingDevicePacketTimestampNanoseconds;
        private double pendingDevicePacketArrivalSessionTime;
        private double deviceTimestampAnchorNanoseconds;
        private double deviceTimestampAnchorSessionTime;
        private double lastDevicePacketTimestampNanoseconds = double.NaN;
        private bool hasSessionTimestampOrigin;
        private bool hasPendingDevicePacket;
        private bool hasDeviceTimestampAnchor;

        private void OnEnable()
        {
            EoM_Events.OnLoggingStateChanged += HandleLoggingStateChanged;
            EoM_Events.OnPostProcessingStarted += HandlePostProcessingStarted;

            if (ExciteOMeterManager.currentlyRecordingSession)
            {
                ConfigureSessionTimestampOrigin();
            }
        }

        private void OnDisable()
        {
            EoM_Events.OnLoggingStateChanged -= HandleLoggingStateChanged;
            EoM_Events.OnPostProcessingStarted -= HandlePostProcessingStarted;
        }

        private void LateUpdate()
        {
            // A possible legacy header may span frames. Do not close the packet until
            // that short prefix has either been confirmed or released as ECG data.
            if (framingBuffer.Count == 0)
            {
                FlushPendingDevicePacket();
            }
        }

        public override void AStreamIsFound(LSLStreamInfoWrapper stream)
        {
            if (!isTheExpected(stream))
            {
                return;
            }

            if (stream.Item.channel_format() != LSL.liblsl.channel_format_t.cf_int32)
            {
                Debug.LogError(
                    $"RawECG stream has format {stream.Item.channel_format()}, but EOM expects int32. " +
                    "Session start will keep waiting for valid ECG samples.");
                return;
            }

            ResetDecoder();
            base.AStreamIsFound(stream);

            // Convert timestamps generated on the sender computer into the LSL clock
            // domain of this Unity computer. The original per-sample timing is retained.
            inlet.set_postprocessing(LSL.liblsl.processing_options_t.post_clocksync);
        }

        protected override void Process(int[] newSample, double timestamp)
        {
            if (ExciteOMeterManager.inPostProcessingStage)
            {
                return;
            }

            ProcessFramedSample(new TimestampedRawSample(newSample[0], timestamp));
        }

        private void ProcessFramedSample(TimestampedRawSample rawSample)
        {
            framingBuffer.Add(rawSample);

            while (framingBuffer.Count > 0)
            {
                if (!CouldStillBeLegacyChunkHeader())
                {
                    TimestampedRawSample confirmedSample = framingBuffer[0];
                    framingBuffer.RemoveAt(0);
                    ProcessRawValue(confirmedSample);
                    continue;
                }

                if (framingBuffer.Count < 4)
                {
                    return;
                }

                rejectedChunkHeaders++;
                if (rejectedChunkHeaders == 1 || rejectedChunkHeaders % 100 == 0)
                {
                    Debug.LogWarning(
                        $"Removed legacy RawECG chunk header #{rejectedChunkHeaders}: " +
                        $"{framingBuffer[0].Value},{framingBuffer[1].Value}," +
                        $"{framingBuffer[2].Value},{framingBuffer[3].Value}.");
                }

                framingBuffer.Clear();
            }
        }

        private void ProcessRawValue(TimestampedRawSample rawSample)
        {
            if (!modeResolved)
            {
                pendingSamples.Add(rawSample);
                TryResolveMode();
                return;
            }

            DecodeAndPublish(rawSample);
        }

        private bool CouldStillBeLegacyChunkHeader()
        {
            if (decodingMode == EcgDecodingMode.NativeInt32)
            {
                return false;
            }

            // The UWP int[,] P/Invoke path emits [rank, length, lower-bound, length]
            // before each ECG chunk. In the observed legacy app this is [1, 73, 0, 73].
            if (framingBuffer[0].Value != 1)
            {
                return false;
            }

            if (framingBuffer.Count >= 2
                && (framingBuffer[1].Value <= 0 || framingBuffer[1].Value > 512))
            {
                return false;
            }

            if (framingBuffer.Count >= 3 && framingBuffer[2].Value != 0)
            {
                return false;
            }

            return framingBuffer.Count < 4
                || framingBuffer[1].Value == framingBuffer[3].Value;
        }

        private void TryResolveMode()
        {
            int requiredSamples = Mathf.Max(16, autoDetectionSamples);
            if (pendingSamples.Count < requiredSamples)
            {
                return;
            }

            int paddedSamples = 0;
            for (int i = 0; i < pendingSamples.Count; i++)
            {
                if ((pendingSamples[i].Value & 0xFF) == 0)
                {
                    paddedSamples++;
                }
            }

            float paddedFraction = (float)paddedSamples / pendingSamples.Count;
            float requiredPaddingRatio = Mathf.Clamp(legacyPaddingRatio, 0.5f, 1f);
            activeMode = paddedFraction >= requiredPaddingRatio
                ? EcgDecodingMode.LegacyDevicesByteShift
                : EcgDecodingMode.NativeInt32;
            modeResolved = true;

            Debug.Log(
                $"RawECG decoder selected {activeMode} " +
                $"({paddedFraction:P0} of detection samples had the legacy zero byte).");

            for (int i = 0; i < pendingSamples.Count; i++)
            {
                DecodeAndPublish(pendingSamples[i]);
            }

            pendingSamples.Clear();
        }

        private void DecodeAndPublish(TimestampedRawSample rawSample)
        {
            int rawValue = rawSample.Value;
            int decodedValue;

            if (activeMode == EcgDecodingMode.LegacyDevicesByteShift)
            {
                if ((rawValue & 0xFF) != 0)
                {
                    RejectSample(rawValue, "legacy sample does not contain the expected zero padding byte");
                    return;
                }

                // The legacy UWP app built { 0, byte0, byte1, byte2 } on a little-endian PC.
                // Arithmetic right shift restores the signed 24-bit microvolt value.
                decodedValue = rawValue >> 8;
            }
            else
            {
                decodedValue = rawValue;
            }

            if (decodedValue < MinSigned24Bit || decodedValue > MaxSigned24Bit)
            {
                RejectSample(rawValue, "decoded value is outside the signed 24-bit ECG range");
                return;
            }

            ResolveTimestampMode(rawSample.LslTimestamp);

            if (timestampMode == EcgTimestampMode.DeviceNanoseconds)
            {
                QueueDeviceTimestampSample(rawSample.LslTimestamp, decodedValue);
                return;
            }

            if (!TryGetSessionTimestamp(rawSample.LslTimestamp, out double sessionTimestamp))
            {
                return;
            }

            PublishAtSessionTimestamp(decodedValue, sessionTimestamp);
        }

        private void ResolveTimestampMode(double rawTimestamp)
        {
            if (timestampMode != EcgTimestampMode.Unknown)
            {
                return;
            }

            timestampMode = rawTimestamp >= NanosecondTimestampThreshold
                ? EcgTimestampMode.DeviceNanoseconds
                : EcgTimestampMode.LslSeconds;

            if (timestampMode == EcgTimestampMode.DeviceNanoseconds)
            {
                Debug.Log(
                    $"RawECG detected Polar device nanosecond packet timestamps. " +
                    $"Using {deviceTimestampSampleRateHz:F3} Hz as the nominal rate and " +
                    "fitting valid samples to consecutive packet timestamps.");
            }
            else
            {
                Debug.Log("RawECG detected standard LSL timestamps in seconds.");
            }
        }

        private void QueueDeviceTimestampSample(double packetTimestampNanoseconds, int decodedValue)
        {
            if (hasPendingDevicePacket
                && packetTimestampNanoseconds != pendingDevicePacketTimestampNanoseconds)
            {
                FlushPendingDevicePacket();
            }

            if (!hasPendingDevicePacket)
            {
                pendingDevicePacketTimestampNanoseconds = packetTimestampNanoseconds;
                pendingDevicePacketArrivalSessionTime = ExciteOMeterManager.GetTimestampDouble();
                hasPendingDevicePacket = true;
            }

            pendingDevicePacketValues.Add(decodedValue);
        }

        private void FlushPendingDevicePacket()
        {
            if (!hasPendingDevicePacket || pendingDevicePacketValues.Count == 0)
            {
                return;
            }

            if (!ExciteOMeterManager.currentlyRecordingSession)
            {
                // Before the Session, these events only prove to AutoRecorder that valid
                // ECG data is arriving. LoggerController ignores the CSV writes.
                double readinessTimestamp = ExciteOMeterManager.GetTimestampDouble();
                int firstValueIndex = GetDevicePacketFirstValueIndex();
                for (int i = firstValueIndex; i < pendingDevicePacketValues.Count; i++)
                {
                    PublishAtSessionTimestamp(pendingDevicePacketValues[i], readinessTimestamp);
                }

                ClearPendingDevicePacket();
                return;
            }

            if (!hasDeviceTimestampAnchor)
            {
                deviceTimestampAnchorNanoseconds = pendingDevicePacketTimestampNanoseconds;
                deviceTimestampAnchorSessionTime = pendingDevicePacketArrivalSessionTime;
                hasDeviceTimestampAnchor = true;
            }

            if (!double.IsNaN(lastDevicePacketTimestampNanoseconds)
                && pendingDevicePacketTimestampNanoseconds <= lastDevicePacketTimestampNanoseconds)
            {
                Debug.LogWarning(
                    "RawECG received a repeated or non-increasing Polar packet timestamp; " +
                    "that packet was omitted to protect ECG time ordering.");
                ClearPendingDevicePacket();
                return;
            }

            int firstValueIndexForSession = GetDevicePacketFirstValueIndex();
            int validSampleCount = pendingDevicePacketValues.Count - firstValueIndexForSession;
            if (validSampleCount <= 0)
            {
                lastDevicePacketTimestampNanoseconds = pendingDevicePacketTimestampNanoseconds;
                ClearPendingDevicePacket();
                return;
            }

            double packetEndSessionTime = DeviceTimestampToSessionTime(
                pendingDevicePacketTimestampNanoseconds);
            double secondsPerSample = GetDevicePacketSecondsPerSample(validSampleCount);
            double firstSampleSessionTime = packetEndSessionTime
                - (validSampleCount - 1) * secondsPerSample;

            for (int i = firstValueIndexForSession; i < pendingDevicePacketValues.Count; i++)
            {
                int validSampleIndex = i - firstValueIndexForSession;
                double sampleSessionTime = firstSampleSessionTime
                    + validSampleIndex * secondsPerSample;
                if (sampleSessionTime < -PreSessionTimestampToleranceSeconds)
                {
                    rejectedPreSessionSamples++;
                    continue;
                }

                PublishAtSessionTimestamp(
                    pendingDevicePacketValues[i],
                    sampleSessionTime < 0.0 ? 0.0 : sampleSessionTime);
            }

            lastDevicePacketTimestampNanoseconds = pendingDevicePacketTimestampNanoseconds;
            ClearPendingDevicePacket();
        }

        private double DeviceTimestampToSessionTime(double timestampNanoseconds)
        {
            return deviceTimestampAnchorSessionTime
                + (timestampNanoseconds - deviceTimestampAnchorNanoseconds)
                * NanosecondsToSeconds;
        }

        private double GetDevicePacketSecondsPerSample(int validSampleCount)
        {
            double nominalRate = Mathf.Max(1f, deviceTimestampSampleRateHz);
            double nominalSecondsPerSample = 1.0 / nominalRate;

            if (!fitSamplesToDevicePacketInterval
                || double.IsNaN(lastDevicePacketTimestampNanoseconds))
            {
                return nominalSecondsPerSample;
            }

            double packetDurationSeconds =
                (pendingDevicePacketTimestampNanoseconds - lastDevicePacketTimestampNanoseconds)
                * NanosecondsToSeconds;
            if (packetDurationSeconds <= 0.0)
            {
                return nominalSecondsPerSample;
            }

            // The previous packet's last value is already placed at its device timestamp.
            // The current packet contributes validSampleCount new intervals, including the
            // interval from that previous endpoint to the first value in this packet.
            double effectiveRate = validSampleCount / packetDurationSeconds;
            double allowedDeviation = Mathf.Clamp(
                maximumPacketRateDeviationFraction,
                0.01f,
                0.5f);
            double minimumAcceptedRate = nominalRate * (1.0 - allowedDeviation);
            double maximumAcceptedRate = nominalRate * (1.0 + allowedDeviation);

            if (effectiveRate >= minimumAcceptedRate
                && effectiveRate <= maximumAcceptedRate)
            {
                fittedDevicePacketIntervals++;
                if (fittedDevicePacketIntervals == 1
                    || fittedDevicePacketIntervals % 100 == 0)
                {
                    Debug.Log(
                        $"RawECG fitted packet #{fittedDevicePacketIntervals}: " +
                        $"{validSampleCount} valid samples across " +
                        $"{packetDurationSeconds * 1000.0:F3} ms " +
                        $"({effectiveRate:F3} effective Hz).");
                }

                return packetDurationSeconds / validSampleCount;
            }

            rejectedDevicePacketIntervals++;
            if (effectiveRate > maximumAcceptedRate)
            {
                if (rejectedDevicePacketIntervals == 1
                    || rejectedDevicePacketIntervals % 100 == 0)
                {
                    Debug.LogWarning(
                        $"RawECG compressed a short packet interval because " +
                        $"{validSampleCount} samples across {packetDurationSeconds * 1000.0:F3} ms " +
                        $"implies {effectiveRate:F3} Hz. Fitting this interval prevents " +
                        "duplicate or backwards ECG timestamps.");
                }

                return packetDurationSeconds / validSampleCount;
            }

            if (rejectedDevicePacketIntervals == 1
                || rejectedDevicePacketIntervals % 100 == 0)
            {
                Debug.LogWarning(
                    $"RawECG kept a packet timing gap because " +
                    $"{validSampleCount} samples across {packetDurationSeconds * 1000.0:F3} ms " +
                    $"implies {effectiveRate:F3} Hz, outside the accepted " +
                    $"{minimumAcceptedRate:F3}-{maximumAcceptedRate:F3} Hz range. " +
                    "This most likely indicates a genuinely lost or malformed packet.");
            }

            return nominalSecondsPerSample;
        }

        private int GetDevicePacketFirstValueIndex()
        {
            if (!discardLeadingPacketZero
                || pendingDevicePacketValues.Count == 0
                || pendingDevicePacketValues[0] != 0)
            {
                return 0;
            }

            discardedLeadingPacketZeros++;
            if (discardedLeadingPacketZeros == 1
                || discardedLeadingPacketZeros % 100 == 0)
            {
                Debug.Log(
                    $"RawECG discarded systematic leading packet zero " +
                    $"#{discardedLeadingPacketZeros}.");
            }

            return 1;
        }

        private void ClearPendingDevicePacket()
        {
            pendingDevicePacketValues.Clear();
            pendingDevicePacketTimestampNanoseconds = 0.0;
            pendingDevicePacketArrivalSessionTime = 0.0;
            hasPendingDevicePacket = false;
        }

        private void PublishAtSessionTimestamp(int decodedValue, double sessionTimestamp)
        {
            float eventTimestamp = (float)sessionTimestamp;
            EoM_Events.Send_OnDataReceived(VariableType, eventTimestamp, decodedValue);
            LoggerController.instance.WriteLine(
                LogName.VariableRawECG,
                sessionTimestamp.ToString("F6", CultureInfo.InvariantCulture) + "," +
                decodedValue.ToString("F0", CultureInfo.InvariantCulture));
        }

        private bool TryGetSessionTimestamp(double correctedLslTimestamp, out double sessionTimestamp)
        {
            if (!ExciteOMeterManager.currentlyRecordingSession || !hasSessionTimestampOrigin)
            {
                // Before a Session starts, events are still needed so AutoRecorder can
                // confirm that a valid ECG sample has arrived. No CSV is written then.
                sessionTimestamp = ExciteOMeterManager.GetTimestampDouble();
                return true;
            }

            sessionTimestamp = correctedLslTimestamp - sessionStartLslTimestamp;

            if (sessionTimestamp < -PreSessionTimestampToleranceSeconds)
            {
                rejectedPreSessionSamples++;
                if (rejectedPreSessionSamples == 1)
                {
                    Debug.Log(
                        "RawECG discarded buffered samples captured before the EOM Session started.");
                }

                return false;
            }

            // Avoid tiny negative values caused only by the two clock reads used to set
            // the Session origin.
            if (sessionTimestamp < 0.0)
            {
                sessionTimestamp = 0.0;
            }

            if (!double.IsNaN(lastPublishedLslTimestamp)
                && correctedLslTimestamp <= lastPublishedLslTimestamp)
            {
                nonIncreasingLslTimestamps++;
                if (nonIncreasingLslTimestamps == 1 || nonIncreasingLslTimestamps % 1000 == 0)
                {
                    Debug.LogWarning(
                        "RawECG received repeated or non-increasing LSL timestamps. " +
                        "The sender must assign a capture timestamp to every ECG sample " +
                        "for waveform and R-peak timing analysis.");
                }
            }

            lastPublishedLslTimestamp = correctedLslTimestamp;

            return true;
        }

        private void HandlePostProcessingStarted()
        {
            // At Session stop no later sample may arrive to disprove a partial header
            // prefix. Preserve those remaining values as ECG before closing the file.
            while (framingBuffer.Count > 0)
            {
                TimestampedRawSample finalSample = framingBuffer[0];
                framingBuffer.RemoveAt(0);
                ProcessRawValue(finalSample);
            }

            FlushPendingDevicePacket();
        }

        private void HandleLoggingStateChanged(bool isLogging)
        {
            if (isLogging)
            {
                // Do not let the last few values buffered before Session start leak into
                // the new ECG file or the aligned Path cache.
                pendingSamples.Clear();
                framingBuffer.Clear();
                rejectedPreSessionSamples = 0;
                nonIncreasingLslTimestamps = 0;
                discardedLeadingPacketZeros = 0;
                fittedDevicePacketIntervals = 0;
                rejectedDevicePacketIntervals = 0;
                lastPublishedLslTimestamp = double.NaN;
                ClearPendingDevicePacket();
                hasDeviceTimestampAnchor = false;
                lastDevicePacketTimestampNanoseconds = double.NaN;

                int discarded = DiscardQueuedSamples();
                ConfigureSessionTimestampOrigin();
                Debug.Log($"RawECG cleared {discarded} queued LSL samples at Session start.");
            }
            else
            {
                hasSessionTimestampOrigin = false;
                lastPublishedLslTimestamp = double.NaN;
                ClearPendingDevicePacket();
                hasDeviceTimestampAnchor = false;
                lastDevicePacketTimestampNanoseconds = double.NaN;
            }
        }

        private void ConfigureSessionTimestampOrigin()
        {
            double localLslNow = LSL.liblsl.local_clock();
            double currentSessionTime = ExciteOMeterManager.GetTimestampDouble();
            sessionStartLslTimestamp = localLslNow - currentSessionTime;
            hasSessionTimestampOrigin = true;

            Debug.Log(
                $"RawECG LSL timestamp origin configured at Session Time {currentSessionTime:F6}.");
        }

        private void RejectSample(int rawValue, string reason)
        {
            rejectedSamples++;
            if (logRejectedSamples || rejectedSamples == 1 || rejectedSamples % 100 == 0)
            {
                Debug.LogWarning($"Rejected malformed RawECG sample #{rejectedSamples}: {rawValue} ({reason}).");
            }
        }

        private void ResetDecoder()
        {
            pendingSamples.Clear();
            framingBuffer.Clear();
            ClearPendingDevicePacket();
            rejectedSamples = 0;
            rejectedChunkHeaders = 0;
            rejectedPreSessionSamples = 0;
            nonIncreasingLslTimestamps = 0;
            discardedLeadingPacketZeros = 0;
            fittedDevicePacketIntervals = 0;
            rejectedDevicePacketIntervals = 0;
            lastPublishedLslTimestamp = double.NaN;
            lastDevicePacketTimestampNanoseconds = double.NaN;
            timestampMode = EcgTimestampMode.Unknown;
            hasDeviceTimestampAnchor = false;

            if (decodingMode == EcgDecodingMode.AutoDetect)
            {
                activeMode = EcgDecodingMode.AutoDetect;
                modeResolved = false;
            }
            else
            {
                activeMode = decodingMode;
                modeResolved = true;
            }
        }
    }
}
