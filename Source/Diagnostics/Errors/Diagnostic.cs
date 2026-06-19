namespace LanguageCore;

[ExcludeFromCodeCoverage]
public class Diagnostic : IEquatable<Diagnostic>
{
    public DiagnosticsLevel Level { get; }
    public string Message { get; }
    public ImmutableArray<DiagnosticRelatedInformation> RelatedInformation { get; }
    public ImmutableArray<Diagnostic> SubErrors { get; }
    public bool IgnoreOnPartialSource { get; }

#if DEBUG && !UNITY
    bool IsDebugged;
#endif

    protected Diagnostic(DiagnosticsLevel level, string message, bool @break, bool ignoreOnPartialSource, ImmutableArray<Diagnostic> suberrors, ImmutableArray<DiagnosticRelatedInformation> relatedInformation)
    {
        Level = level;
        Message = message;
        SubErrors = suberrors;
        RelatedInformation = relatedInformation;
        IgnoreOnPartialSource = ignoreOnPartialSource;

        if (@break)
        { Break(); }
    }

    public Diagnostic(DiagnosticsLevel level, string message, ImmutableArray<Diagnostic> suberrors, ImmutableArray<DiagnosticRelatedInformation> relatedInformation)
        : this(level, message, level == DiagnosticsLevel.Error, false, suberrors, relatedInformation) { }

    public virtual Diagnostic WithSuberrors(Diagnostic? suberror) => suberror is null ? this : new(Level, Message, false, IgnoreOnPartialSource, ImmutableArray.Create(suberror), RelatedInformation);
    public virtual Diagnostic WithSuberrors(params Diagnostic?[] suberrors) => WithSuberrors(suberrors.Where(v => v is not null).ToImmutableArray()!);
    public virtual Diagnostic WithSuberrors(IEnumerable<Diagnostic?> suberrors) => WithSuberrors(suberrors.Where(v => v is not null).ToImmutableArray()!);
    public virtual Diagnostic WithSuberrors(ImmutableArray<Diagnostic> suberrors) => suberrors.IsDefaultOrEmpty ? this : new(Level, Message, false, IgnoreOnPartialSource, SubErrors.AddRange(suberrors), RelatedInformation);

    public virtual Diagnostic WithRelatedInfo(DiagnosticRelatedInformation? relatedInfo) => relatedInfo is null ? this : new(Level, Message, false, IgnoreOnPartialSource, SubErrors, ImmutableArray.Create(relatedInfo));
    public virtual Diagnostic WithRelatedInfo(params DiagnosticRelatedInformation?[] relatedInfo) => WithRelatedInfo(relatedInfo.Where(v => v is not null).ToImmutableArray()!);
    public virtual Diagnostic WithRelatedInfo(IEnumerable<DiagnosticRelatedInformation?> relatedInfo) => WithRelatedInfo(relatedInfo.Where(v => v is not null).ToImmutableArray()!);
    public virtual Diagnostic WithRelatedInfo(ImmutableArray<DiagnosticRelatedInformation> relatedInfo) => relatedInfo.IsDefaultOrEmpty ? this : new(Level, Message, false, IgnoreOnPartialSource, SubErrors, RelatedInformation.AddRange(relatedInfo));

    [DoesNotReturn]
    public virtual void Throw() => throw ToException();

    public virtual LanguageException ToException() => new(Message, SubErrors.ToImmutableArray(v => v.ToException() as Exception));

    public static Diagnostic Internal(string message, bool @break = true)
        => new(DiagnosticsLevel.Error, message, @break, false, ImmutableArray<Diagnostic>.Empty, ImmutableArray<DiagnosticRelatedInformation>.Empty);

    public static Diagnostic Error(string message, bool @break = true, bool ignoreOnPartialSource = false)
        => new(DiagnosticsLevel.Error, message, @break, ignoreOnPartialSource, ImmutableArray<Diagnostic>.Empty, ImmutableArray<DiagnosticRelatedInformation>.Empty);

    public static Diagnostic Warning(string message, bool @break = true, bool ignoreOnPartialSource = false)
        => new(DiagnosticsLevel.Warning, message, @break, ignoreOnPartialSource, ImmutableArray<Diagnostic>.Empty, ImmutableArray<DiagnosticRelatedInformation>.Empty);

    public virtual Diagnostic Break()
    {
#if DEBUG && !UNITY
        if (!IsDebugged)
        {
            Debugger.Break();
        }
        IsDebugged = true;
#endif

#if TESTING
        //Throw();
#endif

        return this;
    }

    public override string ToString() => Message;

    public bool Equals([NotNullWhen(true)] Diagnostic? other)
    {
        if (other is null) return false;
        if (Message != other.Message) return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is Diagnostic other && Equals(other);

    public override int GetHashCode() => Message.GetHashCode();

    public static IEnumerable<Diagnostic> EnumerateAll(Diagnostic diagnostic)
    {
        yield return diagnostic;
        foreach (Diagnostic sub in diagnostic.SubErrors.SelectMany(EnumerateAll))
        {
            yield return sub;
        }
    }

    public static Diagnostic FromException(Exception exception) => exception switch
    {
        LanguageExceptionAt ex => ex.ToDiagnostic(),
        LanguageException ex => ex.ToDiagnostic(),
        AggregateException ex => new Diagnostic(DiagnosticsLevel.Error, ex.Message, false, false, ex.InnerExceptions is null ? ImmutableArray<Diagnostic>.Empty : ex.InnerExceptions.ToImmutableArray(FromException), ImmutableArray<DiagnosticRelatedInformation>.Empty),
        _ => new Diagnostic(DiagnosticsLevel.Error, exception.Message, false, false, exception.InnerException is null ? ImmutableArray<Diagnostic>.Empty : ImmutableArray.Create(FromException(exception.InnerException)), ImmutableArray<DiagnosticRelatedInformation>.Empty)
    };
}
