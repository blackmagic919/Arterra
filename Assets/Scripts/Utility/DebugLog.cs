using UnityEngine;

namespace Arterra.Utils
{
    public static class ADebug
    {
        public enum LogLevel
        {
            Critical = 0,
            Error = 1,
            Warning = 2,
            Info = 3,
            Trace = 4,
            Debug = 5
        }

        public static LogLevel CurrentLevel = LogLevel.Debug;

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void Log(string message, LogLevel level = LogLevel.Info)
        {
            if (level <= CurrentLevel)
            {
                switch (level)
                {
                    case LogLevel.Error:
                    case LogLevel.Critical:
                        Debug.LogError(message);
                        break;
                    case LogLevel.Warning:
                        Debug.LogWarning(message);
                        break;
                    case LogLevel.Info:
                    case LogLevel.Trace:
                    case LogLevel.Debug:
                        Debug.Log(message);
                        break;
                    default:
                        Debug.Log(message);
                        break;
                }
            }
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void LogError(string message)
        {
            Log(message, LogLevel.Error);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void LogWarning(string message)
        {
            Log(message, LogLevel.Warning);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void LogInfo(string message)
        {
            Log(message, LogLevel.Info);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void LogDebug(string message)
        {
            Log(message, LogLevel.Debug);
        }
    }

}