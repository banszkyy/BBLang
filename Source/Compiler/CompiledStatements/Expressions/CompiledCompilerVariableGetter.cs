namespace LanguageCore.Compiler;

public class CompiledCompilerVariableAccess : CompiledExpression
{
    public required string Identifier { get; init; }
    public CompiledVariableConstant? Definition { get; init; }

    public override string ToString() => $"@{Identifier}";
}
