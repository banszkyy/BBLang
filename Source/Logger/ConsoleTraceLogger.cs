namespace LanguageCore;

[ExcludeFromCodeCoverage]
public class ConsoleTraceLogger : ITraceLogger
{
    public bool LogDebugs { get; init; }
    public bool LogInfos { get; init; }
    public bool LogWarnings { get; init; }
    public bool EnableProgress { get; init; }

    public static readonly ConsoleTraceLogger Default = new()
    {
        LogDebugs = false,
        LogInfos = true,
        LogWarnings = true,
        EnableProgress = false,
    };

    public void Log(LogType level, string message) => Console.WriteLine($"{level}: {message}");

    public IDisposableProgress<float> Progress(LogType level) => VoidProgress<float>.Instance;
    public IDisposableProgress<string> Label(LogType level) => VoidProgress<string>.Instance;
}
