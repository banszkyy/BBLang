namespace LanguageCore.Compiler;

public class CompiledReturn : CompiledStatement
{
    public required CompiledExpression? Value { get; init; }

    public override string ToString()
        => Value is null
        ? $"return"
        : $"return {Value}";
}
