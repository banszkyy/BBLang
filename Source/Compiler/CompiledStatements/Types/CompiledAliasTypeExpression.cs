using LanguageCore.Parser;

namespace LanguageCore.Compiler;

public class CompiledAliasTypeExpression : CompiledTypeExpression,
    IEquatable<CompiledAliasTypeExpression>
{
    public CompiledTypeExpression Value => Definition.Value;
    public CompiledAlias Definition { get; }

    public override CompiledTypeExpression FinalValue => Value is CompiledAliasTypeExpression aliasType ? aliasType.FinalValue : Value;
    public string Identifier => Definition.Identifier;

    [SetsRequiredMembers]
    public CompiledAliasTypeExpression(CompiledAlias definition, Location location) : base(location)
    {
        Definition = definition;
    }

    public override bool Equals(object? other) => Equals(other as CompiledAliasTypeExpression);
    public override bool Equals(CompiledTypeExpression? other) => Equals(other as CompiledAliasTypeExpression);
    public bool Equals(CompiledAliasTypeExpression? other)
    {
        if (other is null) return false;
        if (!other.Identifier.Equals(Identifier)) return false;
        return true;
    }
    public override bool Equals(TypeInstance? other)
    {
        if (other is null) return false;
        if (other is not TypeInstanceSimple otherSimple) return false;
        if (!Identifier.Equals(otherSimple.Identifier.Content)) return false;
        return true;
    }
    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => Identifier;

    public static CompiledAliasTypeExpression CreateAnonymous(AliasType type, ILocated location) => new(
        type.Definition,
        location.Location
    );
}
