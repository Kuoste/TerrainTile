#if UNITY_5_3_OR_NEWER
using System;
using UnityEngine;

namespace Kuoste.TerrainEngine.DebugTools.Logger
{
    internal class UnityLogger : ILogger
    {
        public void LogDebug(string message)
        {
            Debug.Log(message);
        }
        public void LogInfo(string message)
        {
            Debug.Log(message);
        }
        public void LogWarning(string message)
        {
            Debug.LogWarning(message);
        }
        public void LogError(string message)
        {
            Debug.LogError(message);
        }
        public void LogException(Exception exception)
        {
            Debug.LogException(exception);
        }
    }
}
#endif
