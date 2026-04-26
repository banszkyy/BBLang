namespace LanguageCore;

public class AsciiBuilder
{
    readonly List<byte> _builder = new();

    public int Length => _builder.Count;

    public void Append(byte @char) => _builder.Add(@char);
    public void Clear() => _builder.Clear();

    public override string ToString() => Encoding.UTF8.GetString(_builder.ToArray());
}
