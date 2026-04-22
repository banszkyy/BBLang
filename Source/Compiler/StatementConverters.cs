using LanguageCore.Compiler;
using LanguageCore.Parser.Statements;

namespace LanguageCore.Parser;

public static class StatementConverters
{
    public static bool ToFunctionCall(this AnyCallExpression anyCall, [NotNullWhen(true)] out FunctionCallExpression? functionCall)
    {
        functionCall = null;

        if (anyCall.Expression is null)
        { return false; }

        if (anyCall.Expression is IdentifierExpression functionIdentifier)
        {
            functionCall = new FunctionCallExpression(null, functionIdentifier.Identifier, anyCall.Arguments, anyCall.File)
            {
                Semicolon = anyCall.Semicolon,
                SaveValue = anyCall.SaveValue,
                SurroundingBrackets = anyCall.SurroundingBrackets,
                CompiledType = anyCall.CompiledType,
                PredictedValue = anyCall.PredictedValue,
                Reference = anyCall.Reference,
            };
            return true;
        }

        if (anyCall.Expression is FieldExpression field)
        {
            functionCall = new FunctionCallExpression(ArgumentExpression.Wrap(field.Object), field.Identifier, anyCall.Arguments, anyCall.File)
            {
                Semicolon = anyCall.Semicolon,
                SaveValue = anyCall.SaveValue,
                SurroundingBrackets = anyCall.SurroundingBrackets,
                CompiledType = anyCall.CompiledType,
                PredictedValue = anyCall.PredictedValue,
                Reference = anyCall.Reference,
            };
            return true;
        }

        return false;
    }

    public static NewInstanceExpression ToInstantiation(this ConstructorCallExpression constructorCall) => new(constructorCall.Keyword, constructorCall.Type, constructorCall.File)
    {
        CompiledType = constructorCall.CompiledType,
        SaveValue = true,
        Semicolon = constructorCall.Semicolon,
    };

    public static CompiledVariableDefinition ToVariable(this ParameterDefinition parameterDefinition, GeneralType type, CompiledArgument? initialValue = null)
        => new()
        {
            TypeExpression = CompiledTypeExpression.CreateAnonymous(type, parameterDefinition.Type.Location),
            Identifier = parameterDefinition.Identifier.Content,
            Type = type,
            Cleanup = new CompiledCleanup()
            {
                Location = parameterDefinition.Location,
                TrashType = type,
            },
            InitialValue = initialValue,
            Location = parameterDefinition.Location,
            IsGlobal = false,
            Definition = new VariableDefinition(ImmutableArray<AttributeUsage>.Empty, parameterDefinition.Modifiers, parameterDefinition.Type, parameterDefinition.Identifier, parameterDefinition.DefaultValue, parameterDefinition.File),
        };
}
