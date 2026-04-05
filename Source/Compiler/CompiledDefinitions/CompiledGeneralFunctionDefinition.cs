using LanguageCore.Parser;
using LanguageCore.Parser.Statements;

namespace LanguageCore.Compiler;

public class CompiledGeneralFunctionDefinition :
    ICompiledDefinition<GeneralFunctionDefinition>,
    ICompiledDefinition<FunctionThingDefinition>,
    IReferenceable<Expression?>,
    IInContext<CompiledStruct>,
    ICompiledFunctionDefinition,
    IIdentifiable<string>
{
    public bool IsMsilCompatible { get; set; } = true;

    public GeneralFunctionDefinition Definition { get; }
    public GeneralType Type { get; }
    public ImmutableArray<CompiledParameter> Parameters { get; }
    public CompiledStruct Context { get; }
    public List<Reference<Expression?>> References { get; }

    FunctionThingDefinition ICompiledDefinition<FunctionThingDefinition>.Definition => Definition;
    public bool ReturnSomething => !Type.SameAs(BasicType.Void);
    public Uri File => Definition.File;
    public Location Location => Definition.Location;
    public string Identifier => Definition.Identifier.Content;
    CanUseOn IHaveAttributes.AttributeUsageKind => (Definition as IHaveAttributes).AttributeUsageKind;
    public ImmutableArray<AttributeUsage> Attributes => Definition.Attributes;

    public CompiledGeneralFunctionDefinition(GeneralType type, ImmutableArray<CompiledParameter> parameters, CompiledStruct context, GeneralFunctionDefinition functionDefinition)
    {
        Definition = functionDefinition;
        Type = type;
        Parameters = parameters;
        Context = context;
        References = new List<Reference<Expression?>>();
    }

    public CompiledGeneralFunctionDefinition(GeneralType type, ImmutableArray<CompiledParameter> parameters, CompiledGeneralFunctionDefinition other)
    {
        Definition = other.Definition;
        Type = type;
        Parameters = parameters;
        Context = other.Context;
        References = new List<Reference<Expression?>>(other.References);
    }

    public override string ToString()
    {
        StringBuilder result = new();

        if (Definition.IsExported)
        { result.Append("export "); }

        result.Append(Identifier);

        result.Append('(');
        if (Parameters.Length > 0)
        {
            for (int i = 0; i < Parameters.Length; i++)
            {
                if (i > 0) result.Append(", ");
                if (Parameters[i].Definition.Modifiers.Length > 0)
                {
                    result.AppendJoin(' ', Parameters[i].Definition.Modifiers);
                    result.Append(' ');
                }
                result.Append(Parameters[i].Type.ToString());
            }
        }
        result.Append(')');
        result.Append(' ');

        result.Append(Definition.Block?.ToString() ?? ";");

        return result.ToString();
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
}
