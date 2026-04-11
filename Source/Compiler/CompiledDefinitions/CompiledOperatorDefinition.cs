using LanguageCore.Parser;
using LanguageCore.Parser.Statements;

namespace LanguageCore.Compiler;

public class CompiledOperatorDefinition :
    ICompiledDefinition<FunctionDefinition>,
    ICompiledDefinition<FunctionThingDefinition>,
    IReferenceable<Expression>,
    IInContext<CompiledStruct?>,
    ICompiledFunctionDefinition,
    IExternalFunctionDefinition,
    IIdentifiable<string>
{
    public bool IsMsilCompatible { get; set; } = true;

    public FunctionDefinition Definition { get; }
    public GeneralType Type { get; }
    public ImmutableArray<CompiledParameter> Parameters { get; }
    public CompiledStruct? Context { get; }
    public List<Reference<Expression>> References { get; }

    FunctionThingDefinition ICompiledDefinition<FunctionThingDefinition>.Definition => Definition;
    public bool ReturnSomething => !Type.SameAs(BasicType.Void);
    public Uri File => Definition.File;
    public Location Location => Definition.Location;
    public string? ExternalFunctionName => Definition.ExposedFunctionName;
    public string Identifier => Definition.Identifier.Content;
    CanUseOn IHaveAttributes.AttributeUsageKind => (Definition as IHaveAttributes).AttributeUsageKind;
    public ImmutableArray<AttributeUsage> Attributes => Definition.Attributes;

    public CompiledOperatorDefinition(GeneralType type, ImmutableArray<CompiledParameter> parameters, CompiledStruct? context, FunctionDefinition functionDefinition)
    {
        Definition = functionDefinition;
        Type = type;
        Parameters = parameters;
        Context = context;
        References = new List<Reference<Expression>>();
    }

    public CompiledOperatorDefinition(GeneralType type, ImmutableArray<CompiledParameter> parameters, CompiledOperatorDefinition other)
    {
        Definition = other.Definition;
        Type = type;
        Parameters = parameters;
        Context = other.Context;
        References = new List<Reference<Expression>>(other.References);
    }

    public string ToReadable(IReadOnlyDictionary<string, GeneralType>? typeArguments = null)
    {
        StringBuilder result = new();
        result.Append(GeneralType.TryInsertTypeParameters(Type, typeArguments).ToString());
        result.Append(' ');
        result.Append(Identifier);
        result.Append('(');
        for (int i = 0; i < Parameters.Length; i++)
        {
            if (i > 0) result.Append(", ");
            result.Append(GeneralType.TryInsertTypeParameters(Parameters[i].Type, typeArguments).ToString());
        }
        result.Append(')');
        return result.ToString();
    }

    string IReadable.ToReadable() => ToReadable(null);
    public override string ToString() => ToReadable(null);
}
