using System;

namespace Kuoste.TerrainEngine.Interfaces.DebugTools
{
    public interface ILogger
    {
        void LogDebug(string message);
        void LogInfo(string message);
        void LogWarning(string message);
        void LogError(string message);
        void LogException(Exception exception);
    }
}

