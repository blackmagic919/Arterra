using UnityEngine;

namespace Utils
{
    public static class ADebug
    {
        public enum LogLevel
        {
            Error = 0,
            Warning = 1,
            Info = 2,
            Debug = 3
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
                        Debug.LogError(message);
                        break;
                    case LogLevel.Warning:
                        Debug.LogWarning(message);
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