namespace LanguageCore.Compiler;

public class CompiledStackAllocation : CompiledExpression
{
    public required CompiledTypeExpression TypeExpression { get; init; }

    public override string ToString() => $"new {Type}";
}
