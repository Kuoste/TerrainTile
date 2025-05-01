using System;
using UnityEngine;

namespace Kuoste.TerrainEngine.Common.Loggers
{
    public class UnityLogger : Kuoste.TerrainEngine.Common.Interfaces.ILogger
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
