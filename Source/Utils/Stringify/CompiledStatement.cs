using LanguageCore.Compiler;

namespace LanguageCore;

public static partial class Stringifier
{
    public static void Stringify(CompiledReturn statement, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append(StatementKeywords.Return);
        if (statement.Value is not null)
        {
            builder.Append(' ');
            Stringify(statement.Value, builder);
        }
    }
    public static void Stringify(CompiledVariableDefinition statement, BuilderBase builder, StringifyContext context = default)
    {
        if (statement.Cleanup.Deallocator is not null
            || statement.Cleanup.Destructor is not null)
        {
            builder.Append(ModifierKeywords.Temp);
            builder.Append(' ');
        }
        Stringify(statement.TypeExpression, builder);
        builder.Append(' ');
        builder.Append(statement.Identifier);
        if (statement.InitialValue is CompiledCompilerVariableAccess compilerVariableAccess
            && compilerVariableAccess.Identifier == statement.Definition.InternalConstantName)
        {

        }
        else if (statement.InitialValue is not null)
        {
            builder.Space();
            builder.Append('=');
            builder.Space();
            Stringify(statement.InitialValue, builder);
        }
    }
    public static void Stringify(CompiledSetter statement, BuilderBase builder, StringifyContext context = default)
    {
        Stringify(statement.Target, builder, new() { IsNaked = true });
        builder.Space();
        builder.Append('=');
        builder.Space();
        Stringify(statement.Value, builder);
    }
    public static void Stringify(CompiledWhileLoop statement, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append(StatementKeywords.While);
        builder.Space();
        builder.Append('(');
        Stringify(statement.Condition, builder);
        builder.Append(')');
        builder.NewLine();
        Stringify(statement.Body, builder);
    }
    public static void Stringify(CompiledBlock statement, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append('{');
        builder.IndentLevel++;
        foreach (CompiledStatement item in statement.Statements)
        {
            builder.NewLine();
            Stringify(item, builder);
            if (NeedsSemicolon(item))
            {
                builder.Append(';');
            }
        }
        builder.IndentLevel--;
        builder.NewLine();
        builder.Append('}');
    }
    public static void Stringify(CompiledForLoop statement, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append(StatementKeywords.For);
        builder.Space();
        builder.Append('(');
        Stringify(statement.Initialization, builder);
        builder.Append(';');
        builder.Space();
        Stringify(statement.Condition, builder);
        builder.Append(';');
        builder.Space();
        Stringify(statement.Step, builder);
        builder.Append(')');
        builder.NewLine();
        Stringify(statement.Body, builder);
    }
    public static void Stringify(CompiledIf statement, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append(StatementKeywords.If);
        builder.Space();
        builder.Append('(');
        Stringify(statement.Condition, builder);
        builder.Append(')');
        builder.NewLine();
        Stringify(statement.Body, builder);

        if (statement.Next is not null)
        {
            builder.NewLine();
            Stringify(statement.Next, builder);
        }
    }
    public static void Stringify(CompiledElse statement, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append(StatementKeywords.Else);
        if (statement.Body is CompiledIf compiledIf)
        {
            builder.Append(' ');
            Stringify(compiledIf, builder);
        }
        else
        {
            builder.NewLine();
            Stringify(statement.Body, builder);
        }
    }
    public static void Stringify(CompiledBreak statement, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append(StatementKeywords.Break);
    }
    public static void Stringify(CompiledCrash statement, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append(StatementKeywords.Crash);
        builder.Append(' ');
        Stringify(statement.Value, builder);
    }
    public static void Stringify(CompiledDelete statement, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append(StatementKeywords.Delete);
        builder.Append(' ');
        Stringify(statement.Value, builder);
    }
    public static void Stringify(CompiledGoto statement, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append(StatementKeywords.Goto);
        builder.Append(' ');
        Stringify(statement.Value, builder);
    }
    public static void Stringify(CompiledLabelDeclaration statement, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append(statement.Identifier);
        builder.Append(':');
    }
    public static void Stringify(CompiledStatement? statement, BuilderBase builder, StringifyContext context = default)
    {
        switch (statement)
        {
            case null: break;
            case CompiledEmptyStatement: break;
            case CompiledExpression v: Stringify(v, builder, context); break;
            case CompiledReturn v: Stringify(v, builder, context); break;
            case CompiledVariableDefinition v: Stringify(v, builder, context); break;
            case CompiledSetter v: Stringify(v, builder, context); break;
            case CompiledWhileLoop v: Stringify(v, builder, context); break;
            case CompiledBlock v: Stringify(v, builder, context); break;
            case CompiledForLoop v: Stringify(v, builder, context); break;
            case CompiledIf v: Stringify(v, builder, context); break;
            case CompiledElse v: Stringify(v, builder, context); break;
            case CompiledBreak v: Stringify(v, builder, context); break;
            case CompiledCrash v: Stringify(v, builder, context); break;
            case CompiledDelete v: Stringify(v, builder, context); break;
            case CompiledGoto v: Stringify(v, builder, context); break;
            case CompiledLabelDeclaration v: Stringify(v, builder, context); break;
            default: throw new NotImplementedException(statement.GetType().Name);
        }
    }
}
