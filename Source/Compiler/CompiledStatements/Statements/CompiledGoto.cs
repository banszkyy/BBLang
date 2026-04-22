namespace LanguageCore.Compiler;

public class CompiledGoto : CompiledStatement
{
    public required CompiledExpression Value { get; init; }

    public override string ToString() => $"goto {Value}";
}
