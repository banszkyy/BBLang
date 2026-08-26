using LanguageCore.Runtime;

namespace LanguageCore.Profiling;

public abstract class Profiler
{
    protected readonly CompiledDebugInformation _debugInformation;
    readonly double _tickToMicroseconds;

    public Profiler(CompiledDebugInformation debugInformation, double tickToMicroseconds = 1.0)
    {
        _tickToMicroseconds = tickToMicroseconds;
        _debugInformation = debugInformation;
    }

    protected TimeSpan TickToTimestamp(ulong ticks) => TimeSpan.FromMicroseconds(ticks * _tickToMicroseconds);

    public abstract void Sample(in ProcessorState state, ulong tick);
    public abstract void WriteTo(string path);
}
