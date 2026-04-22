namespace LanguageCore.Compiler;

public class CompiledGetReference : CompiledExpression
{
    public required CompiledExpression Of { get; init; }

    public override string ToString() => $"&{Of}";
}
