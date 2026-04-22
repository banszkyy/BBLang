using LanguageCore.Compiler;
using LanguageCore.Tokenizing;

namespace LanguageCore.Parser.Statements;

public class FunctionCallExpression : Expression, IReferenceableTo<CompiledFunctionDefinition>
{
    /// <summary>
    /// Set by the compiler
    /// </summary>
    public CompiledFunctionDefinition? Reference { get; set; }

    public Token Identifier { get; }
    public ArgumentListExpression Arguments { get; }
    public ArgumentExpression? Object { get; }

    public ImmutableArray<ArgumentExpression> MethodArguments
    {
        get
        {
            if (Object is null) return Arguments.Arguments;
            return Arguments.Arguments.Insert(0, Object);
        }
    }
    public override Position Position => new(Identifier, Arguments, Object);

    public FunctionCallExpression(
        ArgumentExpression? @object,
        Token identifier,
        ArgumentListExpression arguments,
        Uri file) : base(file)
    {
        Object = @object;
        Identifier = identifier;
        Arguments = arguments;
    }

    public override string ToString()
    {
        StringBuilder result = new();

        result.Append(SurroundingBrackets?.Start);

        if (Object is not null)
        {
            result.Append(Object);
            result.Append('.');
        }
        result.Append(Identifier);
        result.Append(Arguments.ToString());

        result.Append(SurroundingBrackets?.End);
        result.Append(Semicolon);

        return result.ToString();
    }
}
