namespace LanguageCore.Compiler;

public class CompiledRuntimeCall : CompiledExpression
{
    public required CompiledExpression Function { get; init; }
    public required ImmutableArray<CompiledArgument> Arguments { get; init; }

    public override string ToString() => $"{Function}({string.Join(", ", Arguments.Select(v => v.ToString()))})";
}
