using LanguageCore.Parser;

namespace LanguageCore;

public static partial class Stringifier
{
    public static void Stringify(TypeInstanceFunction type, BuilderBase builder, StringifyContext context = default)
    {
        if (type.ClosureModifier is not null) builder.Append('@');
        Stringify(type.FunctionReturnType, builder);
        builder.Append('(');
        builder.AppendJoin(type.FunctionParameterTypes, Stringify);
        builder.Append(')');
    }
    public static void Stringify(TypeInstancePointer type, BuilderBase builder, StringifyContext context = default)
    {
        Stringify(type.To, builder);
        builder.Append('*');
    }
    public static void Stringify(TypeInstanceReference type, BuilderBase builder, StringifyContext context = default)
    {
        Stringify(type.To, builder);
        builder.Append('&');
    }
    public static void Stringify(TypeInstanceSimple type, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append(type.Identifier.Content);
        if (type.TypeArguments.HasValue)
        {
            builder.Append('<');
            builder.AppendJoin(type.TypeArguments.Value, Stringify);
            builder.Append('>');
        }
    }
    public static void Stringify(TypeInstanceStackArray type, BuilderBase builder, StringifyContext context = default)
    {
        Stringify(type.StackArrayOf, builder);
        builder.Append('[');
        if (type.StackArraySize is not null) Stringify(type.StackArraySize, builder);
        builder.Append(']');
    }
    public static void Stringify(TypeInstance type, BuilderBase builder, StringifyContext context = default)
    {
        switch (type)
        {
            case MissingTypeInstance v: break;
            case TypeInstanceFunction v: Stringify(v, builder, context); break;
            case TypeInstancePointer v: Stringify(v, builder, context); break;
            case TypeInstanceReference v: Stringify(v, builder, context); break;
            case TypeInstanceSimple v: Stringify(v, builder, context); break;
            case TypeInstanceStackArray v: Stringify(v, builder, context); break;
            default: throw new NotImplementedException(type.GetType().Name);
        }
    }
}
