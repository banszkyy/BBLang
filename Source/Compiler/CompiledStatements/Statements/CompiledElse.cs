namespace LanguageCore.Compiler;

public class CompiledElse : CompiledBranch
{
    public required CompiledStatement Body { get; init; }

    public override string ToString()
    {
        StringBuilder res = new();

        res.Append($"else ");
        res.Append(Body.ToString());

        return res.ToString();
    }
}
