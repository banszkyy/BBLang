namespace LanguageCore.Compiler;

public class CompiledMeowExpression : CompiledExpression
{
    public ImmutableArray<CompiledStatement> Statements { get; init; } = ImmutableArray<CompiledStatement>.Empty;

    public override string ToString() => "::meow::";
}
