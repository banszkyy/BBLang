namespace LanguageCore.Compiler;

public class CompiledFieldAccess : CompiledAccessExpression
{
    public required CompiledExpression Object { get; init; }
    public required CompiledField Field { get; init; }

    public override string ToString() => $"{Object}.{Field.Identifier}";
}
