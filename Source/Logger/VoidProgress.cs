namespace LanguageCore;

public class VoidProgress<T> : IDisposableProgress<T>
{
    public static VoidProgress<T> Instance = new();
    public void Dispose() { }
    public void Report(T value) { }
}
