using Kuoste.TerrainEngine.Common.Interfaces;
using System;
using System.IO;

namespace Kuoste.TerrainEngine.Common.Loggers
{
    internal class FileLogger : ILogger
    {
        private readonly string _filePath;
        public FileLogger(string filePath)
        {
            _filePath = filePath;

            // Ensure the directory exists
            string sDir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(sDir) && !Directory.Exists(sDir))
            {
                Directory.CreateDirectory(sDir);
            }
        }

        public void LogDebug(string message)
        {
            WriteToFile("Debug", message);
        }
        public void LogInfo(string message)
        {
            WriteToFile("Info", message);
        }
        public void LogWarning(string message)
        {
            WriteToFile("Warning", message);
        }
        public void LogError(string message)
        {
            WriteToFile("Error", message);
        }
        public void LogException(Exception exception)
        {
            WriteToFile("Exception", exception.ToString());
        }
        private void WriteToFile(string logType, string message)
        {
            File.AppendAllText(_filePath, $"{DateTime.Now}: {logType} - {message}\n");
        }
    }
}
