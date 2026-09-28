namespace LanguageCore;

public static class LoggerExtensions
{
    public static void LogError(this ITraceLogger logger, string message) => logger.Log(LogType.Error, message);
    public static void LogWarning(this ITraceLogger logger, string message) => logger.Log(LogType.Warning, message);
    public static void LogDebug(this ITraceLogger logger, string message) => logger.Log(LogType.Debug, message);
    public static IDisposableProgress<string> Label(this ITraceLogger logger, LogType level, string message)
    {
        IDisposableProgress<string> label = logger.Label(level);
        label.Report(message);
        return label;
    }
}
