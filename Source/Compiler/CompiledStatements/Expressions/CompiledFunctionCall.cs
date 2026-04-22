namespace LanguageCore.Compiler;

public class CompiledFunctionCall : CompiledExpression
{
    public required TemplateInstance<ICompiledFunctionDefinition> Function { get; init; }
    public required ImmutableArray<CompiledArgument> Arguments { get; init; }

    public override string ToString() => $"{Function.Template switch
    {
        CompiledFunctionDefinition v => v.Identifier,
        CompiledOperatorDefinition v => v.Identifier,
        CompiledGeneralFunctionDefinition v => v.Identifier,
        CompiledConstructorDefinition v => v.Type.ToString(),
        _ => throw new UnreachableException(),
    }}({string.Join(", ", Arguments.Select(v => v.ToString()))})";
}
