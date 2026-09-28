namespace LanguageCore;

public class VoidLogger : ITraceLogger, IDiagnosticsLogger
{
    public static readonly VoidLogger Instance = new();

    public IDisposableProgress<string> Label(LogType level) => VoidProgress<string>.Instance;
    public void Log(LogType level, string message) { }
    public IDisposableProgress<float> Progress(LogType level) => VoidProgress<float>.Instance;
    public void LogDiagnostic(DiagnosticAt diagnostic, IEnumerable<ISourceProvider>? sourceProviders) { }
    public void LogDiagnostic(Diagnostic diagnostic, IEnumerable<ISourceProvider>? sourceProviders) { }
}
