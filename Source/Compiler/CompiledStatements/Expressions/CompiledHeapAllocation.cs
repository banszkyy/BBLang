namespace LanguageCore.Compiler;

public class CompiledHeapAllocation : CompiledExpression
{
    public required TemplateInstance<CompiledFunctionDefinition> Allocator { get; init; }
    public required CompiledTypeExpression TypeExpression { get; init; }

    public override string ToString() => $"new {Type}";
}
