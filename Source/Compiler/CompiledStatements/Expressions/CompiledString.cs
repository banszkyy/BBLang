namespace LanguageCore.Compiler;

public class CompiledString : CompiledExpression
{
    public required string Value { get; init; }
    public required bool IsUTF8 { get; init; }
    public required CompiledExpression Allocator { get; init; }

    public int GetRealLength()
    {
        if (IsUTF8)
        {
            return Encoding.UTF8.GetByteCount(Value) + 1;
        }
        else
        {
            return Value.Length + 1;
        }
    }

    public override string ToString() => $"\"{Value}\"";
}
