using System.IO;

namespace LanguageCore;

public interface IReadOnlyDiagnosticsCollection
{
    IReadOnlyCollection<DiagnosticAt> Diagnostics { get; }
    IReadOnlyCollection<Diagnostic> DiagnosticsWithoutContext { get; }
    bool HasErrors { get; }

    void Throw();
}

[ExcludeFromCodeCoverage]
public class DiagnosticsCollection : IReadOnlyDiagnosticsCollection
{
    readonly List<DiagnosticAt> _diagnostics;
    readonly List<Diagnostic> _diagnosticsWithoutContext;
    readonly Stack<Override> _overrides;

    public IReadOnlyCollection<DiagnosticAt> Diagnostics => _diagnostics;
    public IReadOnlyCollection<Diagnostic> DiagnosticsWithoutContext => _diagnosticsWithoutContext;

    public readonly struct Override : IDisposable
    {
        public DiagnosticsCollection Collection { get; }
        public DiagnosticsCollection Base { get; }

        public Override(DiagnosticsCollection @base, DiagnosticsCollection? collection)
        {
            Collection = collection ?? new();
            Base = @base;
        }

        public void Apply()
        {
            Base.AddRange(Collection);
            Collection.Clear();
        }

        public void Dispose()
        {
            Override r = Base._overrides.Pop();
            if (!Utils.ReferenceEquals(r.Collection, Collection)) throw new UnreachableException();
        }
    }

    public Override MakeOverride(DiagnosticsCollection? diagnostics = null)
    {
        Override r = new(this, diagnostics);
        _overrides.Push(r);
        return r;
    }

    public bool HasErrors =>
        _diagnostics.Any(v => v.Level == DiagnosticsLevel.Error) ||
        _diagnosticsWithoutContext.Any(v => v.Level == DiagnosticsLevel.Error);

    public bool Has(DiagnosticsLevel level) =>
        _diagnostics.Any(v => v.Level == level) ||
        _diagnosticsWithoutContext.Any(v => v.Level == level);

    public DiagnosticsCollection()
    {
        _diagnostics = new();
        _diagnosticsWithoutContext = new();
        _overrides = new();
    }

    public DiagnosticsCollection(DiagnosticsCollection other)
    {
        _diagnostics = new(other._diagnostics);
        _diagnosticsWithoutContext = new(other._diagnosticsWithoutContext);
        _overrides = new();
    }

    public void Throw()
    {
        LanguageException[] exceptions = _diagnostics.Where(v => v.Level == DiagnosticsLevel.Error).Select(v => v.ToException())
            .Concat(_diagnosticsWithoutContext.Where(v => v.Level == DiagnosticsLevel.Error).Select(v => v.ToException()))
            .ToArray();
        if (exceptions.Length == 0) return;
        throw new AggregateException(exceptions);
    }

    public void Clear()
    {
        _diagnostics.Clear();
        _diagnosticsWithoutContext.Clear();
    }

    public void AddRange(DiagnosticsCollection other)
    {
        AddRange(other._diagnostics);
        AddRange(other._diagnosticsWithoutContext);
    }

    public void Add(DiagnosticAt? diagnostic)
    {
        if (diagnostic is null) return;

        if (_diagnostics.Any(v => v.Equals(diagnostic)))
        { return; }
        _diagnostics.Add(diagnostic);
    }

    public void Add(Diagnostic? diagnostic)
    {
        switch (diagnostic)
        {
            case DiagnosticAt v: Add(v); break;
            case Diagnostic v:
                if (_diagnosticsWithoutContext.Any(v => v.Equals(v)))
                { return; }
                _diagnosticsWithoutContext.Add(v); break;
            case null: break;
            default: throw new UnreachableException();
        }
    }

    public bool Update(DiagnosticAt old, DiagnosticAt diagnostic)
    {
        if (diagnostic is null) return false;

        for (int i = 0; i < _diagnostics.Count; i++)
        {
            if (Utils.ReferenceEquals(_diagnostics[i], old))
            {
                _diagnostics[i] = diagnostic;
                return true;
            }
        }

        return false;
    }

    public void AddRange(IEnumerable<DiagnosticAt> diagnostic)
    { foreach (DiagnosticAt item in diagnostic) Add(item); }

    public void AddRange(IEnumerable<Diagnostic> diagnostic)
    { foreach (Diagnostic item in diagnostic) Add(item); }
}

public static class DiagnosticsCollectionExtensions
{
    public static void Print(this IReadOnlyDiagnosticsCollection diagnosticsCollection, ILogger logger, IEnumerable<ISourceProvider>? sourceProviders = null)
    {
        foreach (Diagnostic diagnostic in diagnosticsCollection.DiagnosticsWithoutContext)
        {
            switch (diagnostic.Level)
            {
                case DiagnosticsLevel.Error:
                    logger.Log(LogType.Error, diagnostic.Message);
                    break;
                case DiagnosticsLevel.Warning:
                    logger.Log(LogType.Warning, diagnostic.Message);
                    break;
                case DiagnosticsLevel.Information:
                    logger.Log(LogType.Normal, diagnostic.Message);
                    break;
                case DiagnosticsLevel.Hint:
                    logger.Log(LogType.Normal, diagnostic.Message);
                    break;
            }
        }

        foreach (DiagnosticAt diagnostic in diagnosticsCollection.Diagnostics)
        {
            logger.LogDiagnostic(diagnostic, sourceProviders);
        }
    }

    static void WriteTo(Diagnostic diagnostic, StringBuilder writer, int depth)
    {
        writer.Append(' ', depth * 2);
        writer.AppendLine(diagnostic.ToString());
        foreach (Diagnostic item in diagnostic.SubErrors)
        {
            WriteTo(item, writer, depth + 1);
        }
    }

    public static void WriteErrorsTo(this IReadOnlyDiagnosticsCollection diagnosticsCollection, StringBuilder writer)
    {
        foreach (Diagnostic diagnostic in diagnosticsCollection.DiagnosticsWithoutContext)
        {
            if (diagnostic.Level != DiagnosticsLevel.Error) continue;
            WriteTo(diagnostic, writer, 0);
        }

        foreach (DiagnosticAt diagnostic in diagnosticsCollection.Diagnostics)
        {
            if (diagnostic.Level != DiagnosticsLevel.Error) continue;
            WriteTo(diagnostic, writer, 0);
        }
    }

    static void WriteTo(Diagnostic diagnostic, TextWriter writer, int depth)
    {
        writer.Write(new string(' ', depth * 2));
        writer.WriteLine(diagnostic.ToString());
        foreach (Diagnostic item in diagnostic.SubErrors)
        {
            WriteTo(item, writer, depth + 1);
        }
    }

    public static void WriteErrorsTo(this IReadOnlyDiagnosticsCollection diagnosticsCollection, TextWriter writer)
    {
        foreach (Diagnostic diagnostic in diagnosticsCollection.DiagnosticsWithoutContext)
        {
            if (diagnostic.Level != DiagnosticsLevel.Error) continue;
            WriteTo(diagnostic, writer, 0);
        }

        foreach (DiagnosticAt diagnostic in diagnosticsCollection.Diagnostics)
        {
            if (diagnostic.Level != DiagnosticsLevel.Error) continue;
            WriteTo(diagnostic, writer, 0);
        }
    }
}
