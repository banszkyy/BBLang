namespace LanguageCore.Compiler;

public class CompiledSizeof : CompiledExpression
{
    public required CompiledTypeExpression Of { get; init; }

    public override string ToString() => $"sizeof({Of})";
}
