namespace LanguageCore.Compiler;

public class CompiledExpressionVariableAccess : CompiledAccessExpression
{
    public required ExpressionVariable Variable { get; init; }

    public override string ToString() => Variable.Name;
}
