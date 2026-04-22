namespace LanguageCore.Compiler;

public class CompiledParameterAccess : CompiledAccessExpression
{
    public required CompiledParameter Parameter { get; init; }

    public override string ToString() => $"{Parameter.Identifier}";
}
