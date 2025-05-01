using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Schema;

namespace Kuoste.LidarWorld.Tools.Logger
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error,
        Exception
    }

    public class CompositeLogger
    {
        private readonly List<ILogger> _loggers;
        private readonly LogLevel _logLevel;

        public CompositeLogger(LogLevel logLevel, bool bUseConsole, bool bUseUnity, bool bUseFile,
            string sFilePath)
        {
            _loggers = new List<ILogger>();
            if (bUseConsole)
            {
                _loggers.Add(new ConsoleLogger());
            }
            if (bUseFile)
            {
                _loggers.Add(new FileLogger(sFilePath));
            }

            if (bUseUnity)
            {
#if UNITY_5_3_OR_NEWER
                _loggers.Add(new UnityLogger());
#endif
            }

            _logLevel = logLevel;
        }

        public void LogDebug(string message)
        {
            if (_logLevel <= LogLevel.Debug)
            {
                foreach (var logger in _loggers)
                    logger.LogDebug(message);
            }
        }

        public void LogInfo(string message)
        {
            if (_logLevel <= LogLevel.Info)
            {
                foreach (var logger in _loggers)
                    logger.LogInfo(message);
            }
        }

        public void LogWarning(string message)
        {
            if (_logLevel <= LogLevel.Warning)
            {
                foreach (var logger in _loggers)
                    logger.LogWarning(message);
            }
        }

        public void LogError(string message)
        {
            if (_logLevel <= LogLevel.Error)
            {
                foreach (var logger in _loggers)
                    logger.LogError(message);
            }
        }

        public void LogException(Exception exception)
        {
            if (_logLevel <= LogLevel.Exception)
            {
                foreach (var logger in _loggers)
                    logger.LogException(exception);
            }
        }
    }
}
