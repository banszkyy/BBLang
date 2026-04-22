using LanguageCore.Parser;

namespace LanguageCore.Compiler;

public class CompiledEnumTypeExpression : CompiledTypeExpression,
    IEquatable<CompiledEnumTypeExpression>,
    IReferenceableTo<CompiledEnum>
{
    public CompiledEnum Definition { get; }

    CompiledEnum? IReferenceableTo<CompiledEnum>.Reference
    {
        get => Definition;
        set => throw new InvalidOperationException();
    }

    [SetsRequiredMembers]
    public CompiledEnumTypeExpression(CompiledEnumTypeExpression other) : base(other.Location)
    {
        Definition = other.Definition;
    }

    [SetsRequiredMembers]
    public CompiledEnumTypeExpression(CompiledEnum @enum, Location location) : base(location)
    {
        Definition = @enum;
    }
    public override bool Equals(object? other) => Equals(other as CompiledEnumTypeExpression);
    public override bool Equals(CompiledTypeExpression? other) => Equals(other as CompiledEnumTypeExpression);
    public bool Equals(CompiledEnumTypeExpression? other)
    {
        if (other is null) return false;
        if (!Utils.ReferenceEquals(Definition, other.Definition)) return false;
        return true;
    }
    public override bool Equals(TypeInstance? other)
    {
        if (other is null) return false;

        if (other is not TypeInstanceSimple otherSimple)
        { return false; }

        if (TypeKeywords.BasicTypes.ContainsKey(otherSimple.Identifier.Content))
        { return false; }

        if (Definition.Identifier == otherSimple.Identifier.Content)
        { return true; }

        return false;
    }
    public override int GetHashCode() => Definition.GetHashCode();
    public override string ToString() => Definition.Identifier;

    public static CompiledEnumTypeExpression CreateAnonymous(EnumType type, ILocated location)
    {
        return new(
            type.Definition,
            location.Location
        );
    }
}
