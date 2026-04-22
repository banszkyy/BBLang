namespace LanguageCore.Compiler;

public class CompiledConstructorCall : CompiledExpression
{
    public required TemplateInstance<CompiledConstructorDefinition> Function { get; init; }
    public required CompiledExpression Object { get; init; }
    public required ImmutableArray<CompiledArgument> Arguments { get; init; }

    public override string ToString() => $"new {GeneralType.TryInsertTypeParameters(Function.Template.Type, Function.TypeArguments)}({string.Join(", ", Arguments.Select(v => v.ToString()))})";
}
