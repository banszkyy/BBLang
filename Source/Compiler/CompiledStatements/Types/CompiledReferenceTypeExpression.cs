using LanguageCore.Parser;

namespace LanguageCore.Compiler;

public class CompiledReferenceTypeExpression : CompiledTypeExpression,
    IEquatable<CompiledReferenceTypeExpression>
{
    public CompiledTypeExpression To { get; }

    [SetsRequiredMembers]
    public CompiledReferenceTypeExpression(CompiledTypeExpression to, Location location) : base(location)
    {
        To = to;
    }

    public override bool Equals(object? other) => Equals(other as CompiledReferenceTypeExpression);
    public override bool Equals(CompiledTypeExpression? other) => Equals(other as CompiledReferenceTypeExpression);
    public bool Equals(CompiledReferenceTypeExpression? other)
    {
        if (other is null) return false;
        if (!To.Equals(other.To)) return false;
        return true;
    }
    public override bool Equals(TypeInstance? other)
    {
        if (other is null) return false;
        if (other is not TypeInstanceReference otherPointer) return false;
        if (!To.Equals(otherPointer.To)) return false;
        return true;
    }
    public override int GetHashCode() => HashCode.Combine(To);
    public override string ToString() => $"{To}&";
    public override string Stringify(int depth = 0) => $"{To.Stringify(depth)}&";

    public static CompiledReferenceTypeExpression CreateAnonymous(ReferenceType type, ILocated location)
    {
        return new(CreateAnonymous(type.To, location), location.Location);
    }
}
