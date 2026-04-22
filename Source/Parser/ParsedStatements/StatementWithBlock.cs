namespace LanguageCore.Parser.Statements;

public abstract class StatementWithBlock : Statement
{
    public Block Body { get; }

    protected StatementWithBlock(Block block, Uri file) : base(file)
    {
        Body = block;
    }
}
