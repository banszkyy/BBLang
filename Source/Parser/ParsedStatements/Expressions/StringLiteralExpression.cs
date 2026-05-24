using LanguageCore.Tokenizing;

namespace LanguageCore.Parser.Statements;

public class StringLiteralExpression : LiteralExpression
{
    public string Value { get; }

    public override LiteralType Type => LiteralType.String;

    public StringLiteralExpression(string value, Token valueToken, Uri file) : base(valueToken, file)
    {
        Value = value;
    }

    public static StringLiteralExpression CreateAnonymous(string value, Position position, Uri file) => new(
        value,
        Token.CreateAnonymous(value, TokenType.LiteralString, position),
        file
    );

    public override string ToString()
    {
        StringBuilder result = new();
        result.Append(SurroundingBrackets?.Start);

        result.Append($"\"{Value.Escape()}\"");

        result.Append(SurroundingBrackets?.End);
        result.Append(Semicolon);
        return result.ToString();
    }
}
