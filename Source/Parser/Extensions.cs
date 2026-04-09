using LanguageCore.Compiler;
using LanguageCore.Parser.Statements;

namespace LanguageCore.Parser;

public static class Extensions
{
    public static IEnumerable<Statement> EnumerateStatements(this ParserResult parserResult)
    {
        foreach (Statement v in parserResult.TopLevelStatements.IsDefault ? Enumerable.Empty<Statement>() : parserResult.TopLevelStatements.SelectMany(StatementWalker.Visit))
        { yield return v; }

        foreach (Statement statement in parserResult.Functions.IsDefault ? Enumerable.Empty<Statement>() : parserResult.Functions.SelectMany(v => StatementWalker.Visit(v.Block)))
        { yield return statement; }

        foreach (Statement statement in parserResult.Operators.IsDefault ? Enumerable.Empty<Statement>() : parserResult.Operators.SelectMany(v => StatementWalker.Visit(v.Block)))
        { yield return statement; }

        foreach (Statement statement in parserResult.EnumDefinitions.IsDefault ? Enumerable.Empty<Statement>() : parserResult.EnumDefinitions.SelectMany(v => v.Members).SelectMany(v => StatementWalker.Visit(v.Value)))
        { yield return statement; }

        foreach (StructDefinition structs in parserResult.Structs.IsDefault ? Enumerable.Empty<StructDefinition>() : parserResult.Structs)
        {
            foreach (Statement statement in structs.GeneralFunctions.SelectMany(v => StatementWalker.Visit(v.Block)))
            { yield return statement; }

            foreach (Statement statement in structs.Functions.SelectMany(v => StatementWalker.Visit(v.Block)))
            { yield return statement; }

            foreach (Statement statement in structs.Operators.SelectMany(v => StatementWalker.Visit(v.Block)))
            { yield return statement; }

            foreach (Statement statement in structs.Constructors.SelectMany(v => StatementWalker.Visit(v.Block)))
            { yield return statement; }
        }
    }

    public static IEnumerable<TypeInstance> EnumerateTypeInstances(this ParserResult ast)
    {
        foreach (AliasDefinition item in ast.AliasDefinitions) yield return item.Value;

        foreach (EnumDefinition item in ast.EnumDefinitions.Where(v => v.Type is not null)) yield return item.Type!;

        foreach (StructDefinition item in ast.Structs)
        {
            foreach (FieldDefinition v in item.Fields) yield return v.Type;

            foreach (ConstructorDefinition v in item.Constructors)
            {
                yield return v.Type;
                foreach (ParameterDefinition p in v.Parameters.Parameters) yield return p.Type;
            }

            foreach (FunctionDefinition v in item.Functions)
            {
                yield return v.Type;
                foreach (ParameterDefinition p in v.Parameters.Parameters) yield return p.Type;
            }

            foreach (GeneralFunctionDefinition v in item.GeneralFunctions)
            {
                foreach (ParameterDefinition p in v.Parameters.Parameters) yield return p.Type;
            }

            foreach (FunctionDefinition v in item.Operators)
            {
                yield return v.Type;
                foreach (ParameterDefinition p in v.Parameters.Parameters) yield return p.Type;
            }
        }

        foreach (FunctionDefinition v in ast.Functions)
        {
            yield return v.Type;
            foreach (ParameterDefinition p in v.Parameters.Parameters) yield return p.Type;
        }

        foreach (FunctionDefinition v in ast.Operators)
        {
            yield return v.Type;
            foreach (ParameterDefinition p in v.Parameters.Parameters) yield return p.Type;
        }

        foreach (IHaveType item in ast.EnumerateStatements().OfType<IHaveType>())
        {
            yield return item.Type;
        }
    }

    public static bool EnumerateStatements(this CompilerResult parserResult, Func<CompiledStatement, bool> callback)
    {
        if (!parserResult.Statements.IsDefault)
        {
            if (!StatementWalker.Visit(parserResult.Statements, callback)) return false;
        }

        if (!parserResult.Enums.IsDefault)
        {
            foreach (CompiledExpression? item in parserResult.Enums.SelectMany(v => v.Members).Select(v => v.Value))
            {
                if (!StatementWalker.Visit(item, callback)) return false;
            }
        }

        if (!parserResult.Functions.IsDefault)
        {
            foreach (CompiledFunction function in parserResult.Functions)
            {
                if (!StatementWalker.Visit(function.Body, callback)) return false;
            }
        }

        return true;
    }
}
