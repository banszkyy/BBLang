using Logger;

namespace LanguageCore;

[ExcludeFromCodeCoverage]
public class PrettyConsoleLogger : IDiagnosticsLogger, ITraceLogger
{
    public bool LogDebugs { get; init; }
    public bool LogInfos { get; init; }
    public bool LogWarnings { get; init; }
    public bool EnableProgress { get; init; }

    public static readonly PrettyConsoleLogger Default = new()
    {
        LogDebugs = false,
        LogInfos = true,
        LogWarnings = true,
        EnableProgress = false,
    };

    bool IsOn(LogType level) => level switch
    {
        LogType.Normal => LogInfos,
        LogType.Warning => LogWarnings,
        LogType.Error => true,
        LogType.Debug => LogDebugs,
        _ => false,
    };

    public void Log(LogType level, string message)
    {
        switch (level)
        {
            case LogType.Normal: LogInfo(message); break;
            case LogType.Warning: LogWarning(message); break;
            case LogType.Error: LogError(message); break;
            case LogType.Debug: LogDebug(message); break;
        }
    }

    public void LogInfo(string message)
    {
        if (!LogInfos) return;
        Logger.Log.Info(message);
    }

    public void LogError(string message)
    {
        Logger.Log.Error(message);
    }

    public void LogError(Exception exception)
    {
        Logger.Log.Error(exception);
    }

    public void LogWarning(string message)
    {
        if (!LogWarnings) return;
        Logger.Log.Warning(message);
    }

    public void LogDebug(string message)
    {
        if (!LogDebugs) return;
        Logger.Log.Debug(message);
    }

    public void LogDiagnostic(DiagnosticAt diagnostic, IEnumerable<ISourceProvider>? sourceProviders = null)
        => LogDiagnostic(diagnostic, 0, sourceProviders);

    public void LogDiagnostic(Diagnostic diagnostic, IEnumerable<ISourceProvider>? sourceProviders = null)
        => LogDiagnostic(diagnostic, 0, sourceProviders);

    void LogDiagnostic(Diagnostic diagnostic, int depth, IEnumerable<ISourceProvider>? sourceProviders = null, Diagnostic? parent = null)
    {
        DiagnosticsLevel level = diagnostic.Level;

        if (parent is not null && parent.Level > level)
        {
            level = parent.Level;
        }

        if (!(level switch
        {
            DiagnosticsLevel.Error => true,
            DiagnosticsLevel.Warning => LogWarnings,
            DiagnosticsLevel.Information => LogInfos,
            DiagnosticsLevel.Hint => LogInfos,
            DiagnosticsLevel.OptimizationNotice => LogInfos,
            DiagnosticsLevel.FailedOptimization => LogInfos,
            _ => false,
        }))
        { return; }

        using Log.AutoScope _ = Logger.Log.Auto();

        ConsoleDiagnosticsLogger.LogDiagnosticImplementation(diagnostic, depth, sourceProviders, parent);
    }

    public IDisposableProgress<float> Progress(LogType level)
    {
        if (!EnableProgress || !IsOn(level))
        {
            return VoidProgress<float>.Instance;
        }

        return new ConsoleProgressBar();
    }

    public IDisposableProgress<string> Label(LogType level)
    {
        if (!EnableProgress || !IsOn(level))
        {
            return VoidProgress<string>.Instance;
        }

        return new ConsoleProgressLabel();
    }

    [ExcludeFromCodeCoverage]
    class ConsoleProgressBar : ProgressBar, IDisposableProgress<float>
    {
        public ConsoleProgressBar() : base()
        {

        }
    }

    [ExcludeFromCodeCoverage]
    class ConsoleProgressLabel : ProgressBar, IDisposableProgress<string>
    {
        public ConsoleProgressLabel() : base()
        {

        }
    }
}
