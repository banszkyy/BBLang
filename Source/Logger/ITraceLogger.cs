namespace LanguageCore;

public interface ITraceLogger
{
    void Log(LogType level, string message);

    IDisposableProgress<float> Progress(LogType level);

    IDisposableProgress<string> Label(LogType level);
}
