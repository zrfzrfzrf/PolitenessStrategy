using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Assets.LSL4Unity.Scripts;
using UnityEngine;

namespace ExciteOMeter
{
    public class LSL_Inlet_RRi : InletFloatSamples
    {
        private readonly LslSessionTimestampMapper timestampMapper =
            new LslSessionTimestampMapper("RRInterval");

        private void OnEnable()
        {
            EoM_Events.OnLoggingStateChanged += HandleLoggingStateChanged;
        }

        private void OnDisable()
        {
            EoM_Events.OnLoggingStateChanged -= HandleLoggingStateChanged;
        }

        public override void AStreamIsFound(LSLStreamInfoWrapper stream)
        {
            if (!isTheExpected(stream))
            {
                return;
            }

            timestampMapper.ResetStream();
            base.AStreamIsFound(stream);
            inlet.set_postprocessing(LSL.liblsl.processing_options_t.post_clocksync);
        }

        protected override void Process(float[] newSample, double timeStamp)
        {
            float value = newSample[0];
            if (ExciteOMeterManager.inPostProcessingStage
                || float.IsNaN(value)
                || float.IsInfinity(value)
                || value <= 0f
                || !timestampMapper.TryGetSessionTimestamp(timeStamp, out double sessionTimestamp))
            {
                return;
            }

            EoM_Events.Send_OnDataReceived(VariableType, (float)sessionTimestamp, value);
            LoggerController.instance.WriteLine(
                LogName.VariableRrInterval,
                sessionTimestamp.ToString("F6", CultureInfo.InvariantCulture) + "," +
                value.ToString("F3", CultureInfo.InvariantCulture));
        }

        private void HandleLoggingStateChanged(bool isLogging)
        {
            if (isLogging)
            {
                int discarded = DiscardQueuedSamples();
                timestampMapper.BeginSession();
                Debug.Log($"RRInterval cleared {discarded} queued LSL samples at Session start.");
            }
            else
            {
                timestampMapper.EndSession();
            }
        }
    }
}

