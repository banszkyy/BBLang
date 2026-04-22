namespace LanguageCore.Compiler;

public class CompiledBlock : CompiledStatement
{
    public required ImmutableArray<CompiledStatement> Statements { get; init; }

    public static CompiledBlock CreateIfNot(CompiledStatement statement) =>
        statement is CompiledBlock block
        ? block
        : new CompiledBlock()
        {
            Location = statement.Location,
            Statements = ImmutableArray.Create(statement),
        };

    public override string ToString()
    {
        StringBuilder result = new();

        result.Append('{');

        switch (Statements.Length)
        {
            case 0:
                result.Append(' ');
                break;
            case 1:
                result.Append(' ');
                result.Append(Statements[0]);
                result.Append(' ');
                break;
            default:
                result.Append("...");
                break;
        }

        result.Append('}');

        return result.ToString();
    }
}
