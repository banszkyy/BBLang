using LanguageCore.Compiler;
using LanguageCore.Parser.Statements;
using LanguageCore.Tokenizing;

namespace LanguageCore.Parser;

public class ParameterDefinition :
    IPositioned,
    IHaveType,
    IIdentifiable<Token>,
    IInFile,
    ILocated
{
    public Token Identifier { get; }
    public TypeInstance Type { get; }
    public ImmutableArray<Token> Modifiers { get; }
    public Expression? DefaultValue { get; }

    public bool IsThis => Modifiers.Contains(ModifierKeywords.This);
    public Position Position =>
        new Position(Identifier, Type)
        .Union(Modifiers);
    public Uri File { get; }

    public Location Location => new(Position, File);

    public ParameterDefinition(ParameterDefinition other)
    {
        Modifiers = other.Modifiers;
        Type = other.Type;
        Identifier = other.Identifier;
        DefaultValue = other.DefaultValue;
        File = other.File;
    }

    public ParameterDefinition(ImmutableArray<Token> modifiers, TypeInstance type, Token identifier, Expression? defaultValue, Uri file)
    {
        Modifiers = modifiers;
        Type = type;
        Identifier = identifier;
        DefaultValue = defaultValue;
        File = file;
    }

    public override string ToString() => $"{string.Join(' ', Modifiers)} {Type} {Identifier}".TrimStart();
}
