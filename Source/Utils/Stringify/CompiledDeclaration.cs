using LanguageCore.Compiler;
using LanguageCore.Parser;
using LanguageCore.Tokenizing;

namespace LanguageCore;

public static partial class Stringifier
{
    public static void Stringify(CompiledVariableConstant constant, BuilderBase builder, StringifyContext context = default)
    {
        foreach (AttributeUsage attr in constant.Definition.Attributes)
        {
            Stringify(attr, builder);
            builder.NewLine();
        }

        foreach (Token modifier in constant.Definition.Modifiers)
        {
            builder.Append(modifier.Content);
            builder.Append(' ');
        }

        builder.Append(constant.Type.ToString());
        builder.Append(' ');
        builder.Append(constant.Identifier);
        if (!constant.Value.IsNull)
        {
            builder.Space();
            builder.Append('=');
            builder.Space();
            if (constant.Value.Type == RuntimeType.F32)
            {
                builder.Append(constant.Value.F32.ToString()); // TODO
            }
            else
            {
                builder.Append(constant.Value.ToString());
            }
        }
    }
    public static void Stringify(AttributeUsage attributeUsage, BuilderBase builder)
    {
        builder.Append('[');
        builder.Append(attributeUsage.Identifier.Content);
        if (!attributeUsage.Parameters.IsDefaultOrEmpty)
        {
            builder.Append('(');
            builder.AppendJoin(attributeUsage.Parameters, Stringify);
            builder.Append(')');
        }
        builder.Append(']');
    }

    public static void Stringify(ICompiledFunctionDefinition signature, BuilderBase builder)
    {
        foreach (AttributeUsage attr in signature.Attributes)
        {
            Stringify(attr, builder);
            builder.NewLine();
        }

        foreach (Token modifier in signature.Definition.Modifiers)
        {
            builder.Append(modifier.Content);
            builder.Append(' ');
        }

        if (signature is CompiledConstructorDefinition)
        {
            builder.Append(signature.Type.ToString());
        }
        else
        {
            builder.Append(signature.Type.ToString());
            builder.Append(' ');
            builder.Append(signature switch
            {
                CompiledFunctionDefinition v => v.Identifier,
                CompiledOperatorDefinition v => v.Identifier,
                CompiledGeneralFunctionDefinition v => v.Identifier,
                _ => throw new UnreachableException(signature.GetType().Name),
            });
            if (signature.Definition.Template is not null)
            {
                builder.Append('<');
                builder.AppendJoin(signature.Definition.Template.Parameters, (v, _, _) => builder.Append(v.Content));
                builder.Append('>');
            }
        }
        builder.Append('(');
        bool comma = false;
        for (int i = 0; i < signature.Parameters.Length; i++)
        {
            CompiledParameter parameter = signature.Parameters[i];

            if (parameter.Definition.IsThis
                && parameter.Identifier == StatementKeywords.This
                && signature.Definition is IInContext<StructDefinition?> { Context: not null })
            {
                continue;
            }

            if (comma)
            {
                builder.Append(',');
                builder.Space();
            }

            foreach (Token modifier in parameter.Definition.Modifiers)
            {
                builder.Append(modifier.Content);
                builder.Append(' ');
            }

            builder.Append(parameter.Type.ToString());
            builder.Append(' ');
            builder.Append(parameter.Identifier);

            if (parameter.Definition.DefaultValue is not null)
            {
                builder.Space();
                builder.Append('=');
                builder.Space();
                Stringify(parameter.Definition.DefaultValue, builder);
            }

            comma = true;
        }
        builder.Append(')');
    }

    public static void Stringify(CompiledStruct @struct, IEnumerable<CompiledFunction>? methods, BuilderBase builder)
    {
        foreach (AttributeUsage attr in @struct.Attributes)
        {
            Stringify(attr, builder);
            builder.NewLine();
        }

        foreach (Token modifier in @struct.Definition.Modifiers)
        {
            builder.Append(modifier.Content);
            builder.Append(' ');
        }

        builder.Append(DeclarationKeywords.Struct);
        builder.Append(' ');
        builder.Append(@struct.Identifier);
        if (@struct.Definition.Template is not null)
        {
            builder.Append('<');
            builder.AppendJoin(@struct.Definition.Template.Parameters, (v, _, _) => builder.Append(v.Content));
            builder.Append('>');
        }
        builder.NewLine();
        builder.Append('{');
        builder.IndentLevel++;

        for (int i = 0; i < @struct.Fields.Length; i++)
        {
            builder.NewLine();
            Stringify(@struct.Fields[i], builder);
        }

        if (methods is not null)
        {
            foreach (CompiledFunction method in methods)
            {
                builder.NewLine();
                Stringify(method.Function, method.Body, builder);
            }
        }

        builder.IndentLevel--;
        builder.NewLine();
        builder.Append('}');
    }

    public static void Stringify(CompiledField field, BuilderBase builder)
    {
        foreach (AttributeUsage attr in field.Attributes)
        {
            Stringify(attr, builder);
            builder.NewLine();
        }

        foreach (Token modifier in field.Definition.Modifiers)
        {
            builder.Append(modifier.Content);
            builder.Append(' ');
        }

        builder.Append(field.Type.ToString());
        builder.Append(' ');
        builder.Append(field.Identifier);
        builder.Append(';');
    }

    public static void Stringify(CompiledAlias alias, BuilderBase builder)
    {
        foreach (AttributeUsage attr in alias.Definition.Attributes)
        {
            Stringify(attr, builder);
            builder.NewLine();
        }

        foreach (Token modifier in alias.Definition.Modifiers)
        {
            builder.Append(modifier.Content);
            builder.Append(' ');
        }

        builder.Append(DeclarationKeywords.Alias);
        builder.Append(' ');
        builder.Append(alias.Identifier);
        builder.Append(' ');
        Stringify(alias.Value, builder);
        builder.Append(';');
    }

    public static void Stringify(CompiledEnum @enum, BuilderBase builder)
    {
        foreach (AttributeUsage attr in @enum.Definition.Attributes)
        {
            Stringify(attr, builder);
            builder.NewLine();
        }

        foreach (Token modifier in @enum.Definition.Modifiers)
        {
            builder.Append(modifier.Content);
            builder.Append(' ');
        }

        builder.Append(DeclarationKeywords.Enum);
        builder.Append(@enum.Identifier);

        if (@enum.Type is not null)
        {
            builder.Space();
            builder.Append(':');
            builder.Space();
            builder.Append(@enum.Type.ToString());
        }

        builder.NewLine();
        builder.Append('{');
        builder.IndentLevel++;

        for (int i = 0; i < @enum.Members.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
                builder.NewLine();
            }

            builder.Append(@enum.Members[i].Identifier);
            builder.Space();
            builder.Append('=');
            builder.Space();
            Stringify(@enum.Members[i].Value, builder);
        }

        builder.IndentLevel--;
        builder.NewLine();
        builder.Append('}');
    }

    public static bool NeedsSemicolon(CompiledStatement statement) => statement
        is not CompiledBlock
        and not CompiledWhileLoop
        and not CompiledForLoop
        and not CompiledIf
        and not CompiledElse
        and not CompiledLabelDeclaration;

    public static void Stringify(ICompiledFunctionDefinition signature, CompiledBlock body, BuilderBase builder)
    {
        Stringify(signature, builder);
        builder.NewLine();
        Stringify(body, builder);
    }
}
