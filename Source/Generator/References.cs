namespace LanguageCore.Compiler;

public static class ReferenceExtensions
{
    public static void AddReference<TSource>(this IReferenceable<TSource> references, TSource source, Location sourceLocation, bool isImplicit = false)
        => references.References.Add(new Reference<TSource>(source, sourceLocation, isImplicit));

    public static void AddReference<TSource>(this IReferenceable<TSource> references, TSource source, bool isImplicit = false)
        where TSource : ILocated
        => references.References.Add(new Reference<TSource>(source, source.Location, isImplicit));
}

public readonly struct Reference
{
    public Location SourceLocation { get; }
    public bool IsImplicit { get; }

    public Reference(Location sourceLocation, bool isImplicit = false)
    {
        SourceLocation = sourceLocation;
        IsImplicit = isImplicit;
    }
}

public readonly struct Reference<TSource>
{
    public TSource Source { get; }
    public Location SourceLocation { get; }
    public bool IsImplicit { get; }

    public Reference(TSource source, Location sourceLocation, bool isImplicit = false)
    {
        Source = source;
        SourceLocation = sourceLocation;
        IsImplicit = isImplicit;
    }

    public static implicit operator Reference(Reference<TSource> v) => new(v.SourceLocation, v.IsImplicit);
}

public interface IReferenceable
{
    IEnumerable<Reference> References { get; }
}

public interface IReferenceable<TBy> : IReferenceable
{
    new List<Reference<TBy>> References { get; }
    IEnumerable<Reference> IReferenceable.References => References.Select(v => (Reference)v);
}
