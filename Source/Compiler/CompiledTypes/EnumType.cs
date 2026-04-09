using LanguageCore.Parser;

namespace LanguageCore.Compiler;

public class EnumType : GeneralType,
    IEquatable<EnumType>
{
    public CompiledEnum Definition { get; }

    public string Identifier => Definition.Identifier;

    public EnumType(CompiledEnum definition)
    {
        Definition = definition;
    }

    public override bool Equals(object? other) => Equals(other as EnumType);
    public override bool Equals(GeneralType? other) => Equals(other as EnumType);
    public bool Equals(EnumType? other)
    {
        if (other is null) return false;
        if (!Utils.ReferenceEquals(other.Definition, Definition)) return false;
        return true;
    }
    public override bool Equals(TypeInstance? other)
    {
        if (other is null) return false;
        if (other is not TypeInstanceSimple otherSimple) return false;
        if (!Identifier.Equals(otherSimple.Identifier.Content)) return false;
        return true;
    }
    public override int GetHashCode() => Definition.GetHashCode();

    public override string ToString() => Identifier;
}
