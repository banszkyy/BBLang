using LanguageCore.Parser;
using LanguageCore.Parser.Statements;

namespace LanguageCore.Compiler;

public class CompiledField :
    IHaveCompiledType,
    IInContext<CompiledStruct>,
    IIdentifiable<string>,
    IInFile,
    ILocated,
    IReferenceable<Expression>
{
    public FieldDefinition Definition { get; }
    public CompiledStruct Context { get; set; }
    public GeneralType Type { get; }

    public HashSet<CompiledFieldAccess> Getters { get; } = new();
    public HashSet<CompiledFieldAccess> Setters { get; } = new();

    public string Identifier => Definition.Identifier.Content;
    public Uri File => Context.File;
    public Location Location => Definition.Location;

    public List<Reference<Expression>> References { get; } = new();

    public CompiledField(GeneralType type, CompiledStruct context, FieldDefinition definition)
    {
        Definition = definition;
        Type = type;
        Context = context;
    }

    public override string ToString() => $"{Type} {Identifier}";
}
