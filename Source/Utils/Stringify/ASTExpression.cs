using LanguageCore.Parser.Statements;

namespace LanguageCore;

public static partial class Stringifier
{
    public static void Stringify(ListExpression expression, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append('[');
        builder.AppendJoin(expression.Values, Stringify);
        builder.Append(']');
    }
    public static void Stringify(BinaryOperatorCallExpression expression, BuilderBase builder, StringifyContext context = default)
    {
        if (context.IsNaked) builder.Append('(');
        Stringify(expression.Left, builder, new() { IsNaked = true });
        builder.Space();
        builder.Append(expression.Operator.Content);
        builder.Space();
        Stringify(expression.Right, builder, new() { IsNaked = true });
        if (context.IsNaked) builder.Append(')');
    }
    public static void Stringify(UnaryOperatorCallExpression expression, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append(expression.Operator.Content);
        Stringify(expression.Expression, builder, new() { IsNaked = true });
    }
    public static void Stringify(LiteralExpression expression, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append(expression.ValueToken.ToOriginalString());
    }
    public static void Stringify(IdentifierExpression expression, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append(expression.Identifier.Content);
    }
    public static void Stringify(GetReferenceExpression expression, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append('&');
        Stringify(expression.Expression, builder, new() { IsNaked = true });
    }
    public static void Stringify(DereferenceExpression expression, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append('*');
        Stringify(expression.Expression, builder, new() { IsNaked = true });
    }
    public static void Stringify(NewInstanceExpression expression, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append(StatementKeywords.New);
        builder.Append(' ');
        Stringify(expression.Type, builder);
    }
    public static void Stringify(ConstructorCallExpression expression, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append(StatementKeywords.New);
        builder.Append(' ');
        Stringify(expression.Type, builder);
        builder.Append('(');
        builder.AppendJoin(expression.Arguments.Arguments, Stringify);
        builder.Append(')');
    }
    public static void Stringify(IndexCallExpression expression, BuilderBase builder, StringifyContext context = default)
    {
        Stringify(expression.Object, builder, new() { IsNaked = true });
        builder.Append('[');
        Stringify(expression.Index, builder);
        builder.Append(']');
    }
    public static void Stringify(FieldExpression expression, BuilderBase builder, StringifyContext context = default)
    {
        Stringify(expression.Object, builder, new() { IsNaked = true });
        builder.Append('.');
        builder.Append(expression.Identifier.Content);
    }
    public static void Stringify(ReinterpretExpression expression, BuilderBase builder, StringifyContext context = default)
    {
        Stringify(expression.PrevStatement, builder, new() { IsNaked = true });
        builder.Append(' ');
        builder.Append(StatementKeywords.As);
        builder.Append(' ');
        Stringify(expression.Type, builder);
    }
    public static void Stringify(ManagedTypeCastExpression expression, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append('(');
        Stringify(expression.Type, builder);
        builder.Append(')');
        Stringify(expression.Expression, builder, new() { IsNaked = true });
    }
    public static void Stringify(ArgumentExpression expression, BuilderBase builder, StringifyContext context = default)
    {
        if (expression.Modifier is not null)
        {
            builder.Append(expression.Modifier.Content);
            builder.Append(' ');
        }
        Stringify(expression.Value, builder);
    }
    public static void Stringify(AnyCallExpression expression, BuilderBase builder, StringifyContext context = default)
    {
        Stringify(expression.Expression, builder, new() { IsNaked = true });
        builder.Append('(');
        builder.AppendJoin(expression.Arguments.Arguments, Stringify);
        builder.Append(')');
    }
    public static void Stringify(LambdaExpression expression, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append('(');
        builder.AppendJoin(expression.Parameters.Parameters, Stringify);
        builder.Append(')');
    }
    public static void Stringify(Expression statement, BuilderBase builder, StringifyContext context = default)
    {
        switch (statement)
        {
            case ListExpression v: Stringify(v, builder, context); break;
            case BinaryOperatorCallExpression v: Stringify(v, builder, context); break;
            case UnaryOperatorCallExpression v: Stringify(v, builder, context); break;
            case LiteralExpression v: Stringify(v, builder, context); break;
            case IdentifierExpression v: Stringify(v, builder, context); break;
            case GetReferenceExpression v: Stringify(v, builder, context); break;
            case DereferenceExpression v: Stringify(v, builder, context); break;
            case NewInstanceExpression v: Stringify(v, builder, context); break;
            case ConstructorCallExpression v: Stringify(v, builder, context); break;
            case IndexCallExpression v: Stringify(v, builder, context); break;
            case FieldExpression v: Stringify(v, builder, context); break;
            case ReinterpretExpression v: Stringify(v, builder, context); break;
            case ManagedTypeCastExpression v: Stringify(v, builder, context); break;
            case ArgumentExpression v: Stringify(v, builder, context); break;
            case AnyCallExpression v: Stringify(v, builder, context); break;
            case LambdaExpression v: Stringify(v, builder, context); break;
            default: throw new NotImplementedException(statement.GetType().Name);
        }
    }
}
