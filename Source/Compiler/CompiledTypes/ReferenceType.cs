using LanguageCore.Parser;

namespace LanguageCore.Compiler;

public class ReferenceType : GeneralType,
    IEquatable<ReferenceType>,
    IReferenceType
{
    public GeneralType To { get; }

    public ReferenceType(GeneralType to)
    {
        To = to;
    }

    public override bool Equals(object? other) => Equals(other as ReferenceType);
    public override bool Equals(GeneralType? other) => Equals(other as ReferenceType);
    public bool Equals(ReferenceType? other)
    {
        if (other is null) return false;
        if (!To.Equals(other.To)) return false;
        return true;
    }
    public override bool Equals(TypeInstance? other)
    {
        if (other is null) return false;
        if (other is not TypeInstanceReference otherReference) return false;
        if (!To.Equals(otherReference.To)) return false;
        return true;
    }
    public override int GetHashCode() => HashCode.Combine(To);
    public override string ToString() => $"{To}&";
}
