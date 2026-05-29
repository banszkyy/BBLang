using LanguageCore.Parser;
using LanguageCore.Parser.Statements;

namespace LanguageCore.Compiler;

public class CompiledParameter :
    IHaveCompiledType,
    IIdentifiable<string>,
    IInFile,
    ILocated,
    ICompiledDefinition<ParameterDefinition>,
    IReferenceable<IdentifierExpression>
{
    public ParameterDefinition Definition { get; }
    public GeneralType Type { get; }
    public HashSet<CompiledParameterAccess> Getters { get; } = new();
    public HashSet<CompiledParameterAccess> Setters { get; } = new();

    public string Identifier => Definition.Identifier.Content;
    public Uri File => Definition.File;
    public Location Location => new(Definition.Position, Definition.File);

    public List<Reference<IdentifierExpression>> References { get; }

    public CompiledParameter(GeneralType type, ParameterDefinition definition)
    {
        Definition = definition;
        Type = type;
        References = new();
    }

    public override string ToString() => $"{Type} {Identifier}";
}
