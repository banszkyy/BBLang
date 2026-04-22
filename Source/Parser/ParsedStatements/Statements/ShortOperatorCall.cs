using LanguageCore.Compiler;
using LanguageCore.Tokenizing;

namespace LanguageCore.Parser.Statements;

public class ShortOperatorCall : AssignmentStatement, IReferenceableTo<CompiledOperatorDefinition>
{
    /// <summary>
    /// Set by the compiler
    /// </summary>
    public CompiledOperatorDefinition? Reference { get; set; }

    public Token Operator { get; }
    public Expression Target { get; }

    public ImmutableArray<Expression> Arguments => ImmutableArray.Create(Target);
    public override Position Position => new(Operator, Target);

    public ShortOperatorCall(
        Token op,
        Expression expression,
        Uri file) : base(file)
    {
        Operator = op;
        Target = expression;
    }

    public override string ToString()
    {
        StringBuilder result = new();

        if (Target is not null)
        {
            if (Target.ToString().Length <= Stringify.CozyLength)
            { result.Append(Target); }
            else
            { result.Append("..."); }

            result.Append(' ');
            result.Append(Operator);
        }
        else
        { result.Append(Operator); }

        result.Append(Semicolon);
        return result.ToString();
    }

    public override SimpleAssignmentStatement ToAssignment()
    {
        LiteralExpression one = IntLiteralExpression.CreateAnonymous(1, Operator.Position, File);
        BinaryOperatorCallExpression operatorCall = Operator.Content switch
        {
            "++" => new BinaryOperatorCallExpression(Token.CreateAnonymous("+", TokenType.Operator, Operator.Position), ArgumentExpression.Wrap(Target), ArgumentExpression.Wrap(one), File),
            "--" => new BinaryOperatorCallExpression(Token.CreateAnonymous("-", TokenType.Operator, Operator.Position), ArgumentExpression.Wrap(Target), ArgumentExpression.Wrap(one), File),
            _ => throw new NotImplementedException(),
        };
        Token assignmentToken = Token.CreateAnonymous("=", TokenType.Operator, Operator.Position);
        return new SimpleAssignmentStatement(assignmentToken, Target, operatorCall, File);
    }
}
