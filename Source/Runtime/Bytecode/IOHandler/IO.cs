namespace LanguageCore.Runtime;

public abstract class IO : IDisposable
{
    public virtual void Dispose() { }
    public abstract void Register(List<IExternalFunction> externalFunctions);
}
