namespace LanguageCore.Compiler;

public static partial class StatementWalker
{
    public static void VisitWithFunctions(IReadOnlyCollection<CompiledFunction> functions, IEnumerable<CompiledStatement> statements, Func<CompiledStatement, bool> callback, Action<CompiledFunction> functionCallback)
    {
        foreach (CompiledStatement statement in statements) VisitWithFunctions(functions, statement, callback, functionCallback);
    }

    public static void VisitWithFunctions(IReadOnlyCollection<CompiledFunction> functions, CompiledStatement statement, Func<CompiledStatement, bool> callback, Action<CompiledFunction> functionCallback)
    {
        void TryFunctionCallback(ICompiledFunctionDefinition? function)
        {
            if (function is null) return;
            CompiledFunction? f = functions.FirstOrDefault(w => Utils.ReferenceEquals(w.Function, function) && StatementCompiler.TypeArgumentsEquals(w.TypeArguments, null));
            if (f is null) return;
            functionCallback(f);
        }

        Visit(statement, statement =>
        {
            switch (statement)
            {
                case CompiledCleanup v:
                    TryFunctionCallback(v.Deallocator?.Template);
                    TryFunctionCallback(v.Destructor?.Template);
                    break;
                case CompiledFunctionCall v:
                    TryFunctionCallback(v.Function.Template);
                    break;
                case CompiledExternalFunctionCall v:
                    TryFunctionCallback(v.Declaration);
                    break;
                case CompiledHeapAllocation v:
                    TryFunctionCallback(v.Allocator.Template);
                    break;
                case CompiledConstructorCall v:
                    TryFunctionCallback(v.Function.Template);
                    break;
                case CompiledDesctructorCall v:
                    TryFunctionCallback(v.Function.Template);
                    break;
                case CompiledFunctionReference v:
                    TryFunctionCallback(v.Function.Template);
                    break;
            }
            callback(statement);
            return true;
        });
    }

    public static bool Visit(IEnumerable<CompiledStatement> statement, Func<CompiledStatement, bool> callback)
    {
        foreach (CompiledStatement item in statement)
        {
            if (!Visit(item, callback)) return false;
        }
        return true;
    }
    public static bool Visit(CompiledStatement statement, Func<CompiledStatement, bool> callback)
    {
        return statement switch
        {
            CompiledExpression v => Visit(v, callback),
            CompiledEmptyStatement => true,
            CompiledBlock v => Visit(v, callback),
            CompiledIf v => Visit(v, callback),
            CompiledElse v => Visit(v, callback),
            CompiledVariableDefinition v => Visit(v, callback),
            CompiledCrash v => Visit(v, callback),
            CompiledDelete v => Visit(v, callback),
            CompiledReturn v => Visit(v, callback),
            CompiledBreak v => Visit(v, callback),
            CompiledGoto v => Visit(v, callback),
            CompiledLabelDeclaration v => Visit(v, callback),
            CompiledWhileLoop v => Visit(v, callback),
            CompiledForLoop v => Visit(v, callback),
            CompiledSetter v => Visit(v, callback),
            CompiledCleanup v => Visit(v, callback),
            _ => throw new UnreachableException(statement.GetType().Name),
        };
    }
    static bool Visit(CompiledCleanup statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        return true;
    }
    static bool Visit(CompiledExpression statement, Func<CompiledStatement, bool> callback)
    {
        return statement switch
        {
            CompiledDummyExpression v => Visit(v.Statement, callback),
            CompiledList v => Visit(v, callback),
            CompiledRuntimeCall v => Visit(v, callback),
            CompiledFunctionCall v => Visit(v, callback),
            CompiledExternalFunctionCall v => Visit(v, callback),
            CompiledSizeof v => Visit(v, callback),
            CompiledArgument v => Visit(v, callback),
            CompiledBinaryOperatorCall v => Visit(v, callback),
            CompiledUnaryOperatorCall v => Visit(v, callback),
            CompiledConstantValue v => Visit(v, callback),
            CompiledGetReference v => Visit(v, callback),
            CompiledDereference v => Visit(v, callback),
            CompiledStackAllocation v => Visit(v, callback),
            CompiledHeapAllocation v => Visit(v, callback),
            CompiledConstructorCall v => Visit(v, callback),
            CompiledDesctructorCall v => Visit(v, callback),
            CompiledCast v => Visit(v, callback),
            CompiledReinterpretation v => Visit(v, callback),
            CompiledElementAccess v => Visit(v, callback),
            CompiledVariableAccess v => Visit(v, callback),
            CompiledExpressionVariableAccess v => Visit(v, callback),
            CompiledParameterAccess v => Visit(v, callback),
            CompiledFieldAccess v => Visit(v, callback),
            CompiledRegisterAccess v => Visit(v, callback),
            CompiledString v => Visit(v, callback),
            CompiledStackString v => Visit(v, callback),
            CompiledFunctionReference v => Visit(v, callback),
            CompiledLabelReference v => Visit(v, callback),
            CompiledCompilerVariableAccess v => Visit(v, callback),
            CompiledLambda v => Visit(v, callback),
            CompiledEnumMemberAccess v => Visit(v, callback),
            _ => throw new UnreachableException(),
        };
    }
    static bool Visit(CompiledLambda statement, Func<CompiledStatement, bool> callback)
    {
        if (statement.Allocator is not null) if (!Visit(statement.Allocator, callback)) return false;
        if (!callback(statement)) return false;
        if (!Visit(statement.Block, callback)) return false;
        return true;
    }
    static bool Visit(CompiledEnumMemberAccess statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        if (!Visit(statement.EnumMember.Value, callback)) return false;
        return true;
    }
    static bool Visit(CompiledTypeExpression statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        switch (statement)
        {
            case CompiledAliasTypeExpression v:
                return Visit(v.Value, callback);
            case CompiledEnumTypeExpression v:
                return true;
            case CompiledArrayTypeExpression v:
                if (!Visit(v.Of, callback)) return false;
                if (v.Length is not null) if (!Visit(v.Length, callback)) return false;
                return true;
            case CompiledFunctionTypeExpression v:
                if (!Visit(v.ReturnType, callback)) return false;
                foreach (CompiledTypeExpression i in v.Parameters) if (!Visit(i, callback)) return false;
                return true;
            case CompiledPointerTypeExpression v:
                return Visit(v.To, callback);
            case CompiledReferenceTypeExpression v:
                return Visit(v.To, callback);
            case CompiledStructTypeExpression v:
                foreach (KeyValuePair<string, CompiledTypeExpression> i in v.TypeArguments) if (!Visit(i.Value, callback)) return false;
                return true;
            case CompiledBuiltinTypeExpression:
            case CompiledGenericTypeExpression:
                return true;
            default:
                throw new UnreachableException();
        }
    }
    static bool Visit(CompiledBlock statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        if (!Visit(statement.Statements, callback)) return false;
        return true;
    }
    static bool Visit(CompiledSetter statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        if (!Visit(statement.Target, callback)) return false;
        if (!Visit(statement.Value, callback)) return false;
        return true;
    }
    static bool Visit(CompiledIf statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        if (!Visit(statement.Condition, callback)) return false;
        if (!Visit(statement.Body, callback)) return false;
        if (statement.Next is not null) if (!Visit(statement.Next, callback)) return false;
        return true;
    }
    static bool Visit(CompiledElse statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        if (!Visit(statement.Body, callback)) return false;
        return true;
    }
    static bool Visit(CompiledVariableDefinition statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        if (!Visit(statement.TypeExpression, callback)) return false;
        if (statement.InitialValue is not null) if (!Visit(statement.InitialValue, callback)) return false;
        if (statement.Cleanup is not null) if (!Visit(statement.Cleanup, callback)) return false;
        return true;
    }
    static bool Visit(CompiledCrash statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        if (!Visit(statement.Value, callback)) return false;
        return true;
    }
    static bool Visit(CompiledDelete statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        if (!Visit(statement.Value, callback)) return false;
        return true;
    }
    static bool Visit(CompiledReturn statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        if (statement.Value is not null) if (!Visit(statement.Value, callback)) return false;
        return true;
    }
    static bool Visit(CompiledBreak statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        return true;
    }
    static bool Visit(CompiledGoto statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        if (!Visit(statement.Value, callback)) return false;
        return true;
    }
    static bool Visit(CompiledLabelDeclaration statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        return true;
    }
    static bool Visit(CompiledWhileLoop statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        if (!Visit(statement.Condition, callback)) return false;
        if (!Visit(statement.Body, callback)) return false;
        return true;
    }
    static bool Visit(CompiledForLoop statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        if (statement.Initialization is not null) if (!Visit(statement.Initialization, callback)) return false;
        if (statement.Condition is not null) if (!Visit(statement.Condition, callback)) return false;
        if (statement.Step is not null) if (!Visit(statement.Step, callback)) return false;
        if (!Visit(statement.Body, callback)) return false;
        return true;
    }
    static bool Visit(CompiledList statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        if (!Visit(statement.Values, callback)) return false;
        return true;
    }
    static bool Visit(CompiledRuntimeCall statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        if (!Visit(statement.Function, callback)) return false;
        if (!Visit(statement.Arguments, callback)) return false;
        return true;
    }
    static bool Visit(CompiledFunctionCall statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        if (!Visit(statement.Arguments, callback)) return false;
        return true;
    }
    static bool Visit(CompiledExternalFunctionCall statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        if (!Visit(statement.Arguments, callback)) return false;
        return true;
    }
    static bool Visit(CompiledSizeof statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        if (!Visit(statement.Of, callback)) return false;
        return true;
    }
    static bool Visit(CompiledArgument statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        if (!Visit(statement.Value, callback)) return false;
        if (!Visit(statement.Cleanup, callback)) return false;
        return true;
    }
    static bool Visit(CompiledBinaryOperatorCall statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        if (!Visit(statement.Left, callback)) return false;
        if (!Visit(statement.Right, callback)) return false;
        return true;
    }
    static bool Visit(CompiledUnaryOperatorCall statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        if (!Visit(statement.Expression, callback)) return false;
        return true;
    }
    static bool Visit(CompiledConstantValue statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        return true;
    }
    static bool Visit(CompiledGetReference statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        if (!Visit(statement.Of, callback)) return false;
        return true;
    }
    static bool Visit(CompiledDereference statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        if (!Visit(statement.Address, callback)) return false;
        return true;
    }
    static bool Visit(CompiledStackAllocation statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        if (!Visit(statement.TypeExpression, callback)) return false;
        return true;
    }
    static bool Visit(CompiledHeapAllocation statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        if (!Visit(statement.TypeExpression, callback)) return false;
        return true;
    }
    static bool Visit(CompiledConstructorCall statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        if (!Visit(statement.Object, callback)) return false;
        if (!Visit(statement.Arguments, callback)) return false;
        return true;
    }
    static bool Visit(CompiledDesctructorCall statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        if (!Visit(statement.Value, callback)) return false;
        return true;
    }
    static bool Visit(CompiledCast statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        if (!Visit(statement.Value, callback)) return false;
        if (!Visit(statement.TypeExpression, callback)) return false;
        return true;
    }
    static bool Visit(CompiledReinterpretation statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        if (!Visit(statement.Value, callback)) return false;
        if (!Visit(statement.TypeExpression, callback)) return false;
        return true;
    }
    static bool Visit(CompiledElementAccess statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        if (!Visit(statement.Base, callback)) return false;
        if (!Visit(statement.Index, callback)) return false;
        return true;
    }
    static bool Visit(CompiledExpressionVariableAccess statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        return true;
    }
    static bool Visit(CompiledVariableAccess statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        return true;
    }
    static bool Visit(CompiledParameterAccess statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        return true;
    }
    static bool Visit(CompiledFieldAccess statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        if (!Visit(statement.Object, callback)) return false;
        return true;
    }
    static bool Visit(CompiledRegisterAccess statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        return true;
    }
    static bool Visit(CompiledString statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        if (!Visit(statement.Allocator, callback)) return false;
        return true;
    }
    static bool Visit(CompiledStackString statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        return true;
    }
    static bool Visit(CompiledFunctionReference statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        return true;
    }
    static bool Visit(CompiledLabelReference statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        return true;
    }
    static bool Visit(CompiledCompilerVariableAccess statement, Func<CompiledStatement, bool> callback)
    {
        if (!callback(statement)) return false;
        return true;
    }
}
