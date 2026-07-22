namespace LanguageCore;

public enum LogType
{
    Normal,
    Warning,
    Error,
    Debug,
}

public interface ILogger
{
    void Log(LogType level, string message);
    void LogDiagnostic(DiagnosticAt diagnostic, IEnumerable<ISourceProvider>? sourceProviders) => Log(diagnostic.Level switch
    {
        DiagnosticsLevel.Error => LogType.Error,
        DiagnosticsLevel.Warning => LogType.Warning,
        DiagnosticsLevel.Information => LogType.Normal,
        DiagnosticsLevel.Hint => LogType.Normal,
        DiagnosticsLevel.OptimizationNotice => LogType.Normal,
        DiagnosticsLevel.FailedOptimization => LogType.Normal,
        _ => LogType.Normal,
    }, diagnostic.ToString());
    void LogDiagnostic(Diagnostic diagnostic, IEnumerable<ISourceProvider>? sourceProviders) => Log(diagnostic.Level switch
    {
        DiagnosticsLevel.Error => LogType.Error,
        DiagnosticsLevel.Warning => LogType.Warning,
        DiagnosticsLevel.Information => LogType.Normal,
        DiagnosticsLevel.Hint => LogType.Normal,
        DiagnosticsLevel.OptimizationNotice => LogType.Normal,
        DiagnosticsLevel.FailedOptimization => LogType.Normal,
        _ => LogType.Normal,
    }, diagnostic.ToString());

    IDisposableProgress<float> Progress(LogType level);

    IDisposableProgress<string> Label(LogType level);
}

public class VoidProgress<T> : IDisposableProgress<T>
{
    public static VoidProgress<T> Instance = new();
    public void Dispose() { }
    public void Report(T value) { }
}

public class VoidLogger : ILogger
{
    public static readonly VoidLogger Instance = new();

    public IDisposableProgress<string> Label(LogType level) => VoidProgress<string>.Instance;
    public void Log(LogType level, string message) { }
    public IDisposableProgress<float> Progress(LogType level) => VoidProgress<float>.Instance;
}

public static class LoggerExtensions
{
    public static void LogError(this ILogger logger, string message) => logger.Log(LogType.Error, message);
    public static void LogWarning(this ILogger logger, string message) => logger.Log(LogType.Warning, message);
    public static void LogDebug(this ILogger logger, string message) => logger.Log(LogType.Debug, message);
    public static IDisposableProgress<string> Label(this ILogger logger, LogType level, string message)
    {
        IDisposableProgress<string> label = logger.Label(level);
        label.Report(message);
        return label;
    }
}

public interface IDisposableProgress<T> : IProgress<T>, IDisposable
{

}
