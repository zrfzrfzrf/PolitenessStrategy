using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ExciteOMeter
{
    public class EoM_SignalEmulator : MonoBehaviour
    {
        public bool active = true;

        [Header("Update period")]
        public float sendingPeriod = 1.0f;
        private float elapsedTime = 0.0f;

        [Header("Maximum variability between subsequent samples")]
        [Range(0.0f,0.05f)]
        public float variance = 0.01f;

        [Header("Starting values")]
        private float HR=70.0f;
        private float RRi = 800.0f;
        private float ECG = 1000.0f;

        public bool sendHR = true;
        private bool previousSendHR = false; // Variables to detect changes of state during runtime.
        public bool sendRrI = true;
        private bool previousSendRRi = false;
        public bool sendECG = false;
        private bool previousSendECG = false;
        
        private float randomVariation = 0.0f;

        public static EoM_SignalEmulator instance;

        /// <summary>
        /// Set instance for settings object and initialize callbacks of UI
        /// </summary>
        private void Awake()
        {
            // Check singleton, each time the menu scene is loaded, the instance is replaced with the newest script
            if (instance == null)
            {
                instance = this;
            }
            else
            {
                Destroy(this.gameObject);
            }
        }

        public static void DisableEmulator()
        {
            if(instance != null && instance.active)
            {
                Debug.Log("Signal Emulator was deactivated. Most likely because real LSL signals were detected.");
                instance.active = false;
            }
        }

        // Update is called once per frame
        void Update()
        {
            if (!active) return;

            // Timer control
            elapsedTime += Time.deltaTime;

            // Emulate connection disconnection HR
            if(sendHR && !previousSendHR)
            {
                EoM_Events.Send_OnStreamConnected(DataType.HeartRate);
            }
            else if (!sendHR && previousSendHR)
            {
                EoM_Events.Send_OnStreamDisconnected(DataType.HeartRate);
            }
            
            // Emulate connection disconnection RRi
            if(sendRrI && !previousSendRRi)
            {
                EoM_Events.Send_OnStreamConnected(DataType.RRInterval);
            }
            else if (!sendRrI && previousSendRRi)
            {
                EoM_Events.Send_OnStreamDisconnected(DataType.RRInterval);
            }
            
            // Emulate connection disconnection ECG
            if(sendECG && !previousSendECG)
            {
                EoM_Events.Send_OnStreamConnected(DataType.RawECG);
            }
            else if (!sendECG && previousSendECG)
            {
                EoM_Events.Send_OnStreamDisconnected(DataType.RawECG);
            }
            
            
            // Send data each "sendingPeriod"
            if (elapsedTime >= sendingPeriod) 
            {
                // Reset timer for next event
                elapsedTime -= sendingPeriod;

                // Calculate new random values
                randomVariation = Random.Range(-variance,variance);
                
                // Setup new random values. If HR increases, RRi should decrease.
                HR += HR*randomVariation;
                RRi -= RRi*randomVariation*Random.Range(1.0f,3.0f);
                ECG = 1000.0f + Mathf.Sin(Time.time * 8.0f) * 200.0f + Random.Range(-20.0f, 20.0f);

                // Send events
                float timestamp = ExciteOMeterManager.GetTimestamp();

                if(sendHR)
                {
                    float HRf = (float)Mathf.RoundToInt(HR);
                    EoM_Events.Send_OnDataReceived(DataType.HeartRate, timestamp, HRf);
                    LoggerController.instance.WriteLine(LogName.VariableHeartRate, ExciteOMeterManager.ConvertFloatToString(timestamp) + "," + HR.ToString("F0"));
                }

                if(sendRrI)
                {
                    EoM_Events.Send_OnDataReceived(DataType.RRInterval, timestamp, RRi);
                    LoggerController.instance.WriteLine(LogName.VariableRrInterval, ExciteOMeterManager.ConvertFloatToString(timestamp) + "," + RRi.ToString("F3"));
                }

                if(sendECG)
                {
                    int ECGi = Mathf.RoundToInt(ECG);
                    EoM_Events.Send_OnDataReceived(DataType.RawECG, timestamp, ECGi);
                    LoggerController.instance.WriteLine(LogName.VariableRawECG, ExciteOMeterManager.ConvertFloatToString(timestamp) + "," + ECGi.ToString("F0"));
                }
            }

            // To detect changes in runtime
            previousSendHR = sendHR;
            previousSendRRi = sendRrI;
            previousSendECG = sendECG;
        }
    }
}