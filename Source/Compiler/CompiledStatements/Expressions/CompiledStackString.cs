namespace LanguageCore.Compiler;

public class CompiledStackString : CompiledExpression
{
    public required string Value { get; init; }
    public required bool IsNullTerminated { get; init; }
    public required bool IsUTF8 { get; init; }

    public int Length => IsNullTerminated ? Value.Length + 1 : Value.Length;

    public override string ToString() => $"\"{Value}\"";
}
