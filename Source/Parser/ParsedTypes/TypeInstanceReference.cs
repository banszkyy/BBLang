using LanguageCore.Compiler;
using LanguageCore.Tokenizing;

namespace LanguageCore.Parser;

public class TypeInstanceReference : TypeInstance, IEquatable<TypeInstanceReference?>
{
    /// <summary> Set by the compiler </summary>
    public ReferenceType? CompiledType { get; set; }

    public TypeInstance To { get; }
    public Token Operator { get; }

    public override Position Position => new(To, Operator);

    public TypeInstanceReference(TypeInstance to, Token @operator, Uri file) : base(file)
    {
        To = to;
        Operator = @operator;
    }

    public override bool Equals(object? obj) => obj is TypeInstanceReference other && Equals(other);
    public override bool Equals(TypeInstance? other) => other is TypeInstanceReference other_ && Equals(other_);
    public bool Equals(TypeInstanceReference? other)
    {
        if (other is null) return false;
        return To.Equals(other.To);
    }

    public override int GetHashCode() => HashCode.Combine((byte)5, To);

    public override string ToString() => $"{To}{Operator}";
    public override string ToString(IReadOnlyDictionary<string, GeneralType>? typeArguments) => $"{To.ToString(typeArguments)}{Operator}";

    public static TypeInstanceReference CreateAnonymous(TypeInstance to, Uri file) => new(to, Token.CreateAnonymous("&", TokenType.Operator), file);
}
