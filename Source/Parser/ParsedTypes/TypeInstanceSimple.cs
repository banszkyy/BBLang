using LanguageCore.Compiler;
using LanguageCore.Parser.Statements;
using LanguageCore.Tokenizing;

namespace LanguageCore.Parser;

public class TypeInstanceSimple : TypeInstance, IEquatable<TypeInstanceSimple?>, IReferenceableTo
{
    /// <summary> Set by the compiler </summary>
    public GeneralType? CompiledType { get; set; }

    public Token Identifier { get; }
    TokenPair? TypeArgumentsBrackets { get; }
    public ImmutableArray<TypeInstance>? TypeArguments { get; }
    public object? Reference { get; set; }

    public override Position Position => TypeArguments is null
        ? Identifier.Position
        : new Position(Identifier).Union(TypeArguments).Union(TypeArgumentsBrackets);

    public TypeInstanceSimple(Token identifier, Uri file) : base(file)
    {
        Identifier = identifier;
        TypeArguments = null;
        TypeArgumentsBrackets = null;
    }

    public TypeInstanceSimple(Token identifier, Uri file, ImmutableArray<TypeInstance> typeArguments, TokenPair typeArgumentsBrackets) : base(file)
    {
        Identifier = identifier;
        TypeArguments = typeArguments;
        TypeArgumentsBrackets = typeArgumentsBrackets;
    }

    public override bool Equals(object? obj) => obj is TypeInstanceSimple other && Equals(other);
    public override bool Equals(TypeInstance? other) => other is TypeInstanceSimple other_ && Equals(other_);
    public bool Equals(TypeInstanceSimple? other)
    {
        if (other is null) return false;
        if (Identifier.Content != other.Identifier.Content) return false;

        if (!TypeArguments.HasValue) return other.TypeArguments is null;
        if (!other.TypeArguments.HasValue) return false;

        if (TypeArguments.Value.Length != other.TypeArguments.Value.Length) return false;
        for (int i = 0; i < TypeArguments.Value.Length; i++)
        {
            if (!TypeArguments.Value[i].Equals(other.TypeArguments.Value[i]))
            { return false; }
        }
        return true;
    }

    public override int GetHashCode() => HashCode.Combine((byte)3, Identifier, TypeArguments);

    public static TypeInstanceSimple CreateAnonymous(string name, Uri file)
        => new(Token.CreateAnonymous(name), file);

    public static TypeInstanceSimple CreateAnonymous(string name, Uri file, ImmutableArray<Token>? typeArguments)
    {
        if (typeArguments == null)
        {
            return new TypeInstanceSimple(Token.CreateAnonymous(name), file);
        }
        else
        {
            TypeInstance[] genericTypesConverted = new TypeInstance[typeArguments.Value.Length];
            for (int i = 0; i < typeArguments.Value.Length; i++)
            {
                genericTypesConverted[i] = CreateAnonymous(typeArguments.Value[i].Content, file);
            }
            return new TypeInstanceSimple(Token.CreateAnonymous(name), file, genericTypesConverted.ToImmutableArray(), TokenPair.CreateAnonymous("<", ">"));
        }
    }

    public override string ToString()
    {
        if (TypeArguments is null) return Identifier.Content;
        return $"{Identifier.Content}<{string.Join<TypeInstance>(", ", TypeArguments)}>";
    }
    public override string ToString(IReadOnlyDictionary<string, GeneralType>? typeArguments)
    {
        string identifier = Identifier.Content;
        if (typeArguments is not null && typeArguments.TryGetValue(Identifier.Content, out GeneralType? replaced))
        { identifier = replaced.ToString(); }

        if (!TypeArguments.HasValue)
        { return identifier; }

        StringBuilder result = new(identifier);
        result.Append('<');
        for (int i = 0; i < TypeArguments.Value.Length; i++)
        {
            if (i > 0) result.Append(", ");
            result.Append(TypeArguments.Value[i].ToString(typeArguments));
        }
        result.Append('>');
        return result.ToString();
    }
}
