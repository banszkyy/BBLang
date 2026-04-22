namespace LanguageCore.Compiler;

public class CompiledDummyExpression : CompiledExpression
{
    public required CompiledStatement Statement { get; init; }

    public override string ToString() => Statement.ToString();
}
