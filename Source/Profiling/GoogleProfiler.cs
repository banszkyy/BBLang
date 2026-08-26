using System.IO;
using System.Text.Json;
using LanguageCore.Compiler;
using LanguageCore.Runtime;

namespace LanguageCore.Profiling;

public sealed class GoogleProfiler : Profiler
{
    sealed class Node
    {
        public required int Id;
        public required string FunctionName;
        public string? File;
        public int? Line;
        public int? Column;
        public required Dictionary<string, Node> ChildrenByKey;
        public readonly List<int> ChildIds = new();
    }

    readonly Node _root;
    readonly List<Node> _allNodes = new();
    readonly List<int> _samples = new();
    readonly List<TimeSpan> _timeDeltas = new();
    int _nextNodeId = 1;
    TimeSpan _lastTimestamp;
    TimeSpan _startTs;
    bool _first = true;

    public GoogleProfiler(CompiledDebugInformation debugInformation, double tickToMicroseconds = 1.0) : base(debugInformation, tickToMicroseconds)
    {
        _root = NewNode(null, "(root)", null, null);
    }

    Node NewNode(Node? parent, string functionName, string? file, int? line)
    {
        Node node = new()
        {
            Id = _nextNodeId++,
            FunctionName = functionName,
            File = file,
            Line = line,
            ChildrenByKey = new()
        };
        _allNodes.Add(node);
        parent?.ChildIds.Add(node.Id);
        return node;
    }

    string GetFrameName(CallTraceItem item)
    {
        if (_debugInformation.TryGetFunctionInformation(item.InstructionPointer, out FunctionInformation f))
        {
            if (f.IsTopLevelStub) return "[top_level_statements]";
            else if (f.Function is CompiledLambda) return "[lambda]";
            else if (f.Function is CompiledFunctionDefinition w) return w.Identifier;
        }

        return "[unknown]";
    }

    Location GetLocation(CallTraceItem item)
    {
        if (_debugInformation.TryGetFunctionInformation(item.InstructionPointer, out FunctionInformation f))
        {
            if (f.File is null)
            {
                return default;
            }
            return new Location(f.SourcePosition, f.File);
        }

        if (_debugInformation.TryGetSourceLocation(item.InstructionPointer, out SourceCodeLocation l, true))
        {
            return l.Location;
        }

        return default;
    }

    public override void Sample(in ProcessorState state, ulong tick)
    {
        List<CallTraceItem> stacktrace = new();
        DebugUtils.TraceStack(state.Memory, state.Registers.BasePointer, _debugInformation.StackOffsets, stacktrace);

        stacktrace.Insert(0, new CallTraceItem(state.Registers.BasePointer, state.Registers.CodePointer));

        stacktrace.Reverse();

        TimeSpan ts = TickToTimestamp(tick);

        Node current = _root;
        foreach (CallTraceItem frame in stacktrace)
        {
            string name = GetFrameName(frame);
            Location location = GetLocation(frame);
            string key = $"{name} {(location.IsDefault ? "" : location.Position.Range.Start.Line.ToString())}".TrimEnd();

            if (!current.ChildrenByKey.TryGetValue(key, out Node? child))
            {
                child = NewNode(current, name, location.IsDefault ? null : location.File.IsFile ? location.File.LocalPath : location.File.ToString(), location.IsDefault ? null : location.Position.Range.Start.Line);
                current.ChildrenByKey[key] = child;
            }

            current = child;
        }

        if (_first)
        {
            _startTs = ts;
            _timeDeltas.Add(TimeSpan.Zero);
            _first = false;
        }
        else
        {
            _timeDeltas.Add(ts - _lastTimestamp);
        }

        _samples.Add(current.Id);
        _lastTimestamp = ts;
    }

    public override void WriteTo(string path)
    {
        using FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.Read, 512);
        using Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = false });

        writer.WriteStartObject();

        writer.WriteStartArray("nodes");
        foreach (Node node in _allNodes)
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", node.Id);

            writer.WriteStartObject("callFrame");
            writer.WriteString("functionName", node.FunctionName);
            writer.WriteString("scriptId", "0");
            writer.WriteString("url", node.File ?? "");
            writer.WriteNumber("lineNumber", node.Line ?? -1);
            writer.WriteNumber("columnNumber", node.Column ?? -1);
            writer.WriteEndObject();

            if (node.ChildIds.Count > 0)
            {
                writer.WriteStartArray("children");
                foreach (int childId in node.ChildIds)
                    writer.WriteNumberValue(childId);
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteNumber("startTime", _startTs.TotalMicroseconds);
        writer.WriteNumber("endTime", _lastTimestamp.TotalMicroseconds);

        writer.WriteStartArray("samples");
        foreach (int nodeId in _samples) writer.WriteNumberValue(nodeId);
        writer.WriteEndArray();

        writer.WriteStartArray("timeDeltas");
        foreach (TimeSpan delta in _timeDeltas) writer.WriteNumberValue(delta.TotalMicroseconds);
        writer.WriteEndArray();

        writer.WriteEndObject();
    }
}
