using System;
using UnityEngine;
using System.Collections.Generic;

namespace ExciteOMeter
{
    public class RMSSD : EoM_Base_FeatureCalculation
    {
        [Header("RRi artifact filtering")]
        [SerializeField] private bool filterAbnormalRri = true;
        [SerializeField, Min(1f)] private float minimumRriMs = 300f;
        [SerializeField, Min(1f)] private float maximumRriMs = 2000f;
        [SerializeField, Range(0.05f, 1f)] private float maximumMedianDeviationFraction = 0.20f;

        int N;
        float diff;
        float cumsum;
        float result;
        bool hasLoggedArtifactWarning;

        protected override void SetupStart()
        {
            isTimeBasedFeature = !SettingsManager.Values.featureSettings.RMSSD.isSampleBased;
            windowTime = SettingsManager.Values.featureSettings.RMSSD.windowTime;
            overlappingFraction = SettingsManager.Values.featureSettings.RMSSD.overlapPercentageTime;

            isSampleBasedFeature = SettingsManager.Values.featureSettings.RMSSD.isSampleBased;
            sampleBuffer = SettingsManager.Values.featureSettings.RMSSD.sampleBufferLength;
            overlappingSamples = SettingsManager.Values.featureSettings.RMSSD.overlapSamplesLength;
            offsetSamplesTimestamp = SettingsManager.Values.featureSettings.RMSSD.offsetSamplesInTimestamp;
        }

        protected override float CalculateFeature(float[] timestamps, float[] values)
        {
            /*
            REFERENCE: https://www.frontiersin.org/articles/10.3389/fpubh.2017.00258/full
            
            RMSSD
            The root mean square of successive differences between normal heartbeats (RMSSD) is obtained by first calculating each successive time difference between heartbeats in ms. Then, each of the values is squared and the result is averaged before the square root of the total is obtained. While the conventional minimum recording is 5 min, researchers have proposed ultra-short-term periods of 10 s (30), 30 s (31), and 60 s (36).
            */
            
            float[] valuesForCalculation = filterAbnormalRri
                ? FilterRriArtifacts(values)
                : values;
            int length = valuesForCalculation.Length;
            if (length < 2)
            {
                ExciteOMeterManager.DebugLog("It is not possible to calculate RMSSD with less than 2 values");
                return 0;
            }

            // Calculation
            N = length-1; // Size of final vector of differences

            cumsum = 0;
            for (int i = 0; i < N; i++)
            {
                diff = valuesForCalculation[i+1] - valuesForCalculation[i];
                cumsum += (float)Math.Pow(diff, 2);
            };

            result = (float)Math.Sqrt(cumsum/N);

            return result;
        }

        private float[] FilterRriArtifacts(float[] values)
        {
            float lowerBound = Mathf.Min(minimumRriMs, maximumRriMs);
            float upperBound = Mathf.Max(minimumRriMs, maximumRriMs);
            var rangeFiltered = new List<float>(values.Length);

            for (int i = 0; i < values.Length; i++)
            {
                float value = values[i];
                if (!float.IsNaN(value)
                    && !float.IsInfinity(value)
                    && value >= lowerBound
                    && value <= upperBound)
                {
                    rangeFiltered.Add(value);
                }
            }

            if (rangeFiltered.Count < 2)
            {
                LogArtifactFiltering(values.Length, rangeFiltered.Count);
                return rangeFiltered.ToArray();
            }

            float[] sorted = rangeFiltered.ToArray();
            Array.Sort(sorted);
            int middle = sorted.Length / 2;
            float median = sorted.Length % 2 == 0
                ? (sorted[middle - 1] + sorted[middle]) * 0.5f
                : sorted[middle];
            float maximumDeviation = Mathf.Abs(median) * maximumMedianDeviationFraction;
            var medianFiltered = new List<float>(rangeFiltered.Count);

            for (int i = 0; i < rangeFiltered.Count; i++)
            {
                if (Mathf.Abs(rangeFiltered[i] - median) <= maximumDeviation)
                {
                    medianFiltered.Add(rangeFiltered[i]);
                }
            }

            LogArtifactFiltering(values.Length, medianFiltered.Count);
            return medianFiltered.ToArray();
        }

        private void LogArtifactFiltering(int originalCount, int filteredCount)
        {
            if (hasLoggedArtifactWarning || filteredCount == originalCount)
            {
                return;
            }

            hasLoggedArtifactWarning = true;
            Debug.LogWarning(
                $"RMSSD excluded {originalCount - filteredCount} abnormal RRi values " +
                $"from a {originalCount}-value calculation window.");
        }
    }
}
