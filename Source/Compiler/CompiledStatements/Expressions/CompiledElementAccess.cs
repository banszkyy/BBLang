namespace LanguageCore.Compiler;

public class CompiledElementAccess : CompiledAccessExpression
{
    public required CompiledExpression Base { get; init; }
    public required CompiledExpression Index { get; init; }

    public override string ToString() => $"{Base}[{Index}]";
}
