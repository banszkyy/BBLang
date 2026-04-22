namespace LanguageCore.Compiler;

public class CompiledUnaryOperatorCall : CompiledExpression
{
    #region Operators

    public const string LogicalNOT = "!";
    public const string BinaryNOT = "~";
    public const string UnaryMinus = "-";
    public const string UnaryPlus = "+";

    #endregion

    public required string Operator { get; init; }
    public required CompiledExpression Expression { get; init; }

    public override string ToString() => $"{Operator}{Expression}";
}
