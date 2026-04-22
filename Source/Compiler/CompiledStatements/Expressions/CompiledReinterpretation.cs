namespace LanguageCore.Compiler;

public class CompiledReinterpretation : CompiledExpression
{
    public required CompiledExpression Value { get; init; }
    public required CompiledTypeExpression TypeExpression { get; init; }

    public override string ToString() => $"{Value} as {Type}";
}
