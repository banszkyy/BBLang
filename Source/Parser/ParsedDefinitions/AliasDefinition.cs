using LanguageCore.Compiler;
using LanguageCore.Tokenizing;

namespace LanguageCore.Parser;

public class AliasDefinition :
    IPositioned,
    IInFile,
    ILocated,
    IIdentifiable<Token>,
    IExportable,
    IHaveAttributes
{
    public ImmutableArray<AttributeUsage> Attributes { get; }
    public ImmutableArray<Token> Modifiers { get; }
    public Token Keyword { get; }
    public Token Identifier { get; }
    public TypeInstance Value { get; }
    public Uri File { get; }

    public Position Position => new(Keyword, Identifier, Value);
    public Location Location => new(Position, File);
    public bool IsExported => Modifiers.Contains(ProtectionKeywords.Export);

    CanUseOn IHaveAttributes.AttributeUsageKind => CanUseOn.TypeAlias;

    public AliasDefinition(ImmutableArray<AttributeUsage> attributes, ImmutableArray<Token> modifiers, Token keyword, Token identifier, TypeInstance value, Uri file)
    {
        Attributes = attributes;
        Modifiers = modifiers;
        Keyword = keyword;
        Identifier = identifier;
        Value = value;
        File = file;
    }

    public AliasDefinition(AliasDefinition other)
    {
        Attributes = other.Attributes;
        Modifiers = other.Modifiers;
        Keyword = other.Keyword;
        Identifier = other.Identifier;
        Value = other.Value;
        File = other.File;
    }

    public override string ToString() => $"{DeclarationKeywords.Alias} {Identifier} = {Value}";
}
