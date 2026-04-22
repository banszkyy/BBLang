namespace LanguageCore.Compiler;

public class CompiledIf : CompiledBranch
{
    public required CompiledExpression Condition { get; init; }
    public required CompiledStatement Body { get; init; }
    public required CompiledBranch? Next { get; init; }

    public override string ToString()
    {
        StringBuilder res = new();

        res.Append($"if ({Condition.ToString()})");
        res.Append(' ');
        res.Append(Body.ToString());

        if (Next is not null)
        {
            res.Append("...");
        }

        return res.ToString();
    }
}
