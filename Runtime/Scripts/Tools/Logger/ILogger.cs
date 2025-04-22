using System;

namespace Kuoste.LidarWorld.Tools.Logger
{
    internal interface ILogger
    {
        void LogDebug(string message);
        void LogInfo(string message);
        void LogWarning(string message);
        void LogError(string message);
        void LogException(Exception exception);
    }
}

