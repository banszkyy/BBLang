using LanguageCore.Compiler;

namespace LanguageCore;

public static partial class Stringifier
{
    public static void Stringify(CompiledAliasTypeExpression type, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append(type.Definition.Identifier);
    }
    public static void Stringify(CompiledArrayTypeExpression type, BuilderBase builder, StringifyContext context = default)
    {
        Stringify(type.Of, builder);
        builder.Append('[');
        Stringify(type.Length, builder);
        builder.Append(']');
    }
    public static void Stringify(CompiledBuiltinTypeExpression type, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append(type.ToString());
    }
    public static void Stringify(CompiledEnumTypeExpression type, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append(type.Definition.Identifier);
    }
    public static void Stringify(CompiledFunctionTypeExpression type, BuilderBase builder, StringifyContext context = default)
    {
        if (type.HasClosure) builder.Append('@');
        Stringify(type.ReturnType, builder);
        builder.Append('(');
        for (int i = 0; i < type.Parameters.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
                builder.Space();
            }
            Stringify(type.Parameters[i], builder);
        }
        builder.Append(')');
    }
    public static void Stringify(CompiledGenericTypeExpression type, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append(type.Identifier);
    }
    public static void Stringify(CompiledPointerTypeExpression type, BuilderBase builder, StringifyContext context = default)
    {
        Stringify(type.To, builder);
        builder.Append('*');
    }
    public static void Stringify(CompiledReferenceTypeExpression type, BuilderBase builder, StringifyContext context = default)
    {
        Stringify(type.To, builder);
        builder.Append('&');
    }
    public static void Stringify(CompiledStructTypeExpression type, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append(type.Struct.Identifier);
        if (!type.TypeArguments.IsEmpty)
        {
            if (type.Struct.Definition.Template is null)
            {
                throw new UnreachableException();
            }
            if (!type.Struct.Definition.Template.OrderTypeArguments(type.TypeArguments, out ImmutableArray<CompiledTypeExpression> typeArguments))
            {
                throw new UnreachableException();
            }

            builder.Append('<');
            for (int i = 0; i < typeArguments.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                    builder.Space();
                }
                Stringify(typeArguments[i], builder);
            }
            builder.Append('>');
        }
    }
    public static void Stringify(CompiledTypeExpression type, BuilderBase builder, StringifyContext context = default)
    {
        switch (type)
        {
            case CompiledAliasTypeExpression v: Stringify(v, builder, context); break;
            case CompiledArrayTypeExpression v: Stringify(v, builder, context); break;
            case CompiledBuiltinTypeExpression v: Stringify(v, builder, context); break;
            case CompiledEnumTypeExpression v: Stringify(v, builder, context); break;
            case CompiledFunctionTypeExpression v: Stringify(v, builder, context); break;
            case CompiledGenericTypeExpression v: Stringify(v, builder, context); break;
            case CompiledPointerTypeExpression v: Stringify(v, builder, context); break;
            case CompiledReferenceTypeExpression v: Stringify(v, builder, context); break;
            case CompiledStructTypeExpression v: Stringify(v, builder, context); break;
            default: throw new UnreachableException(type.GetType().Name);
        }
    }
}
