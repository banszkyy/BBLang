using System.IO;
using LanguageCore.Compiler;
using LanguageCore.Runtime;

namespace LanguageCore.Profiling;

public class PerfProfiler : Profiler
{
    sealed class ProfilerSample : IEquatable<ProfilerSample>
    {
        public ulong Tick { get; }
        public ImmutableArray<ProfilerStackTraceItem> Trace { get; }

        public ProfilerSample(ulong tick, ImmutableArray<ProfilerStackTraceItem> trace)
        {
            Tick = tick;
            Trace = trace;
        }

        public override int GetHashCode() => base.GetHashCode();
        public override bool Equals(object? obj) => Equals(obj as ProfilerSample);
        public bool Equals(ProfilerSample? other) => other is not null && Tick == other.Tick && Trace.SequenceEqual(other.Trace, (a, b) => a.Equals(b));
    }

    readonly struct ProfilerStackTraceItem : IEquatable<ProfilerStackTraceItem>
    {
        public int Instruction { get; }
        public string FunctionName { get; }
        public string? Source { get; }

        public ProfilerStackTraceItem(int instruction, string functionName, string? source)
        {
            Instruction = instruction;
            FunctionName = functionName;
            Source = source;
        }

        public override bool Equals(object? obj) => obj is ProfilerStackTraceItem item && Equals(item);
        public bool Equals(ProfilerStackTraceItem other) =>
            Instruction == other.Instruction
            && FunctionName == other.FunctionName
            && Source == other.Source;
        public override int GetHashCode() => base.GetHashCode();

        public static bool operator ==(ProfilerStackTraceItem left, ProfilerStackTraceItem right) => left.Equals(right);
        public static bool operator !=(ProfilerStackTraceItem left, ProfilerStackTraceItem right) => !left.Equals(right);
    }

    readonly List<ProfilerSample> _samples = new();

    public PerfProfiler(CompiledDebugInformation debugInformation, double tickToMicroseconds = 1.0) : base(debugInformation, tickToMicroseconds)
    {

    }

    public override void Sample(in ProcessorState state, ulong tick)
    {
        CompiledDebugInformation debugInformation = _debugInformation;

        List<CallTraceItem> callTrace = new();
        DebugUtils.TraceStack(state.Memory, state.Registers.BasePointer, debugInformation.StackOffsets, callTrace);

        List<ProfilerStackTraceItem> profilerStackTraceItems = new(callTrace.Count);
        foreach (CallTraceItem item in callTrace)
        {
            string name = "[unknown]";
            string? source = null;

            if (debugInformation.TryGetFunctionInformation(item.InstructionPointer, out FunctionInformation f))
            {
                if (f.IsTopLevelStub) name = "[top level statements]";
                else if (f.Function is CompiledLambda) name = "[lambda]";
                else if (f.Function is CompiledFunctionDefinition w) name = w.Identifier;

                source = f.File is null ? null : new Location(f.SourcePosition, f.File).ToString();
            }

            profilerStackTraceItems.Add(new(item.InstructionPointer, name, source));
        }

        if (_samples.Count > 0 && _samples[^1].Equals(new(_samples[^1].Tick, profilerStackTraceItems.ToImmutableArray()))) return;

        _samples.Add(new ProfilerSample(tick, profilerStackTraceItems.ToImmutableArray()));
    }

    public override void WriteTo(string filename)
    {
        using FileStream file = new(filename, FileMode.Create, FileAccess.Write);
        using StreamWriter writer = new(file);

        foreach (ProfilerSample sample in _samples)
        {
            writer.WriteLine($"bblang 621/621 {TickToTimestamp(sample.Tick).TotalMilliseconds.ToString("0.000000", CultureInfo.InvariantCulture)}: 0 cpu/cycles/Pu:");
            foreach (ProfilerStackTraceItem item in sample.Trace)
            {
                writer.WriteLine($"	    {Convert.ToString(item.Instruction, 16)} {item.FunctionName} ({item.Source})");
            }
            writer.WriteLine();
        }
    }
}
