using LanguageCore.Parser;

namespace LanguageCore.Compiler;

public class CompiledLambda : CompiledExpression,
    ICompiledFunctionDefinition,
    IHaveCompiledType,
    IHaveInstructionOffset,
    ICallableDefinition
{
    public bool IsMsilCompatible { get; set; } = true;
    public FunctionFlags Flags { get; set; }

    public ImmutableArray<CompiledParameter> Parameters { get; }
    public ParameterDefinitionCollection ParameterDefinitions { get; }
    public CompiledBlock Block { get; }
    public ImmutableArray<CapturedLocal> CapturedLocals { get; }
    public CompiledExpression? Allocator { get; init; }
    public Uri File { get; }

    public FunctionThingDefinition Definition => throw new InvalidOperationException(); // FIXME
    public bool ReturnSomething => !Type.SameAs(BasicType.Void);
    CanUseOn IHaveAttributes.AttributeUsageKind => (Definition as IHaveAttributes).AttributeUsageKind;
    public ImmutableArray<AttributeUsage> Attributes => Definition.Attributes;

    public CompiledLambda(GeneralType type, ImmutableArray<CompiledParameter> parameters, CompiledBlock block, ParameterDefinitionCollection parameterDefinitions, ImmutableArray<CapturedLocal> capturedLocals, Uri file)
    {
        Type = type;
        Parameters = parameters;
        Block = block;
        ParameterDefinitions = parameterDefinitions;
        CapturedLocals = capturedLocals;
        File = file;
    }

    public override string ToString()
    {
        return $"({string.Join(", ", Parameters.Select(v => $"{v.Type} {v.Identifier}"))}) => {Block}";
    }

    public string ToReadable() => Type.ToString();
}
