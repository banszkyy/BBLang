using LanguageCore.Parser;
using LanguageCore.Parser.Statements;

namespace LanguageCore.Compiler;

public class CompiledConstructorDefinition :
    ICompiledDefinition<ConstructorDefinition>,
    ICompiledDefinition<FunctionThingDefinition>,
    IReferenceable<ConstructorCallExpression>,
    IInContext<CompiledStruct>,
    ICompiledFunctionDefinition,
    IIdentifiable<GeneralType>,
    IExternalFunctionDefinition
{
    public bool IsMsilCompatible { get; set; } = true;

    public ConstructorDefinition Definition { get; }
    public GeneralType Type { get; }
    public ImmutableArray<CompiledParameter> Parameters { get; }
    public CompiledStruct Context { get; set; }
    public List<Reference<ConstructorCallExpression>> References { get; }

    FunctionThingDefinition ICompiledDefinition<FunctionThingDefinition>.Definition => Definition;
    public bool ReturnSomething => true;
    public GeneralType Identifier => Type;
    string? IExternalFunctionDefinition.ExternalFunctionName => null;
    public Uri File => Definition.File;
    public Location Location => Definition.Location;
    CanUseOn IHaveAttributes.AttributeUsageKind => (Definition as IHaveAttributes).AttributeUsageKind;
    public ImmutableArray<AttributeUsage> Attributes => Definition.Attributes;

    public CompiledConstructorDefinition(GeneralType type, ImmutableArray<CompiledParameter> parameters, CompiledStruct context, ConstructorDefinition functionDefinition)
    {
        Definition = functionDefinition;
        Type = type;
        Parameters = parameters;
        Context = context;
        References = new List<Reference<ConstructorCallExpression>>();
    }

    public CompiledConstructorDefinition(GeneralType type, ImmutableArray<CompiledParameter> parameters, CompiledConstructorDefinition other)
    {
        Definition = other.Definition;
        Type = type;
        Parameters = parameters;
        Context = other.Context;
        References = new List<Reference<ConstructorCallExpression>>(other.References);
    }

    public override string ToString()
    {
        StringBuilder result = new();

        if (Definition.IsExported)
        { result.Append("export "); }
        result.Append(Type);
        result.AppendJoin(", ", Parameters.Select(v => $"{v.Type} {v.Identifier}"));
        result.Append(Definition.Block?.ToString() ?? ";");

        return result.ToString();
    }

    public string ToReadable(IReadOnlyDictionary<string, GeneralType>? typeArguments = null)
    {
        StringBuilder result = new();
        result.Append(GeneralType.TryInsertTypeParameters(Type, typeArguments).ToString());
        result.Append('(');
        for (int i = 0; i < Parameters.Length; i++)
        {
            if (i > 0) result.Append(", ");
            result.Append(GeneralType.TryInsertTypeParameters(Parameters[i].Type, typeArguments).ToString());
        }
        result.Append(')');
        return result.ToString();
    }

    public static string ToReadable(GeneralType identifier, IEnumerable<GeneralType> parameters)
    {
        StringBuilder result = new();
        result.Append(identifier.ToString());
        result.Append('(');
        result.AppendJoin(", ", parameters);
        result.Append(')');
        return result.ToString();
    }

    string IReadable.ToReadable() => ToReadable(null);
}
