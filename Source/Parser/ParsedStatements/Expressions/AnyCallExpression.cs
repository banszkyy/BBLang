using LanguageCore.Compiler;

namespace LanguageCore.Parser.Statements;

public class AnyCallExpression : Expression,
    IReferenceableTo<StatementCompiler.FunctionQueryResult<CompiledFunctionDefinition>>
{
    /// <summary>
    /// Set by the compiler
    /// </summary>
    public StatementCompiler.FunctionQueryResult<CompiledFunctionDefinition>? Reference { get; set; }

    public Expression Expression { get; }
    public ArgumentListExpression Arguments { get; }

    public override Position Position => new(Expression, Arguments);

    public AnyCallExpression(
        Expression expression,
        ArgumentListExpression arguments,
        Uri file) : base(file)
    {
        Expression = expression;
        Arguments = arguments;
    }

    public override string ToString()
    {
        StringBuilder result = new();

        result.Append(SurroundingBrackets?.Start);

        result.Append(Expression);
        result.Append(Arguments.ToString());

        result.Append(SurroundingBrackets?.End);
        result.Append(Semicolon);

        return result.ToString();
    }
}
