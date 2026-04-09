using LanguageCore.Compiler;
using LanguageCore.Parser.Statements;
using LanguageCore.Tokenizing;

namespace LanguageCore.Parser;

public class EnumMemberDefinition :
    IPositioned,
    IIdentifiable<Token>,
    ILocated
{
    public Token Identifier { get; }
    public Expression? Value { get; }
    public Uri File { get; }

    public Position Position => new(Identifier, Value);
    public Location Location => new(Position, File);

    public EnumMemberDefinition(Token identifier, Expression? value, Uri file)
    {
        Identifier = identifier;
        Value = value;
        File = file;
    }
}
