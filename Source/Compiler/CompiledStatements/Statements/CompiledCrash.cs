namespace LanguageCore.Compiler;

public class CompiledCrash : CompiledStatement
{
    public required CompiledExpression Value { get; init; }

    public override string ToString() => $"crash {Value}";
}
