using System.IO;
using LanguageCore.Compiler;
using LanguageCore.Parser;
using LanguageCore.Parser.Statements;
using LanguageCore.Tokenizing;

namespace LanguageCore;

public static partial class Stringifier
{
    public static void Stringify(VariableDefinition statement, BuilderBase builder, StringifyContext context = default)
    {
        Stringify(statement.Type, builder);
        builder.Append(' ');
        builder.Append(statement.Identifier.Content);
        if (statement.InitialValue is not null)
        {
            builder.Space();
            builder.Append('=');
            builder.Space();
            Stringify(statement.InitialValue, builder);
        }
    }
    public static void Stringify(KeywordCallStatement statement, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append(statement.Keyword.Content);
        if (!statement.Arguments.IsDefaultOrEmpty)
        {
            builder.Append(' ');
            builder.AppendJoin(statement.Arguments, Stringify, () => builder.Append(' '));
        }
    }
    public static void Stringify(ShortOperatorCall statement, BuilderBase builder, StringifyContext context = default)
    {
        Stringify(statement.Target, builder, new() { IsNaked = true });
        builder.Append(statement.Operator.Content);
    }
    public static void Stringify(CompoundAssignmentStatement statement, BuilderBase builder, StringifyContext context = default)
    {
        Stringify(statement.Target, builder, new() { IsNaked = true });
        builder.Space();
        builder.Append(statement.Operator.Content);
        builder.Space();
        Stringify(statement.Value, builder, new() { IsNaked = true });
    }
    public static void Stringify(SimpleAssignmentStatement statement, BuilderBase builder, StringifyContext context = default)
    {
        Stringify(statement.Target, builder, new() { IsNaked = true });
        builder.Space();
        builder.Append(statement.Operator.Content);
        builder.Space();
        Stringify(statement.Value, builder, new() { IsNaked = true });
    }
    public static void Stringify(AssignmentStatement statement, BuilderBase builder, StringifyContext context = default)
    {
        switch (statement)
        {
            case ShortOperatorCall v: Stringify(v, builder, context); break;
            case CompoundAssignmentStatement v: Stringify(v, builder, context); break;
            case SimpleAssignmentStatement v: Stringify(v, builder, context); break;
            default: break;
        }
    }
    public static void Stringify(WhileLoopStatement statement, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append(StatementKeywords.While);
        builder.Space();
        builder.Append('(');
        Stringify(statement.Condition, builder);
        builder.Append(')');
        builder.NewLine();
        Stringify(statement.Body, builder);
    }
    public static void Stringify(ForLoopStatement statement, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append(StatementKeywords.For);
        builder.Space();
        builder.Append('(');

        if (statement.Initialization is not null) Stringify(statement.Initialization, builder);
        builder.Append(';');
        builder.Space();

        if (statement.Condition is not null) Stringify(statement.Condition, builder);
        builder.Append(';');
        builder.Space();

        if (statement.Step is not null) Stringify(statement.Step, builder);

        builder.Append(')');
        builder.NewLine();
        Stringify(statement.Body, builder);
    }
    public static void Stringify(ElseBranchStatement statement, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append(StatementKeywords.Else);
        if (statement.Body is IfBranchStatement ifBranch)
        {
            builder.Append(' ');
            Stringify(ifBranch, builder);
        }
        else
        {
            builder.NewLine();
            Stringify(statement.Body, builder);
        }
    }
    public static void Stringify(IfBranchStatement statement, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append(StatementKeywords.If);
        builder.Space();
        builder.Append('(');
        Stringify(statement.Condition, builder);
        builder.Append(')');
        builder.NewLine();
        Stringify(statement.Body, builder);

        if (statement.Else is not null)
        {
            builder.NewLine();
            Stringify(statement.Else, builder);
        }
    }
    public static void Stringify(Block statement, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append('{');
        builder.IndentLevel++;
        foreach (Statement item in statement.Statements)
        {
            builder.NewLine();
            Stringify(item, builder);
            if (item.Semicolon is not null)
            {
                builder.Append(';');
            }
        }
        builder.IndentLevel--;
        builder.NewLine();
        builder.Append('}');
    }
    public static void Stringify(InstructionLabelDeclaration statement, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append(statement.Identifier.Content);
        builder.Append(':');
    }
    public static void Stringify(Statement statement, BuilderBase builder, StringifyContext context = default)
    {
        switch (statement)
        {
            case Expression v: Stringify(v, builder, context); break;
            case VariableDefinition v: Stringify(v, builder, context); break;
            case KeywordCallStatement v: Stringify(v, builder, context); break;
            case AssignmentStatement v: Stringify(v, builder, context); break;
            case WhileLoopStatement v: Stringify(v, builder, context); break;
            case ForLoopStatement v: Stringify(v, builder, context); break;
            case IfBranchStatement v: Stringify(v, builder, context); break;
            case Block v: Stringify(v, builder, context); break;
            case InstructionLabelDeclaration v: Stringify(v, builder, context); break;
            case EmptyStatement: break;
            default: throw new NotImplementedException(statement.GetType().Name);
        }
    }

    public static void Stringify(UsingDefinition statement, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append(DeclarationKeywords.Using);
        builder.Append(' ');
        builder.Append(statement.PathString);
    }
}
