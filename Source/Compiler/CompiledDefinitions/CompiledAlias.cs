using LanguageCore.Parser;

namespace LanguageCore.Compiler;

public class CompiledAlias :
    IReferenceable<TypeInstance>,
    IIdentifiable<string>,
    IInFile,
    ILocated,
    ICompiledDefinition<AliasDefinition>
{
    public AliasDefinition Definition { get; }
    public CompiledTypeExpression Value { get; }
    public List<Reference<TypeInstance>> References { get; }

    public string Identifier => Definition.Identifier.Content;
    public Uri File => Definition.File;
    public Location Location => new(Definition.Position, Definition.File);

    public CompiledAlias(CompiledTypeExpression value, AliasDefinition definition)
    {
        Definition = definition;
        Value = value;
        References = new List<Reference<TypeInstance>>();
    }
}
