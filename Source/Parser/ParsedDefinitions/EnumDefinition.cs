using LanguageCore.Compiler;
using LanguageCore.Parser.Statements;
using LanguageCore.Tokenizing;

namespace LanguageCore.Parser;

public class EnumDefinition :
    IPositioned,
    IIdentifiable<Token>,
    IExportable,
    IHaveAttributes,
    ILocated
{
    public ImmutableArray<AttributeUsage> Attributes { get; }
    public ImmutableArray<Token> Modifiers { get; }
    public Token Keyword { get; }
    public TypeInstance? Type { get; }
    public Token Identifier { get; }
    public ImmutableArray<EnumMemberDefinition> Members { get; }
    public TokenPair Brackets { get; }
    public Uri File { get; }

    public Position Position => new Position(Members).Union(Brackets, Keyword, Identifier);
    public Location Location => new(Position, File);
    public bool IsExported => Modifiers.Contains(ProtectionKeywords.Export);

    CanUseOn IHaveAttributes.AttributeUsageKind => CanUseOn.Enum;

    public EnumDefinition(ImmutableArray<AttributeUsage> attributes, ImmutableArray<Token> modifiers, Token keyword, TypeInstance? type, Token identifier, ImmutableArray<EnumMemberDefinition> members, Uri file, TokenPair brackets)
    {
        Attributes = attributes;
        Modifiers = modifiers;
        Keyword = keyword;
        Type = type;
        Identifier = identifier;
        Members = members;
        File = file;
        Brackets = brackets;
    }
}
