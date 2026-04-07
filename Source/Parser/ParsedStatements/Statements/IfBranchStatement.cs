using LanguageCore.Tokenizing;

namespace LanguageCore.Parser.Statements;

public class IfBranchStatement : BranchStatementBase
{
    public Expression Condition { get; }
    public ElseBranchStatement? Else { get; }

    public IfBranchStatement(
        Token keyword,
        Expression condition,
        Statement body,
        ElseBranchStatement? @else,
        Uri file)
        : base(keyword, IfPart.If, body, file)
    {
        Condition = condition;
        Else = @else;
    }
}
