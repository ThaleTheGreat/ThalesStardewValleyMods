using System;
using StardewModdingAPI;

namespace SpaceShared
{
    public static class Log
    {
        private static IMonitor? monitor;

        public static IMonitor Monitor
        {
            get => monitor ?? throw new InvalidOperationException("The mod monitor has not been initialized.");
            set => monitor = value;
        }

        public static bool EnableDebugLogging { get; set; }

        public static void Verbose(string message)
        {
            if (EnableDebugLogging)
                Monitor.Log(message, LogLevel.Trace);
        }

        public static void Trace(string message)
        {
            if (EnableDebugLogging)
                Monitor.Log(message, LogLevel.Trace);
        }

        public static void Debug(string message)
        {
            if (EnableDebugLogging)
                Monitor.Log(message, LogLevel.Debug);
        }

        public static void Info(string message)
        {
            if (EnableDebugLogging)
                Monitor.Log(message, LogLevel.Info);
        }

        public static void Warn(string message)
        {
            if (EnableDebugLogging)
                Monitor.Log(message, LogLevel.Warn);
        }

        public static void Error(string message)
        {
            Monitor.Log(message, LogLevel.Error);
        }
    }
}
