namespace LanguageCore.Compiler;

public class CompiledWhileLoop : CompiledStatement
{
    public required CompiledExpression Condition { get; init; }
    public required CompiledStatement Body { get; init; }

    public override string ToString() => $"while ({Condition}) {Body}";
}
