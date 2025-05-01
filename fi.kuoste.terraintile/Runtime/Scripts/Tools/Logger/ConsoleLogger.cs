using System;

namespace Kuoste.LidarWorld.Tools.Logger
{
    internal class ConsoleLogger : ILogger
    {
        public void LogDebug(string message)
        {
            Console.WriteLine($"Debug: {message}");
        }
        public void LogInfo(string message)
        {
            Console.WriteLine($"Info: {message}");
        }
        public void LogWarning(string message)
        {
            Console.WriteLine($"Warning: {message}");
        }
        public void LogError(string message)
        {
            Console.WriteLine($"Error: {message}");
        }
        public void LogException(Exception exception)
        {
            Console.WriteLine($"Exception: {exception.Message}");
            Console.WriteLine(exception.StackTrace);
        }
    }
}

