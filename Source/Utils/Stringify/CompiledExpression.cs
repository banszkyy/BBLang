using LanguageCore.Compiler;

namespace LanguageCore;

public static partial class Stringifier
{
    public static void Stringify(CompiledConstantValue expression, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append(expression.Value.Type switch
        {
            RuntimeType.Null => throw new InvalidOperationException(),
            RuntimeType.U8 => expression.Value.U8.ToString(),
            RuntimeType.I8 => expression.Value.I8.ToString(),
            RuntimeType.U16 => expression.Value.U16.ToString(),
            RuntimeType.I16 => expression.Value.I16.ToString(),
            RuntimeType.U32 => expression.Value.U32.ToString(),
            RuntimeType.I32 => expression.Value.I32.ToString(),
            RuntimeType.F32 => expression.Value.F32.ToString(),
            _ => throw new UnreachableException(),
        });
    }
    public static void Stringify(CompiledBinaryOperatorCall expression, BuilderBase builder, StringifyContext context = default)
    {
        if (context.IsNaked) builder.Append('(');
        Stringify(expression.Left, builder, new() { IsNaked = true });
        builder.Space();
        builder.Append(expression.Operator);
        builder.Space();
        Stringify(expression.Right, builder, new() { IsNaked = true });
        if (context.IsNaked) builder.Append(')');
    }
    public static void Stringify(CompiledUnaryOperatorCall expression, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append(expression.Operator);
        Stringify(expression.Expression, builder, new() { IsNaked = true });
    }
    public static void Stringify(CompiledVariableAccess expression, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append(expression.Variable.Identifier);
    }
    public static void Stringify(CompiledParameterAccess expression, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append(expression.Parameter.Identifier);
    }
    public static void Stringify(CompiledFieldAccess expression, BuilderBase builder, StringifyContext context = default)
    {
        Stringify(expression.Object, builder, new() { IsNaked = true });
        builder.Append('.');
        builder.Append(expression.Field.Identifier);
    }
    public static void Stringify(CompiledDereference expression, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append('*');
        Stringify(expression.Address, builder, new() { IsNaked = true });
    }
    public static void Stringify(CompiledFunctionCall expression, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append(expression.Function.Template switch
        {
            CompiledFunctionDefinition v => v.Identifier,
            _ => throw new NotImplementedException(expression.Function.Template.GetType().Name),
        });
        builder.Append('(');
        builder.AppendJoin(expression.Arguments, Stringify);
        builder.Append(')');
    }
    public static void Stringify(CompiledExternalFunctionCall expression, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append(expression.Declaration switch
        {
            CompiledFunctionDefinition v => v.Identifier,
            _ => throw new NotImplementedException(expression.Declaration.GetType().Name),
        });
        builder.Append('(');
        builder.AppendJoin(expression.Arguments, Stringify);
        builder.Append(')');
    }
    public static void Stringify(CompiledGetReference expression, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append('&');
        Stringify(expression.Of, builder, new() { IsNaked = true });
    }
    public static void Stringify(CompiledReinterpretation expression, BuilderBase builder, StringifyContext context = default)
    {
        if (context.IsNaked) builder.Append('(');
        Stringify(expression.Value, builder, new() { IsNaked = true });
        builder.Append(' ');
        builder.Append(StatementKeywords.As);
        builder.Append(' ');
        Stringify(expression.TypeExpression, builder);
        if (context.IsNaked) builder.Append(')');
    }
    public static void Stringify(CompiledCast expression, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append('(');
        Stringify(expression.TypeExpression, builder);
        builder.Append(')');
        Stringify(expression.Value, builder, new() { IsNaked = true });
    }
    public static void Stringify(CompiledDummyExpression expression, BuilderBase builder, StringifyContext context = default)
    {
        Stringify(expression.Statement, builder);
    }
    public static void Stringify(CompiledSizeof expression, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append(StatementKeywords.Sizeof);
        builder.Append('(');
        Stringify(expression.Of, builder);
        builder.Append(')');
    }
    public static void Stringify(CompiledStackAllocation expression, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append(StatementKeywords.New);
        builder.Append(' ');
        Stringify(expression.TypeExpression, builder);
    }
    public static void Stringify(CompiledConstructorCall expression, BuilderBase builder, StringifyContext context = default)
    {
        if (expression.Object is CompiledStackAllocation stackAllocation)
        {
            Stringify(stackAllocation, builder);
        }
        else
        {
            builder.Append(StatementKeywords.New);
            builder.Append(' ');
            Stringify(expression.Object, builder);
        }
        builder.Append('(');
        builder.AppendJoin(expression.Arguments, Stringify);
        builder.Append(')');
    }
    public static void Stringify(CompiledString expression, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append('"');
        builder.Append(expression.Value.Escape());
        builder.Append('"');
    }
    public static void Stringify(CompiledElementAccess expression, BuilderBase builder, StringifyContext context = default)
    {
        Stringify(expression.Base, builder, new() { IsNaked = true });
        builder.Append('[');
        Stringify(expression.Index, builder);
        builder.Append(']');
    }
    public static void Stringify(CompiledFunctionReference expression, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append(expression.Function.Template switch
        {
            CompiledFunctionDefinition v => v.Identifier,
            _ => throw new NotImplementedException(expression.Function.Template.GetType().Name),
        });
    }
    public static void Stringify(CompiledArgument expression, BuilderBase builder, StringifyContext context = default)
    {
        if (expression.Cleanup.Deallocator is not null
            || expression.Cleanup.Destructor is not null)
        {
            builder.Append(ModifierKeywords.Temp);
            builder.Append(' ');
        }
        Stringify(expression.Value, builder, context);
    }
    public static void Stringify(CompiledRuntimeCall expression, BuilderBase builder, StringifyContext context = default)
    {
        Stringify(expression.Function, builder);
        builder.Append('(');
        builder.AppendJoin(expression.Arguments, Stringify);
        builder.Append(')');
    }
    public static void Stringify(CompiledRegisterAccess expression, BuilderBase builder, StringifyContext context = default)
    {
        throw new NotImplementedException();
    }
    public static void Stringify(CompiledLabelReference expression, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append(expression.InstructionLabel.Identifier);
    }
    public static void Stringify(CompiledList expression, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append('[');
        builder.AppendJoin(expression.Values, Stringify);
        builder.Append(']');
    }
    public static void Stringify(CompiledStackString expression, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append('"');
        builder.Append(expression.Value.Escape());
        builder.Append('"');
    }
    public static void Stringify(CompiledLambda expression, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append('(');
        builder.AppendJoin(expression.ParameterDefinitions.Parameters, Stringify);
        builder.Append(')');
        builder.Space();
        builder.Append("=>");
        builder.NewLine();
        Stringify(expression.Block, builder);
    }
    public static void Stringify(CompiledCompilerVariableAccess expression, BuilderBase builder, StringifyContext context = default)
    {
        if (expression.Definition is null) throw new UnreachableException();
        builder.Append(expression.Definition.Identifier);
    }
    public static void Stringify(CompiledEnumMemberAccess expression, BuilderBase builder, StringifyContext context = default)
    {
        builder.Append(expression.EnumMember.Enum.Identifier);
        builder.Append('.');
        builder.Append(expression.EnumMember.Identifier);
    }
    public static void Stringify(CompiledExpression? expression, BuilderBase builder, StringifyContext context = default)
    {
        switch (expression)
        {
            case null: break;
            case CompiledConstantValue v: Stringify(v, builder, context); break;
            case CompiledBinaryOperatorCall v: Stringify(v, builder, context); break;
            case CompiledUnaryOperatorCall v: Stringify(v, builder, context); break;
            case CompiledVariableAccess v: Stringify(v, builder, context); break;
            case CompiledParameterAccess v: Stringify(v, builder, context); break;
            case CompiledFieldAccess v: Stringify(v, builder, context); break;
            case CompiledDereference v: Stringify(v, builder, context); break;
            case CompiledFunctionCall v: Stringify(v, builder, context); break;
            case CompiledExternalFunctionCall v: Stringify(v, builder, context); break;
            case CompiledGetReference v: Stringify(v, builder, context); break;
            case CompiledReinterpretation v: Stringify(v, builder, context); break;
            case CompiledCast v: Stringify(v, builder, context); break;
            case CompiledDummyExpression v: Stringify(v.Statement, builder, context); break;
            case CompiledSizeof v: Stringify(v, builder, context); break;
            case CompiledStackAllocation v: Stringify(v, builder, context); break;
            case CompiledConstructorCall v: Stringify(v, builder, context); break;
            case CompiledString v: Stringify(v, builder, context); break;
            case CompiledElementAccess v: Stringify(v, builder, context); break;
            case CompiledFunctionReference v: Stringify(v, builder, context); break;
            case CompiledRuntimeCall v: Stringify(v, builder, context); break;
            case CompiledRegisterAccess v: Stringify(v, builder, context); break;
            case CompiledLabelReference v: Stringify(v, builder, context); break;
            case CompiledList v: Stringify(v, builder, context); break;
            case CompiledStackString v: Stringify(v, builder, context); break;
            case CompiledLambda v: Stringify(v, builder, context); break;
            case CompiledCompilerVariableAccess v: Stringify(v, builder, context); break;
            case CompiledEnumMemberAccess v: Stringify(v, builder, context); break;
            default: throw new NotImplementedException(expression.GetType().Name);
        }
    }
}
