using LanguageCore.Compiler;
using LanguageCore.Tokenizing;

namespace LanguageCore.Parser.Statements;

public class CompoundAssignmentStatement : AssignmentStatement, IReferenceableTo<CompiledOperatorDefinition>
{
    /// <summary>
    /// Set by the compiler
    /// </summary>
    public CompiledOperatorDefinition? Reference { get; set; }

    /// <summary>
    /// This should always starts with <c>"="</c>
    /// </summary>
    public Token Operator { get; }
    public Expression Target { get; }
    public Expression Value { get; }

    public override Position Position => new(Operator, Target, Value);

    public CompoundAssignmentStatement(
        Token @operator,
        Expression left,
        Expression right,
        Uri file) : base(file)
    {
        Operator = @operator;
        Target = left;
        Value = right;
    }

    public override string ToString()
    {
        StringBuilder result = new();

        if (result.Length + Target.ToString().Length > Stringify.CozyLength)
        {
            result.Append($"... {Operator} ...");
        }
        else
        {
            result.Append(Target);
            result.Append(' ');
            result.Append(Operator);
            result.Append(' ');
            if (result.Length + Value.ToString().Length > Stringify.CozyLength)
            { result.Append("..."); }
            else
            { result.Append(Value); }
        }

        result.Append(Semicolon);
        return result.ToString();
    }

    public override SimpleAssignmentStatement ToAssignment()
    {
        BinaryOperatorCallExpression statementToAssign = new(
            Token.CreateAnonymous(Operator.Content.Replace("=", string.Empty, StringComparison.Ordinal), TokenType.Operator, Operator.Position),
            ArgumentExpression.Wrap(Target),
            ArgumentExpression.Wrap(Value),
            File
        );
        Token assignmentOperator = Token.CreateAnonymous("=", TokenType.Operator, Operator.Position);
        return new SimpleAssignmentStatement(assignmentOperator, Target, statementToAssign, File);
    }
}
