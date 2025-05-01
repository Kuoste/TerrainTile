using Kuoste.TerrainEngine.Common.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Schema;

namespace Kuoste.TerrainEngine.Common.Loggers
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error,
        Exception
    }

    public class CompositeLogger : ILogger
    {
        private readonly List<ILogger> _loggers;
        private readonly LogLevel _logLevel;

        public CompositeLogger(LogLevel logLevel, bool bUseConsole, bool bUseFile,
            string sFilePath)
        {
            _loggers = new List<ILogger>();
            if (bUseConsole)
            {
                AddLogger(new ConsoleLogger());
            }
            if (bUseFile)
            {
                AddLogger(new FileLogger(sFilePath));
            }

            _logLevel = logLevel;
        }

        public void AddLogger(ILogger logger)
        {
            _loggers.Add(logger);
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
