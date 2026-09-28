namespace LanguageCore;

public interface IDiagnosticsLogger
{
    void LogDiagnostic(DiagnosticAt diagnostic, IEnumerable<ISourceProvider>? sourceProviders);
    void LogDiagnostic(Diagnostic diagnostic, IEnumerable<ISourceProvider>? sourceProviders);
}
