namespace LanguageCore.Parser;

class OrderedDiagnosticCollection : IEnumerable<OrderedDiagnostic>
{
    readonly List<OrderedDiagnostic> _diagnostics;

    public OrderedDiagnosticCollection()
    {
        _diagnostics = new();
    }

    public void Add(int importance, DiagnosticAt diagnostic, ImmutableArray<OrderedDiagnostic> subdiagnostic)
    {
        Add(new OrderedDiagnostic(importance, diagnostic, subdiagnostic));
    }

    public void Add(int importance, DiagnosticAt diagnostic)
    {
        Add(new OrderedDiagnostic(importance, diagnostic, ImmutableArray<OrderedDiagnostic>.Empty));
    }

    public void Add(OrderedDiagnostic diagnostic)
    {
        int index = _diagnostics.BinarySearch(diagnostic);
        _diagnostics.Insert(index < 0 ? ~index : index, diagnostic);
    }

    static DiagnosticAt Compile(OrderedDiagnostic diagnostic) => new(
        diagnostic.Diagnostic.Level,
        diagnostic.Diagnostic.Message,
        diagnostic.Diagnostic.Position,
        diagnostic.Diagnostic.File,
        false,
        diagnostic.Diagnostic.IgnoreOnPartialSource,
        diagnostic.SubDiagnostics.ToImmutableArray(Compile),
        diagnostic.Diagnostic.RelatedInformation,
        diagnostic.Diagnostic.Tag
    );

    public ImmutableArray<DiagnosticAt> Compile()
    {
        if (_diagnostics.Count == 0) return ImmutableArray<DiagnosticAt>.Empty;
        int max = _diagnostics.Max(v => v.Importance);
        ImmutableArray<DiagnosticAt>.Builder result = ImmutableArray.CreateBuilder<DiagnosticAt>(_diagnostics.Count);
        for (int i = 0; i < _diagnostics.Count; i++)
        {
            if (_diagnostics[i].Importance < max) continue;
            result.Add(Compile(_diagnostics[i]));
        }
        return result.DrainToImmutable();
    }

    public IEnumerator<OrderedDiagnostic> GetEnumerator() => _diagnostics.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _diagnostics.GetEnumerator();
}

readonly struct OrderedDiagnostic : IComparable<OrderedDiagnostic>
{
    public int Importance { get; }
    public DiagnosticAt Diagnostic { get; }
    public ImmutableArray<OrderedDiagnostic> SubDiagnostics { get; }

    public OrderedDiagnostic(int importance, DiagnosticAt diagnostic)
    {
        Importance = importance;
        Diagnostic = diagnostic;
        SubDiagnostics = ImmutableArray<OrderedDiagnostic>.Empty;
    }

    public OrderedDiagnostic(int importance, DiagnosticAt diagnostic, ImmutableArray<OrderedDiagnostic> subdiagnostics)
    {
        Importance = importance;
        Diagnostic = diagnostic;
        SubDiagnostics = subdiagnostics;
    }

    public OrderedDiagnostic(int importance, DiagnosticAt diagnostic, params OrderedDiagnostic[] subdiagnostic)
        : this(importance, diagnostic, subdiagnostic.ToImmutableArray()) { }

    public int CompareTo(OrderedDiagnostic other) => Importance.CompareTo(other.Importance);
}
