using StardewModdingAPI;

namespace ThaleTheGreat.Gatherers.Services;

internal static class Log
{
    private static IMonitor? Monitor;

    internal static void Init(IMonitor monitor)
    {
        Monitor = monitor;
    }

    internal static void Error(string message)
    {
        Monitor?.Log(message, LogLevel.Error);
    }

    internal static void Error(string message, Exception exception)
    {
        Monitor?.Log($"{message}: {exception}", LogLevel.Error);
    }
}
