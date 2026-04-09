using LanguageCore.Parser.Statements;

namespace LanguageCore.Compiler;

public class CompiledVariableConstant :
    IHaveCompiledType,
    IIdentifiable<string>,
    IInFile,
    ILocated,
    ICompiledDefinition<VariableDefinition>
{
    public VariableDefinition Definition { get; }
    public CompiledValue Value { get; }
    public GeneralType Type { get; }

    public string Identifier => Definition.Identifier.Content;
    public Uri File => Definition.File;
    public Location Location => Definition.Location;

    public CompiledVariableConstant(CompiledValue value, GeneralType type, VariableDefinition definition)
    {
        Definition = definition;
        Value = value;
        Type = type;
    }
}
