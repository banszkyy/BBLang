using LanguageCore.Parser.Statements;
using LanguageCore.Runtime;

namespace LanguageCore.Compiler;

public readonly struct CompilerResult
{
    public readonly ImmutableArray<CompiledFunctionDefinition> FunctionDefinitions;
    public readonly ImmutableArray<CompiledGeneralFunctionDefinition> GeneralFunctionDefinitions;
    public readonly ImmutableArray<CompiledOperatorDefinition> OperatorDefinitions;
    public readonly ImmutableArray<CompiledConstructorDefinition> ConstructorDefinitions;

    public readonly ImmutableArray<CompiledAlias> Aliases;
    public readonly ImmutableArray<CompiledEnum> Enums;
    public readonly ImmutableArray<CompiledStruct> Structs;

    public readonly ImmutableArray<ParsedFile> RawTokens;
    public readonly ImmutableArray<(ImmutableArray<Statement> Statements, Uri File)> RawStatements;

    public readonly ImmutableArray<IExternalFunction> ExternalFunctions;
    public readonly Uri File;
    public readonly bool IsExpression;

    public readonly ImmutableArray<CompiledStatement> Statements;
    public readonly ImmutableArray<CompiledVariableConstant> CompiledConstants;
    public readonly ImmutableArray<CompiledFunction> Functions;

    public readonly IEnumerable<Statement> StatementsIn(Uri file)
    {
        foreach ((ImmutableArray<Statement> topLevelStatements, Uri _file) in RawStatements)
        {
            if (file != _file) continue;
            foreach (Statement statement in topLevelStatements)
            { yield return statement; }
        }

        foreach (CompiledFunctionDefinition function in FunctionDefinitions)
        {
            if (file != function.File) continue;
            if (function.Definition.Block is not null) yield return function.Definition.Block;
        }

        foreach (CompiledGeneralFunctionDefinition function in GeneralFunctionDefinitions)
        {
            if (file != function.File) continue;
            if (function.Definition.Block is not null) yield return function.Definition.Block;
        }

        foreach (CompiledOperatorDefinition @operator in OperatorDefinitions)
        {
            if (file != @operator.File) continue;
            if (@operator.Definition.Block is not null) yield return @operator.Definition.Block;
        }
    }

    public static CompilerResult MakeEmpty(Uri file) => new(
        ImmutableArray<ParsedFile>.Empty,
        ImmutableArray<CompiledFunctionDefinition>.Empty,
        ImmutableArray<CompiledGeneralFunctionDefinition>.Empty,
        ImmutableArray<CompiledOperatorDefinition>.Empty,
        ImmutableArray<CompiledConstructorDefinition>.Empty,
        ImmutableArray<CompiledAlias>.Empty,
        ImmutableArray<CompiledEnum>.Empty,
        ImmutableArray<IExternalFunction>.Empty,
        ImmutableArray<CompiledStruct>.Empty,
        ImmutableArray<(ImmutableArray<Statement>, Uri)>.Empty,
        file,
        false,
        ImmutableArray<CompiledStatement>.Empty,
        ImmutableArray<CompiledVariableConstant>.Empty,
        ImmutableArray<CompiledFunction>.Empty);

    public CompilerResult(
        ImmutableArray<ParsedFile> tokens,
        ImmutableArray<CompiledFunctionDefinition> functions,
        ImmutableArray<CompiledGeneralFunctionDefinition> generalFunctions,
        ImmutableArray<CompiledOperatorDefinition> operators,
        ImmutableArray<CompiledConstructorDefinition> constructors,
        ImmutableArray<CompiledAlias> aliases,
        ImmutableArray<CompiledEnum> enums,
        ImmutableArray<IExternalFunction> externalFunctions,
        ImmutableArray<CompiledStruct> structs,
        ImmutableArray<(ImmutableArray<Statement> Statements, Uri File)> topLevelStatements,
        Uri file,
        bool isExpression,
        ImmutableArray<CompiledStatement> compiledStatements,
        ImmutableArray<CompiledVariableConstant> compiledConstants,
        ImmutableArray<CompiledFunction> functions2)
    {
        RawTokens = tokens;
        FunctionDefinitions = functions;
        GeneralFunctionDefinitions = generalFunctions;
        OperatorDefinitions = operators;
        ConstructorDefinitions = constructors;
        Aliases = aliases;
        Enums = enums;
        ExternalFunctions = externalFunctions;
        Structs = structs;
        RawStatements = topLevelStatements;
        File = file;
        IsExpression = isExpression;
        Statements = compiledStatements;
        CompiledConstants = compiledConstants;
        Functions = functions2;
    }
}
