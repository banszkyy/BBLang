using LanguageCore.Parser.Statements;

namespace LanguageCore.Compiler;

public class CompiledLabelDeclaration : CompiledStatement,
    IReferenceable<IdentifierExpression>
{
    public static readonly FunctionType Type = new(BuiltinType.Void, ImmutableArray<GeneralType>.Empty, false);
    public required string Identifier { get; init; }
    public HashSet<CompiledLabelReference> Getters { get; } = new();
    public List<Reference<IdentifierExpression>> References { get; } = new();

    public override string ToString() => $"{Identifier}:";
}
