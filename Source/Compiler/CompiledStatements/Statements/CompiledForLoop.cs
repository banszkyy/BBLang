namespace LanguageCore.Compiler;

public class CompiledForLoop : CompiledStatement
{
    public required CompiledStatement? Initialization { get; init; }
    public required CompiledExpression? Condition { get; init; }
    public required CompiledStatement? Step { get; init; }
    public required CompiledStatement Body { get; init; }

    public override string ToString() => $"for ({Initialization}; {Condition}; {Step}) {Body}";
}
