using LanguageCore.Parser;
using LanguageCore.Tokenizing;

namespace LanguageCore;

public static partial class Stringifier
{
    public static void Stringify(StructDefinition @struct, BuilderBase builder, StringifyContext context = default)
    {
        foreach (AttributeUsage attr in @struct.Attributes)
        {
            Stringify(attr, builder);
            builder.NewLine();
        }

        foreach (Token modifier in @struct.Modifiers)
        {
            builder.Append(modifier.Content);
            builder.Append(' ');
        }

        builder.Append(DeclarationKeywords.Struct);
        builder.Append(' ');
        builder.Append(@struct.Identifier.Content);
        if (@struct.Template is not null)
        {
            builder.Append('<');
            builder.AppendJoin(@struct.Template.Parameters, (v, _, _) => builder.Append(v.Content));
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

        for (int i = 0; i < @struct.Constructors.Length; i++)
        {
            builder.NewLine();
            Stringify(@struct.Constructors[i], builder);
        }

        for (int i = 0; i < @struct.GeneralFunctions.Length; i++)
        {
            builder.NewLine();
            Stringify(@struct.GeneralFunctions[i], builder);
        }

        for (int i = 0; i < @struct.Functions.Length; i++)
        {
            builder.NewLine();
            Stringify(@struct.Functions[i], builder);
        }

        for (int i = 0; i < @struct.Operators.Length; i++)
        {
            builder.NewLine();
            Stringify(@struct.Operators[i], builder);
        }

        builder.IndentLevel--;
        builder.NewLine();
        builder.Append('}');
    }

    public static void Stringify(FieldDefinition field, BuilderBase builder)
    {
        foreach (AttributeUsage attr in field.Attributes)
        {
            Stringify(attr, builder);
            builder.NewLine();
        }

        foreach (Token modifier in field.Modifiers)
        {
            builder.Append(modifier.Content);
            builder.Append(' ');
        }

        Stringify(field.Type, builder);
        builder.Append(' ');
        builder.Append(field.Identifier.Content);
        builder.Append(';');
    }

    public static void Stringify(ConstructorDefinition function, BuilderBase builder)
    {
        foreach (AttributeUsage attr in function.Attributes)
        {
            Stringify(attr, builder);
            builder.NewLine();
        }

        foreach (Token modifier in function.Modifiers)
        {
            builder.Append(modifier.Content);
            builder.Append(' ');
        }

        Stringify(function.Type, builder);
        builder.Append('(');
        for (int i = 0; i < function.Parameters.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
                builder.Space();
            }

            Stringify(function.Parameters[i], builder);
        }
        builder.Append(')');

        if (function.Block is not null)
        {
            builder.NewLine();
            Stringify(function.Block, builder);
        }
    }

    public static void Stringify(FunctionDefinition function, BuilderBase builder)
    {
        foreach (AttributeUsage attr in function.Attributes)
        {
            Stringify(attr, builder);
            builder.NewLine();
        }

        foreach (Token modifier in function.Modifiers)
        {
            builder.Append(modifier.Content);
            builder.Append(' ');
        }

        Stringify(function.Type, builder);
        builder.Append(' ');
        builder.Append(function.Identifier.Content);
        TemplateInfo? template = function.Template ?? function.Context?.Template;
        if (template is not null)
        {
            builder.Append('<');
            builder.AppendJoin(template.Parameters, (v, _, _) => builder.Append(v.Content));
            builder.Append('>');
        }
        builder.Append('(');
        for (int i = 0; i < function.Parameters.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
                builder.Space();
            }

            Stringify(function.Parameters[i], builder);
        }
        builder.Append(')');

        if (function.Block is not null)
        {
            builder.NewLine();
            Stringify(function.Block, builder);
        }
    }

    public static void Stringify(GeneralFunctionDefinition function, BuilderBase builder)
    {
        foreach (AttributeUsage attr in function.Attributes)
        {
            Stringify(attr, builder);
            builder.NewLine();
        }

        foreach (Token modifier in function.Modifiers)
        {
            builder.Append(modifier.Content);
            builder.Append(' ');
        }

        builder.Append(function.Identifier.Content);
        TemplateInfo? template = function.Template ?? function.Context?.Template;
        if (template is not null)
        {
            builder.Append('<');
            builder.AppendJoin(template.Parameters, (v, _, _) => builder.Append(v.Content));
            builder.Append('>');
        }
        builder.Append('(');
        for (int i = 0; i < function.Parameters.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
                builder.Space();
            }

            Stringify(function.Parameters[i], builder);
        }
        builder.Append(')');

        if (function.Block is not null)
        {
            builder.NewLine();
            Stringify(function.Block, builder);
        }
    }

    public static void Stringify(AliasDefinition alias, BuilderBase builder)
    {
        foreach (AttributeUsage attr in alias.Attributes)
        {
            Stringify(attr, builder);
            builder.NewLine();
        }

        foreach (Token modifier in alias.Modifiers)
        {
            builder.Append(modifier.Content);
            builder.Append(' ');
        }

        builder.Append(DeclarationKeywords.Alias);
        builder.Append(' ');
        builder.Append(alias.Identifier.Content);
        builder.Append(' ');
        Stringify(alias.Value, builder);
        builder.Append(';');
    }

    public static void Stringify(EnumDefinition @enum, BuilderBase builder)
    {
        foreach (AttributeUsage attr in @enum.Attributes)
        {
            Stringify(attr, builder);
            builder.NewLine();
        }

        foreach (Token modifier in @enum.Modifiers)
        {
            builder.Append(modifier.Content);
            builder.Append(' ');
        }

        builder.Append(DeclarationKeywords.Enum);
        builder.Append(@enum.Identifier.Content);

        if (@enum.Type is not null)
        {
            builder.Space();
            builder.Append(':');
            builder.Space();
            Stringify(@enum.Type, builder);
        }

        builder.NewLine();
        builder.Append('{');
        builder.IndentLevel++;

        for (int i = 0; i < @enum.Members.Length; i++)
        {
            EnumMemberDefinition member = @enum.Members[i];

            if (i > 0)
            {
                builder.Append(',');
                builder.NewLine();
            }

            builder.Append(member.Identifier.Content);

            if (member.Value is not null)
            {
                builder.Space();
                builder.Append('=');
                builder.Space();
                Stringify(member.Value, builder);
            }
        }

        builder.IndentLevel--;
        builder.NewLine();
        builder.Append('}');
    }

    public static void Stringify(ParameterDefinition parameter, BuilderBase builder, StringifyContext context = default)
    {
        foreach (Token modifier in parameter.Modifiers)
        {
            builder.Append(modifier.Content);
            builder.Append(' ');
        }

        Stringify(parameter.Type, builder);
        builder.Append(' ');
        builder.Append(parameter.Identifier.Content);
        if (parameter.DefaultValue is not null)
        {
            builder.Space();
            builder.Append('=');
            builder.Space();
            Stringify(parameter.DefaultValue, builder);
        }
    }
}
