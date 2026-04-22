namespace LanguageCore.Compiler;

public class CompiledDereference : CompiledAccessExpression
{
    public required CompiledExpression Address { get; init; }

    public override string ToString() => $"*{Address}";
}
