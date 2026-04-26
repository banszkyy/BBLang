using LanguageCore.Parser;
using LanguageCore.Parser.Statements;
using LanguageCore.Runtime;
using LanguageCore.Tokenizing;

namespace LanguageCore.Compiler;

public partial class StatementCompiler : IRuntimeInfoProvider
{
    #region Fields

    readonly List<CompiledStruct> CompiledStructs = new();
    readonly List<CompiledOperatorDefinition> CompiledOperators = new();
    readonly List<CompiledConstructorDefinition> CompiledConstructors = new();
    readonly List<CompiledFunctionDefinition> CompiledFunctions = new();
    readonly List<CompiledGeneralFunctionDefinition> CompiledGeneralFunctions = new();
    readonly List<CompiledAlias> CompiledAliases = new();
    readonly List<CompiledEnum> CompiledEnums = new();

    readonly Stack<CompiledVariableConstant> CompiledGlobalConstants = new();
    readonly Stack<CompiledVariableDefinition> CompiledGlobalVariables = new();

    readonly DiagnosticsCollection Diagnostics;

    readonly ImmutableArray<IExternalFunction> ExternalFunctions;
    readonly ImmutableArray<ExternalConstant> ExternalConstants;
    readonly ImmutableDictionary<string, GeneralType> InternalConstants;

    public BuiltinType ArrayLengthType => Settings.ArrayLengthType;
    public BuiltinType BooleanType => Settings.BooleanType;
    public int PointerSize => Settings.PointerSize;
    public BuiltinType SizeofStatementType => Settings.SizeofStatementType;
    public BuiltinType ExitCodeType => Settings.ExitCodeType;

    readonly CompilerSettings Settings;
    readonly List<CompiledFunction> GeneratedFunctions = new();

    readonly List<TemplateInstance<CompiledFunctionDefinition>> CompilableFunctions = new();
    readonly List<TemplateInstance<CompiledOperatorDefinition>> CompilableOperators = new();
    readonly List<TemplateInstance<CompiledGeneralFunctionDefinition>> CompilableGeneralFunctions = new();
    readonly List<TemplateInstance<CompiledConstructorDefinition>> CompilableConstructors = new();

    readonly List<FunctionDefinition> OperatorDefinitions = new();
    readonly List<FunctionDefinition> FunctionDefinitions = new();
    readonly List<StructDefinition> StructDefinitions = new();
    readonly List<AliasDefinition> AliasDefinitions = new();
    readonly List<EnumDefinition> EnumDefinitions = new();

    readonly List<(ImmutableArray<Statement> Statements, Uri File)> TopLevelStatements = new();

    readonly Stack<ImmutableArray<Token>> GenericParameters = new();
    readonly ImmutableArray<UserDefinedAttribute> UserDefinedAttributes;
    readonly ImmutableHashSet<string> PreprocessorVariables;
    readonly ImmutableArray<CompiledStatement>.Builder CompiledTopLevelStatements = ImmutableArray.CreateBuilder<CompiledStatement>();
    readonly Stack<CompiledFrame> Frames;
    readonly ILogger? Logger;

    #endregion

    enum ConstantPerfectus
    {
        None,
        Name,
        File,
    }

    bool GetConstant(string identifier, Uri file, [NotNullWhen(true)] out CompiledVariableConstant? constant, [NotNullWhen(false)] out PossibleDiagnostic? notFoundError)
    {
        constant = null;
        notFoundError = null;
        ConstantPerfectus perfectus = ConstantPerfectus.None;

        foreach (Scope item in Frames.Last.Scopes)
        {
            foreach (CompiledVariableConstant _constant in item.Constants)
            {
                if (_constant.Identifier != identifier)
                {
                    if (perfectus < ConstantPerfectus.Name ||
                        notFoundError is null)
                    { notFoundError = new PossibleDiagnostic($"Constant \"{_constant.Identifier}\" not found"); }
                    continue;
                }
                perfectus = ConstantPerfectus.Name;

                if (!_constant.Definition.CanUse(file))
                {
                    if (perfectus < ConstantPerfectus.File ||
                        notFoundError is null)
                    { notFoundError = new PossibleDiagnostic($"Constant \"{_constant.Identifier}\" cannot be used due to its protection level"); }
                    continue;
                }
                perfectus = ConstantPerfectus.File;

                if (constant is not null)
                {
                    if (perfectus <= ConstantPerfectus.File ||
                        notFoundError is null)
                    { notFoundError = new PossibleDiagnostic($"Constant \"{_constant.Identifier}\" not found: multiple constants found"); }
                    return false;
                }

                constant = _constant;
            }
        }

        foreach (CompiledVariableConstant _constant in CompiledGlobalConstants)
        {
            if (_constant.Identifier != identifier)
            {
                if (perfectus < ConstantPerfectus.Name ||
                    notFoundError is null)
                { notFoundError = new PossibleDiagnostic($"Constant \"{identifier}\" not found"); }
                continue;
            }
            perfectus = ConstantPerfectus.Name;

            if (!_constant.Definition.CanUse(file))
            {
                if (perfectus < ConstantPerfectus.File ||
                    notFoundError is null)
                { notFoundError = new PossibleDiagnostic($"Constant \"{identifier}\" cannot be used due to its protection level"); }
                continue;
            }
            perfectus = ConstantPerfectus.File;

            if (constant is not null)
            {
                if (perfectus <= ConstantPerfectus.File ||
                    notFoundError is null)
                { notFoundError = new PossibleDiagnostic($"Constant \"{identifier}\" not found: multiple constants found"); }
                return false;
            }

            constant = _constant;
        }

        if (constant is null)
        {
            notFoundError = new PossibleDiagnostic($"Constant \"{identifier}\" not found");
            return false;
        }

        return true;
    }

    bool StatementCanBeDeallocated(ArgumentExpression statement, out bool explicitly)
    {
        if (statement.Modifier?.Content == ModifierKeywords.Temp)
        {
            if (statement.Value is
                LiteralExpression or
                BinaryOperatorCallExpression or
                UnaryOperatorCallExpression)
            {
                Diagnostics.Add(DiagnosticAt.Hint($"Unnecessary explicit temp modifier (\"{statement.Value.GetType().Name}\" statements are implicitly deallocated)", statement.Modifier, statement.File).WithTag(DiagnosticTag.Unnecessary));
            }

            explicitly = true;
            return true;
        }

        if (statement.Value is LiteralExpression)
        {
            explicitly = false;
            return true;
        }

        if (statement.Value is BinaryOperatorCallExpression)
        {
            explicitly = false;
            return true;
        }

        if (statement.Value is UnaryOperatorCallExpression)
        {
            explicitly = false;
            return true;
        }

        explicitly = default;
        return false;
    }

    public static bool AllowDeallocate(GeneralType type) => type.Is<PointerType>() || (type.Is(out FunctionType? functionType) && functionType.HasClosure);

    public static bool TypeArgumentsEquals(ImmutableDictionary<string, GeneralType>? a, ImmutableDictionary<string, GeneralType>? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        if (a.Count != b.Count) return false;
        foreach ((string? key, GeneralType? v) in a)
        {
            if (!b.TryGetValue(key, out GeneralType? w)) return false;
            if (!v.Equals(w)) return false;
        }
        return true;
    }

    static bool FunctionEquals<TFunction>(TemplateInstance<TFunction> a, FunctionMatch<TFunction> b) where TFunction : class
    {
        return Utils.ReferenceEquals(a.Template, b.Function) && TypeArgumentsEquals(a.TypeArguments, b.TypeArguments);
    }

    #region AddCompilable()

    void AddCompilable(TemplateInstance<CompiledFunctionDefinition> compilable)
    {
        for (int i = 0; i < CompilableFunctions.Count; i++)
        {
            if (CompilableFunctions[i].Template.IsSame(compilable.Template)
                && TypeArgumentsEquals(CompilableFunctions[i].TypeArguments, compilable.TypeArguments))
            { return; }
        }
        CompilableFunctions.Add(compilable);
    }

    void AddCompilable(TemplateInstance<CompiledOperatorDefinition> compilable)
    {
        for (int i = 0; i < CompilableOperators.Count; i++)
        {
            if (CompilableOperators[i].Template.IsSame(compilable.Template)
                && TypeArgumentsEquals(CompilableFunctions[i].TypeArguments, compilable.TypeArguments))
            { return; }
        }
        CompilableOperators.Add(compilable);
    }

    void AddCompilable(TemplateInstance<CompiledGeneralFunctionDefinition> compilable)
    {
        for (int i = 0; i < CompilableGeneralFunctions.Count; i++)
        {
            if (CompilableGeneralFunctions[i].Template.IsSame(compilable.Template)
                && TypeArgumentsEquals(CompilableFunctions[i].TypeArguments, compilable.TypeArguments))
            { return; }
        }
        CompilableGeneralFunctions.Add(compilable);
    }

    TemplateInstance<CompiledConstructorDefinition> AddCompilable(TemplateInstance<CompiledConstructorDefinition> compilable)
    {
        for (int i = 0; i < CompilableConstructors.Count; i++)
        {
            if (CompilableConstructors[i].Template.IsSame(compilable.Template)
                && TypeArgumentsEquals(CompilableFunctions[i].TypeArguments, compilable.TypeArguments))
            { return CompilableConstructors[i]; }
        }
        CompilableConstructors.Add(compilable);
        return compilable;
    }

    #endregion

    bool GetLocalSymbolType(IdentifierExpression symbolName, [NotNullWhen(true)] out GeneralType? type)
    {
        if (GetVariable(symbolName.Content, out CompiledVariableDefinition? variable, out _))
        {
            type = variable.Type;
            return true;
        }

        if (GetParameter(symbolName.Content, out CompiledParameter? parameter, out _))
        {
            type = parameter.Type;
            return true;
        }

        type = null;
        return false;
    }

    #region Get Functions ...

    public readonly struct Functions<TFunction> where TFunction : notnull
    {
        public IEnumerable<TFunction> Compiled { get; init; }
        public IEnumerable<TemplateInstance<TFunction>> Compilable { get; init; }
    }

    [DebuggerStepThrough]
    Functions<CompiledFunctionDefinition> GetFunctions() => new()
    {
        Compiled = CompiledFunctions,
        Compilable = CompilableFunctions,
    };

    [DebuggerStepThrough]
    Functions<CompiledOperatorDefinition> GetOperators() => new()
    {
        Compiled = CompiledOperators,
        Compilable = CompilableOperators,
    };

    [DebuggerStepThrough]
    Functions<CompiledGeneralFunctionDefinition> GetGeneralFunctions() => new()
    {
        Compiled = CompiledGeneralFunctions,
        Compilable = CompilableGeneralFunctions,
    };

    [DebuggerStepThrough]
    Functions<CompiledConstructorDefinition> GetConstructors() => new()
    {
        Compiled = CompiledConstructors,
        Compilable = CompilableConstructors,
    };

    bool GetConstructor(
        CompiledExpression objectArg,
        ImmutableArray<CompiledExpression> arguments,
        Uri relevantFile,

        [NotNullWhen(true)] out FunctionQueryResult<CompiledConstructorDefinition>? result,
        [NotNullWhen(false)] out PossibleDiagnostic? error,
        Action<TemplateInstance<CompiledConstructorDefinition>>? addCompilable = null)
    {
        StructType? structType;

        {
            ImmutableArray<CompiledExpression>.Builder argumentsBuilder = ImmutableArray.CreateBuilder<CompiledExpression>();

            if (objectArg.Type.Is(out PointerType? pointerType))
            {
                if (!pointerType.To.Is<StructType>(out structType))
                {
                    result = null;
                    error = new PossibleDiagnostic($"Invalid type \"{objectArg}\" used for constructor");
                    return false;
                }
                argumentsBuilder.Add(objectArg);
            }
            else if (objectArg.Type.Is(out structType))
            {
                argumentsBuilder.Add(new CompiledGetReference()
                {
                    Of = objectArg,
                    Type = new PointerType(structType),
                    Location = objectArg.Location,
                    SaveValue = objectArg.SaveValue,
                });
            }
            else
            {
                result = null;
                error = new PossibleDiagnostic($"Invalid type \"{objectArg}\" used for constructor");
                return false;
            }

            argumentsBuilder.AddRange(arguments);
            arguments = argumentsBuilder.ToImmutable();
        }

        Functions<CompiledConstructorDefinition> constructors = GetConstructors();

        constructors = new Functions<CompiledConstructorDefinition>()
        {
            Compiled = constructors.Compiled.Where(v => Utils.ReferenceEquals(v.Context, structType.Struct)),
            Compilable = constructors.Compilable.Where(v => Utils.ReferenceEquals(v.Template.Context, structType.Struct)),
        };

        return GetFunction<CompiledConstructorDefinition, GeneralType, GeneralType, CompiledExpression>(
            constructors,
            "constructor",
            CompiledConstructorDefinition.ToReadable(objectArg.Type, arguments),

            FunctionQuery.Create(objectArg.Type, arguments, relevantFile, null, addCompilable, (GeneralType passed, GeneralType defined, out int badness) =>
            {
                badness = 0;
                if (passed.Is(out PointerType? passedPointerType))
                {
                    if (defined.Is(out PointerType? definedPointerType))
                    {
                        badness = 0;
                        return passedPointerType.To.Equals(definedPointerType.To);
                    }
                    else if (defined.Is(out StructType? definedStructType))
                    {
                        badness = 1;
                        return passedPointerType.To.Equals(definedStructType);
                    }
                    else
                    {
                        return false;
                    }
                }
                else if (passed.Is(out StructType? passedStructType))
                {
                    if (defined.Is(out PointerType? definedPointerType))
                    {
                        badness = 1;
                        return passedStructType.Equals(definedPointerType.To);
                    }
                    else if (defined.Is(out StructType? definedStructType))
                    {
                        badness = 0;
                        return passedStructType.Equals(definedStructType);
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }),

            out result,
            out error
        );
    }

    bool GetIndexGetter(
        CompiledExpression prevType,
        CompiledExpression indexType,
        Uri relevantFile,

        [NotNullWhen(true)] out FunctionQueryResult<CompiledFunctionDefinition>? result,
        [NotNullWhen(false)] out PossibleDiagnostic? error,
        Action<TemplateInstance<CompiledFunctionDefinition>>? addCompilable = null)
    {
        ImmutableArray<CompiledExpression> arguments = ImmutableArray.Create(prevType, indexType);
        FunctionQuery<CompiledFunctionDefinition, string, string, CompiledExpression> query = FunctionQuery.Create<CompiledFunctionDefinition, string, string>(BuiltinFunctionIdentifiers.IndexerGet, arguments, relevantFile, null, addCompilable);
        return GetFunction<CompiledFunctionDefinition, string, string, CompiledExpression>(
            GetFunctions(),
            "function",
            CompiledFunctionDefinition.ToReadable(BuiltinFunctionIdentifiers.IndexerGet, arguments.Select(v => v.Type.ToString()), null),

            query,

            out result,
            out error
        ) && result.Success;
    }

    bool GetIndexSetter(
        CompiledExpression prevType,
        CompiledExpression elementType,
        CompiledExpression indexType,
        Uri relevantFile,

        [NotNullWhen(true)] out FunctionQueryResult<CompiledFunctionDefinition>? result,
        [NotNullWhen(false)] out PossibleDiagnostic? error,
        Action<TemplateInstance<CompiledFunctionDefinition>>? addCompilable = null)
    {
        ImmutableArray<CompiledExpression> arguments = ImmutableArray.Create(prevType, indexType, elementType);
        FunctionQuery<CompiledFunctionDefinition, string, string, CompiledExpression> query = FunctionQuery.Create<CompiledFunctionDefinition, string, string>(BuiltinFunctionIdentifiers.IndexerSet, arguments, relevantFile, null, addCompilable);
        return GetFunction<CompiledFunctionDefinition, string, string, CompiledExpression>(
            GetFunctions(),
            "function",
            CompiledFunctionDefinition.ToReadable(BuiltinFunctionIdentifiers.IndexerSet, arguments.Select(v => v.Type.ToString()), null),

            query,

            out result,
            out error
        ) && result.Success;
    }

    bool TryGetBuiltinFunction(
        string builtinName,
        ImmutableArray<GeneralType> arguments,
        Uri relevantFile,

        [NotNullWhen(true)] out FunctionQueryResult<CompiledFunctionDefinition>? result,
        [NotNullWhen(false)] out PossibleDiagnostic? error,
        Action<TemplateInstance<CompiledFunctionDefinition>>? addCompilable = null)
    {
        IEnumerable<CompiledFunctionDefinition> builtinCompiledFunctions =
            CompiledFunctions
            .Where(v => v.Definition.BuiltinFunctionName == builtinName);

        IEnumerable<TemplateInstance<CompiledFunctionDefinition>> builtinCompilableFunctions =
            CompilableFunctions
            .Where(v => v.Template.Definition.BuiltinFunctionName == builtinName);

        string readable = $"[{AttributeConstants.BuiltinIdentifier}(\"{builtinName}\")] ?({string.Join(", ", arguments)})";
        FunctionQuery<CompiledFunctionDefinition, string, string, GeneralType> query = FunctionQuery.Create<CompiledFunctionDefinition, string, string>(null as string, arguments, relevantFile, null, addCompilable);

        return GetFunction<CompiledFunctionDefinition, string, string, GeneralType>(
            new Functions<CompiledFunctionDefinition>()
            {
                Compiled = builtinCompiledFunctions,
                Compilable = builtinCompilableFunctions,
            },
            "builtin function",
            readable,

            query,

            out result,
            out error
        );
    }

    bool GetOperator(
        string identifier,
        ImmutableArray<CompiledExpression> arguments,
        Uri relevantFile,

        [NotNullWhen(true)] out FunctionQueryResult<CompiledOperatorDefinition>? result,
        [NotNullWhen(false)] out PossibleDiagnostic? error,
        Action<TemplateInstance<CompiledOperatorDefinition>>? addCompilable = null)
    {
        FunctionQuery<CompiledOperatorDefinition, string, string, CompiledExpression> query = FunctionQuery.Create<CompiledOperatorDefinition, string, string>(
            identifier,
            arguments,
            relevantFile,
            null,
            addCompilable);
        return GetFunction<CompiledOperatorDefinition, string, string, CompiledExpression>(
            GetOperators(),
            "operator",
            CompiledFunctionDefinition.ToReadable(identifier, arguments.Select(v => v.Type.ToString()), null),

            query,

            out result,
            out error
        ) && result.Success;
    }

    bool GetGeneralFunction(
        GeneralType context,
        ImmutableArray<GeneralType> arguments,
        string identifier,
        Uri relevantFile,

        [NotNullWhen(true)] out FunctionQueryResult<CompiledGeneralFunctionDefinition>? result,
        [NotNullWhen(false)] out PossibleDiagnostic? error,
        Action<TemplateInstance<CompiledGeneralFunctionDefinition>>? addCompilable = null)
    {
        IEnumerable<CompiledGeneralFunctionDefinition> compiledGeneralFunctionsInContext =
            CompiledGeneralFunctions
            .Where(v => ContextIs(v, context));

        IEnumerable<TemplateInstance<CompiledGeneralFunctionDefinition>> compilableGeneralFunctionsInContext =
            CompilableGeneralFunctions
            .Where(v => ContextIs(v.Template, context));

        return GetFunction<CompiledGeneralFunctionDefinition, string, string, GeneralType>(
            new Functions<CompiledGeneralFunctionDefinition>()
            {
                Compiled = compiledGeneralFunctionsInContext,
                Compilable = compilableGeneralFunctionsInContext,
            },
            "general function",
            CompiledFunctionDefinition.ToReadable(identifier, arguments.Select(v => v.ToString()), null),

            FunctionQuery.Create<CompiledGeneralFunctionDefinition, string, string>(identifier, arguments, relevantFile, null, addCompilable),

            out result,
            out error
        );
    }

    bool GetFunction(
        string identifier,
        GeneralType? type,
        [NotNullWhen(true)] out FunctionQueryResult<CompiledFunctionDefinition>? result,
        [NotNullWhen(false)] out PossibleDiagnostic? error,
        Action<TemplateInstance<CompiledFunctionDefinition>>? addCompilable = null)
    {
        if (type is null || !type.Is(out FunctionType? functionType))
        {
            return GetFunction(
                FunctionQuery.Create<CompiledFunctionDefinition, string, string>(identifier),
                CompiledFunctionDefinition.ToReadable(identifier, null, null),
                out result,
                out error
            );
        }

        return GetFunction(
            FunctionQuery.Create<CompiledFunctionDefinition, string, string>(identifier, functionType.Parameters, null, functionType.ReturnType, addCompilable),
            CompiledFunctionDefinition.ToReadable(identifier, functionType.Parameters.Select(v => v.ToString()), functionType.ReturnType.ToString()),
            out result,
            out error
        );
    }

    bool GetFunction(
        string identifier,
        ImmutableArray<CompiledExpression> arguments,
        Uri file,

        [NotNullWhen(true)] out FunctionQueryResult<CompiledFunctionDefinition>? result,
        [NotNullWhen(false)] out PossibleDiagnostic? error,
        Action<TemplateInstance<CompiledFunctionDefinition>>? addCompilable = null)
    {
        FunctionQuery<CompiledFunctionDefinition, string, string, CompiledExpression> query = FunctionQuery.Create<CompiledFunctionDefinition, string, string>(
            identifier,
            arguments,
            file,
            null,
            addCompilable);
        return GetFunction<CompiledExpression>(
            query,
            CompiledFunctionDefinition.ToReadable(identifier, arguments.Select(v => v.Type.ToString()), null),
            out result,
            out error
        );
    }

    bool GetFunction<TArgument>(
        FunctionQuery<CompiledFunctionDefinition, string, string, TArgument> query,
        string readableName,

        [NotNullWhen(true)] out FunctionQueryResult<CompiledFunctionDefinition>? result,
        [NotNullWhen(false)] out PossibleDiagnostic? error)
        where TArgument : notnull
        => GetFunction<CompiledFunctionDefinition, string, string, TArgument>(
            GetFunctions(),
            "function",
            readableName,

            query,
            out result,
            out error
        );

    #endregion

    static bool ContextIs(CompiledGeneralFunctionDefinition function, GeneralType type) =>
        type.Is(out StructType? structType) &&
        function.Context is not null &&
        Utils.ReferenceEquals(function.Context, structType.Struct);

    #region CompileConstant()

    bool CompileConstant(VariableDefinition variableDefinition, [NotNullWhen(true)] out CompiledVariableConstant? result)
    {
        result = null;
        variableDefinition.Identifier.AnalyzedType = TokenAnalyzedType.ConstantName;

        if (GetConstant(variableDefinition.Identifier.Content, variableDefinition.File, out _, out _))
        { Diagnostics.Add(DiagnosticAt.Error($"Constant \"{variableDefinition.Identifier}\" already defined", variableDefinition.Identifier, variableDefinition.File)); }

        CompileVariableAttributes(variableDefinition);

        GeneralType? constantType = null;
        if (variableDefinition.Type != StatementKeywords.Var)
        {
            CompileType(variableDefinition.Type, out constantType, Diagnostics);
        }

        CompiledValue constantValue;

        if (variableDefinition.ExternalConstantName is not null)
        {
            ExternalConstant? externalConstant = ExternalConstants.FirstOrDefault(v => v.Name == variableDefinition.ExternalConstantName);
            if (externalConstant is not null)
            {
                constantValue = externalConstant.Value;
                goto gotExternalValue;
            }
            else if (variableDefinition.InitialValue is null)
            {
                Diagnostics.Add(DiagnosticAt.Error($"External constant \"{variableDefinition.ExternalConstantName}\" not found", variableDefinition));
                constantValue = default;
            }
        }

        if (variableDefinition.InternalConstantName is not null)
        {
            if (!InternalConstants.TryGetValue(variableDefinition.InternalConstantName, out GeneralType? internalConstantType))
            {
                Diagnostics.Add(DiagnosticAt.Warning($"Internal constant \"{variableDefinition.InternalConstantName}\" not found", variableDefinition));
            }
            else
            {
                constantType ??= internalConstantType;

                if (!GetInitialValue(constantType, out constantValue))
                {
                    Diagnostics.Add(DiagnosticAt.Error($"Invalid type `{constantType}` specified for internal constant", variableDefinition.Type));
                    constantValue = default;
                }

                goto gotExternalValue;
            }
        }

        if (variableDefinition.InitialValue is null)
        {
            Diagnostics.Add(DiagnosticAt.Error($"Constant value must have initial value", variableDefinition));
            constantValue = default;
        }
        else
        {
            if (CompileExpression(variableDefinition.InitialValue, out CompiledExpression? compiledInitialValue, constantType))
            {
                if (!TryCompute(compiledInitialValue, out constantValue, out PossibleDiagnostic? evaluationError))
                {
                    Diagnostics.Add(DiagnosticAt.Error($"Constant value must be evaluated at compile-time", variableDefinition.InitialValue).WithSuberrors(evaluationError.ToError(variableDefinition.InitialValue)));
                    constantValue = default;
                }
            }
            else
            {
                constantValue = default;
            }
        }

    gotExternalValue:

        if (constantType is not null)
        {
            if (!constantValue.IsNull && constantType.Is(out BuiltinType? builtinType))
            {
                if (!constantValue.TryCast(builtinType.RuntimeType, out CompiledValue castedConstantValue))
                {
                    Diagnostics.Add(DiagnosticAt.Error($"Can't cast constant value {constantValue} of type \"{constantValue.Type}\" to {constantType}", variableDefinition.InitialValue?.Location ?? variableDefinition.Location));
                }
                else
                {
                    constantValue = castedConstantValue;
                }
            }
        }
        else
        {
            if (!CompileType(constantValue.Type, out constantType, out PossibleDiagnostic? typeError))
            {
                Diagnostics.Add(typeError.ToError(variableDefinition.InitialValue?.Location ?? variableDefinition.Location));
                return false;
            }
        }

        result = new CompiledVariableConstant(constantValue, constantType, variableDefinition);
        SetStatementReference(variableDefinition, result);
        return true;
    }

    #endregion

    #region GetStruct()

    public enum StructPerfectus
    {
        None,

        /// <summary>
        /// The identifier is good
        /// </summary>
        Identifier,

        /// <summary>
        /// Boundary between good and bad structs
        /// </summary>
        Good,

        // == MATCHED --> Searching for the most relevant struct ==

        /// <summary>
        /// The struct is in the same file
        /// </summary>
        File,
    }

    bool GetStruct(
        string structName,
        Uri relevantFile,

        [NotNullWhen(true)] out CompiledStruct? result,
        [NotNullWhen(false)] out PossibleDiagnostic? error)
        => GetStruct(
            CompiledStructs,

            structName,
            relevantFile,

            out result,
            out error);

    public static bool GetStruct(
        IEnumerable<CompiledStruct> structs,

        string structName,
        Uri relevantFile,

        [NotNullWhen(true)] out CompiledStruct? result,
        [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        CompiledStruct? result_ = default;
        PossibleDiagnostic? error_ = null;

        StructPerfectus perfectus = StructPerfectus.None;

        static StructPerfectus Max(StructPerfectus a, StructPerfectus b) => a > b ? a : b;

        bool HandleIdentifier(CompiledStruct function)
        {
            if (structName is not null &&
                function.Identifier != structName)
            { return false; }

            perfectus = Max(perfectus, StructPerfectus.Identifier);
            return true;
        }

        bool HandleFile(CompiledStruct function)
        {
            if (relevantFile is null ||
                function.Definition.File != relevantFile)
            {
                // Not in the same file
                return false;
            }

            if (perfectus >= StructPerfectus.File)
            {
                error_ = new PossibleDiagnostic($"Struct \"{structName}\" not found: multiple structs matched in the same file");
                // Debugger.Break();
            }

            perfectus = StructPerfectus.File;
            result_ = function;
            return true;
        }

        foreach (CompiledStruct function in structs)
        {
            if (!HandleIdentifier(function))
            { continue; }

            // MATCHED --> Searching for most relevant struct

            if (perfectus < StructPerfectus.Good)
            {
                result_ = function;
                perfectus = StructPerfectus.Good;
            }

            if (!HandleFile(function))
            { continue; }
        }

        if (result_ is not null && perfectus >= StructPerfectus.Good)
        {
            result = result_;
            error = error_;
            return true;
        }

        error = error_ ?? new PossibleDiagnostic($"Struct \"{structName}\" not found");
        result = null;
        return false;
    }

    #endregion

    #region GetAlias()

    public enum AliasPerfectus
    {
        None,

        /// <summary>
        /// The identifier is good
        /// </summary>
        Identifier,

        /// <summary>
        /// Boundary between good and bad structs
        /// </summary>
        Good,

        // == MATCHED --> Searching for the most relevant struct ==

        /// <summary>
        /// The struct is in the same file
        /// </summary>
        File,
    }

    bool GetAlias(
        string aliasName,
        Uri relevantFile,

        [NotNullWhen(true)] out CompiledAlias? result,
        [NotNullWhen(false)] out PossibleDiagnostic? error)
        => GetAlias(
            CompiledAliases,

            aliasName,
            relevantFile,

            out result,
            out error);

    public static bool GetAlias(
        IEnumerable<CompiledAlias> aliases,

        string aliasName,
        Uri relevantFile,

        [NotNullWhen(true)] out CompiledAlias? result,
        [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        CompiledAlias? result_ = default;
        PossibleDiagnostic? error_ = null;

        AliasPerfectus perfectus = AliasPerfectus.None;

        static AliasPerfectus Max(AliasPerfectus a, AliasPerfectus b) => a > b ? a : b;

        bool HandleIdentifier(CompiledAlias _alias)
        {
            if (aliasName is not null &&
                _alias.Identifier != aliasName)
            { return false; }

            perfectus = Max(perfectus, AliasPerfectus.Identifier);
            return true;
        }

        bool HandleFile(CompiledAlias _alias)
        {
            if (relevantFile is null ||
                _alias.Definition.File != relevantFile)
            {
                // Not in the same file
                return false;
            }

            if (perfectus >= AliasPerfectus.File)
            {
                error_ = new PossibleDiagnostic($"Alias \"{aliasName}\" not found: multiple aliases matched in the same file");
                // Debugger.Break();
            }

            perfectus = AliasPerfectus.File;
            result_ = _alias;
            return true;
        }

        foreach (CompiledAlias _alias in aliases)
        {
            if (!HandleIdentifier(_alias))
            { continue; }

            // MATCHED --> Searching for most relevant alias

            if (perfectus < AliasPerfectus.Good)
            {
                result_ = _alias;
                perfectus = AliasPerfectus.Good;
            }

            if (!HandleFile(_alias))
            { continue; }
        }

        if (result_ is not null && perfectus >= AliasPerfectus.Good)
        {
            result = result_;
            error = error_;
            return true;
        }

        error = error_ ?? new PossibleDiagnostic($"Alias \"{aliasName}\" not found");
        result = null;
        return false;
    }

    #endregion

    #region GetAlias()

    public enum EnumPerfectus
    {
        None,

        /// <summary>
        /// The identifier is good
        /// </summary>
        Identifier,

        /// <summary>
        /// Boundary between good and bad enums
        /// </summary>
        Good,

        // == MATCHED --> Searching for the most relevant enum ==

        /// <summary>
        /// The enum is in the same file
        /// </summary>
        File,
    }

    bool GetEnum(
        string enumName,
        Uri relevantFile,

        [NotNullWhen(true)] out CompiledEnum? result,
        [NotNullWhen(false)] out PossibleDiagnostic? error)
        => GetEnum(
            CompiledEnums,

            enumName,
            relevantFile,

            out result,
            out error);

    public static bool GetEnum(
        IEnumerable<CompiledEnum> enums,

        string enumName,
        Uri relevantFile,

        [NotNullWhen(true)] out CompiledEnum? result,
        [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        CompiledEnum? result_ = default;
        PossibleDiagnostic? error_ = null;

        EnumPerfectus perfectus = EnumPerfectus.None;

        static EnumPerfectus Max(EnumPerfectus a, EnumPerfectus b) => a > b ? a : b;

        bool HandleIdentifier(CompiledEnum @enum)
        {
            if (enumName is not null &&
                @enum.Identifier != enumName)
            { return false; }

            perfectus = Max(perfectus, EnumPerfectus.Identifier);
            return true;
        }

        bool HandleFile(CompiledEnum @enum)
        {
            if (relevantFile is null ||
                @enum.Definition.File != relevantFile)
            {
                // Not in the same file
                return false;
            }

            if (perfectus >= EnumPerfectus.File)
            {
                error_ = new PossibleDiagnostic($"Enum \"{enumName}\" not found: multiple enums matched in the same file");
            }

            perfectus = EnumPerfectus.File;
            result_ = @enum;
            return true;
        }

        foreach (CompiledEnum @enum in enums)
        {
            if (!HandleIdentifier(@enum))
            { continue; }

            // MATCHED --> Searching for most relevant enum

            if (perfectus < EnumPerfectus.Good)
            {
                result_ = @enum;
                perfectus = EnumPerfectus.Good;
            }

            if (!HandleFile(@enum))
            { continue; }
        }

        if (result_ is not null && perfectus >= EnumPerfectus.Good)
        {
            result = result_;
            error = error_;
            return true;
        }

        error = error_ ?? new PossibleDiagnostic($"Enum \"{enumName}\" not found");
        result = null;
        return false;
    }

    #endregion

    public enum GlobalVariablePerfectus
    {
        None,

        /// <summary>
        /// The identifier is good
        /// </summary>
        Identifier,

        /// <summary>
        /// Boundary between good and bad global variables
        /// </summary>
        Good,

        // == MATCHED --> Searching for the most relevant global variable ==

        /// <summary>
        /// The global variable is in the same file
        /// </summary>
        File,
    }

    bool GetVariable(string variableName, [NotNullWhen(true)] out CompiledVariableDefinition? compiledVariable, [NotNullWhen(false)] out PossibleDiagnostic? error) => GetVariable(variableName, Frames.Last, out compiledVariable, out error);

    static bool GetVariable(string variableName, CompiledFrame frame, [NotNullWhen(true)] out CompiledVariableDefinition? compiledVariable, [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        foreach (Scope scope in frame.Scopes)
        {
            foreach (CompiledVariableDefinition compiledVariable_ in scope.Variables)
            {
                if (compiledVariable_.Identifier == variableName)
                {
                    compiledVariable = compiledVariable_;
                    error = null;
                    return true;
                }
            }
        }

        error = new PossibleDiagnostic($"Variable \"{variableName}\" not found");
        compiledVariable = null;
        return false;
    }

    bool GetGlobalVariable(
        string variableName,
        Uri relevantFile,

        [NotNullWhen(true)] out CompiledVariableDefinition? result,
        [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        CompiledVariableDefinition? result_ = default;
        PossibleDiagnostic? error_ = null;

        GlobalVariablePerfectus perfectus = GlobalVariablePerfectus.None;

        static GlobalVariablePerfectus Max(GlobalVariablePerfectus a, GlobalVariablePerfectus b) => a > b ? a : b;

        bool HandleIdentifier(CompiledVariableDefinition variable)
        {
            if (variableName is not null &&
                variable.Identifier != variableName)
            { return false; }

            perfectus = Max(perfectus, GlobalVariablePerfectus.Identifier);
            return true;
        }

        bool HandleFile(CompiledVariableDefinition variable)
        {
            if (relevantFile is null ||
                variable.Location.File != relevantFile)
            {
                // Not in the same file
                return false;
            }

            if (perfectus >= GlobalVariablePerfectus.File)
            {
                error_ = new PossibleDiagnostic($"Global variable \"{variableName}\" not found: multiple variables matched in the same file");
                // Debugger.Break();
            }

            perfectus = GlobalVariablePerfectus.File;
            result_ = variable;
            return true;
        }

        foreach (CompiledVariableDefinition variable in CompiledGlobalVariables)
        {
            if (!HandleIdentifier(variable))
            { continue; }

            // MATCHED --> Searching for most relevant global variable

            if (perfectus < GlobalVariablePerfectus.Good)
            {
                result_ = variable;
                perfectus = GlobalVariablePerfectus.Good;
            }

            if (!HandleFile(variable))
            { continue; }
        }

        if (result_ is not null && perfectus >= GlobalVariablePerfectus.Good)
        {
            result = result_;
            error = error_;
            return true;
        }

        error = error_ ?? new PossibleDiagnostic($"Global variable \"{variableName}\" not found");
        result = null;
        return false;
    }

    bool GetParameter(string parameterName, [NotNullWhen(true)] out CompiledParameter? parameter, [NotNullWhen(false)] out PossibleDiagnostic? error) => GetParameter(parameterName, Frames.Last, out parameter, out error);

    static bool GetParameter(string parameterName, CompiledFrame frame, [NotNullWhen(true)] out CompiledParameter? parameter, [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        foreach (CompiledParameter compiledParameter in frame.CompiledParameters)
        {
            if (compiledParameter.Identifier == parameterName)
            {
                parameter = compiledParameter;
                error = null;
                return true;
            }
        }

        error = new PossibleDiagnostic($"Parameter \"{parameterName}\" not found");
        parameter = null;
        return false;
    }

    bool GetInstructionLabel(string identifier, [NotNullWhen(true)] out CompiledLabelDeclaration? instructionLabel, [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        foreach (CompiledLabelDeclaration compiledInstructionLabel in Frames[^1].InstructionLabels)
        {
            if (compiledInstructionLabel.Identifier != identifier) continue;
            instructionLabel = compiledInstructionLabel;
            error = null;
            return true;
        }

        foreach (CompiledLabelDeclaration compiledInstructionLabel in Frames[0].InstructionLabels)
        {
            if (compiledInstructionLabel.Identifier != identifier) continue;
            instructionLabel = compiledInstructionLabel;
            error = null;
            return true;
        }

        error = new PossibleDiagnostic($"Instruction label \"{identifier}\" not found");
        instructionLabel = null;
        return false;
    }

    public static bool IsLValue(CompiledExpression expression, [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        error = null;
        switch (expression)
        {
            case CompiledVariableAccess:
            case CompiledParameterAccess:
            case CompiledExpressionVariableAccess:
                return true;
            case CompiledFieldAccess fieldAccess:
            {
                if (fieldAccess.Object.Type.Is<IReferenceType>()) return true;
                if (!IsLValue(fieldAccess.Object, out error)) return false;
                return true;
            }
            case CompiledElementAccess elementAccess:
            {
                if (elementAccess.Base.Type.Is<IReferenceType>()) return true;
                if (!IsLValue(elementAccess.Base, out error)) return false;
                return true;
            }
            default:
                error = new PossibleDiagnostic($"Expression doesn't have an address", expression);
                return false;
        }
    }

    public static bool CanCastImplicitly(GeneralType source, GeneralType destination, [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        error = null;

        if (destination.SameAs(source))
        { return true; }

        if (destination.SameAs(BasicType.Any))
        { return true; }

        {
            if (destination.Is(out PointerType? dstPointer) &&
                source.Is(out PointerType? srcPointer))
            {
                if (dstPointer.To.SameAs(BasicType.Any))
                { return true; }

                if (dstPointer.To.Is(out ArrayType? dstArray) &&
                    srcPointer.To.Is(out ArrayType? srcArray))
                {
                    if (dstArray.Length.HasValue &&
                        srcArray.Length.HasValue &&
                        dstArray.Length.Value != srcArray.Length.Value)
                    {
                        error = new($"Can't cast an array pointer with length of {dstArray.Length.Value} to an array pointer with length of {srcArray.Length.Value}");
                        return false;
                    }

                    if (dstArray.Length is null)
                    { return true; }
                }
            }
        }

        {
            if (destination.Is(out PointerType? dstPointer) &&
                source.Is(out FunctionType? srcFunction)
                && dstPointer.To.SameAs(BasicType.Any)
                && srcFunction.HasClosure)
            { return true; }
        }

        {
            if (destination.Is(out ReferenceType? destReferenceType)
                && source.Is(out PointerType? srcPointerType)
                && destReferenceType.To.Equals(srcPointerType.To))
            {
                return true;
            }
        }

        error = new($"Can't cast \"{source}\" to \"{destination}\" implicitly");
        return false;
    }

    public static bool CanCastImplicitly(GeneralType source, GeneralType destination, Expression? value, [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        if (CanCastImplicitly(source, destination, out error)) return true;

        if (value is StringLiteralExpression stringLiteral)
        {
            if (destination.Is(out ArrayType? destArrayType) &&
                destArrayType.Of.SameAs(BasicType.U16))
            {
                string literalValue = stringLiteral.Value;
                if (destArrayType.Length is null)
                {
                    error = new($"Can't cast literal value \"{literalValue}\" (length of {literalValue.Length}) to stack array \"{destination}\" (without length)", stringLiteral);
                    return false;
                }

                if (!destArrayType.Length.HasValue)
                {
                    error = new($"Can't cast literal value \"{literalValue}\" (length of {literalValue.Length}) to stack array \"{destination}\" (length of <runtime value>)", stringLiteral);
                    return false;
                }

                if (literalValue.Length != destArrayType.Length.Value)
                {
                    error = new($"Can't cast literal value \"{literalValue}\" (length of {literalValue.Length}) to stack array \"{destination}\" (length of \"{destArrayType.Length.ToString() ?? "null"}\")", stringLiteral);
                    return false;
                }

                return true;
            }

            if (destination.Is(out PointerType? pointerType) &&
                pointerType.To.Is(out ArrayType? arrayType) &&
                arrayType.Of.SameAs(BasicType.U16))
            {
                if (arrayType.Length is not null)
                {
                    if (!arrayType.Length.HasValue)
                    {
                        error = new($"Can't cast literal value \"{stringLiteral.Value}\" (length of {stringLiteral.Value.Length}) to array \"{destination}\" (length of <runtime value>)", stringLiteral);
                        return false;
                    }

                    if (stringLiteral.Value.Length != arrayType.Length.Value)
                    {
                        error = new($"Can't cast literal value \"{stringLiteral.Value}\" (length of {stringLiteral.Value.Length}) to array \"{destination}\" (length of \"{arrayType.Length.ToString() ?? "null"}\")", stringLiteral);
                        return false;
                    }
                }

                return true;
            }
        }

        error = new($"Can't cast \"{source}\" to \"{destination}\" implicitly", value);
        return false;
    }

    public bool CanCastImplicitly(CompiledExpression value, GeneralType destination, out CompiledExpression assignedValue, [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        assignedValue = value;

        if (CanCastImplicitly(value.Type, destination, out error)) return true;

        if (destination.Is(out ReferenceType? returnRefType))
        {
            if (value.Type.SameAs(returnRefType.To))
            {
                if (!IsLValue(value, out PossibleDiagnostic? lvalueError))
                {
                    error = new PossibleDiagnostic($"Can't cast `{value.Type}` to `{returnRefType.To}`", value, lvalueError);
                    return false;
                }

                assignedValue = new CompiledGetReference()
                {
                    Of = value,
                    Location = value.Location,
                    SaveValue = true,
                    Type = new ReferenceType(value.Type),
                };
                return true;
            }
            else
            {
                error = new PossibleDiagnostic($"Can't cast `{value.Type}` to `{returnRefType.To}`", value);
            }
        }

        if (value.Type.Is(out ReferenceType? valueRefType))
        {
            if (valueRefType.To.SameAs(destination))
            {
                assignedValue = new CompiledDereference()
                {
                    Address = value,
                    Location = value.Location,
                    SaveValue = true,
                    Type = valueRefType.To,
                };
                return true;
            }
            else
            {
                error = new PossibleDiagnostic($"Can't cast `{valueRefType.To}` to `{value.Type}`", value);
            }
        }

        if (value is CompiledString stringInstance)
        {
            if (destination.Is(out PointerType? pointerType) &&
                pointerType.To.Is(out ArrayType? arrayType) &&
                arrayType.Of.SameAs(BasicType.U16))
            {
                if (arrayType.Length is not null)
                {
                    if (!arrayType.Length.HasValue)
                    {
                        error = new($"Can't cast literal value \"{stringInstance.Value}\" (length of {stringInstance.Value.Length}) to array \"{destination}\" (with a non-constant length)", stringInstance);
                        return false;
                    }

                    if (stringInstance.Value.Length != arrayType.Length.Value)
                    {
                        error = new($"Can't cast literal value \"{stringInstance.Value}\" (length of {stringInstance.Value.Length}) to array \"{destination}\" (length of {arrayType.Length.Value})", stringInstance);
                        return false;
                    }
                }

                return true;
            }
        }

        if (value is CompiledStackString stackStringInstance)
        {
            if (destination.Is(out ArrayType? destArrayType) && destArrayType.Of.SameAs(BasicType.U16))
            {
                if (destArrayType.Length is null)
                {
                    error = new($"Can't cast literal value \"{stackStringInstance.Value}\" (length of {stackStringInstance.Value.Length}) to stack array \"{destination}\" (without length)", stackStringInstance);
                    return false;
                }

                if (!destArrayType.Length.HasValue)
                {
                    error = new($"Can't cast literal value \"{stackStringInstance.Value}\" (length of {stackStringInstance.Value.Length}) to stack array \"{destination}\" (with a non-constant length)", stackStringInstance);
                    return false;
                }

                if (stackStringInstance.Value.Length != destArrayType.Length.Value)
                {
                    error = new($"Can't cast literal value \"{stackStringInstance.Value}\" (length of {stackStringInstance.Value.Length}) to stack array \"{destination}\" (length of {destArrayType.Length.Value})", stackStringInstance);
                    return false;
                }

                assignedValue = new CompiledStackString()
                {
                    Value = stackStringInstance.Value,
                    IsNullTerminated = false,
                    IsUTF8 = false,
                    Type = destination,
                    Location = stackStringInstance.Location,
                    SaveValue = stackStringInstance.SaveValue,
                };
                return true;
            }

            if (destination.Is(out ArrayType? destArrayType2) && destArrayType2.Of.SameAs(BasicType.U8))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(stackStringInstance.Value);

                if (destArrayType2.Length is null)
                {
                    error = new($"Can't cast literal value \"{stackStringInstance.Value}\" (length of {bytes.Length}) to stack array \"{destination}\" (without length)", stackStringInstance);
                    return false;
                }

                if (!destArrayType2.Length.HasValue)
                {
                    error = new($"Can't cast literal value \"{stackStringInstance.Value}\" (length of {bytes.Length}) to stack array \"{destination}\" (with a non-constant length)", stackStringInstance);
                    return false;
                }

                if (bytes.Length != destArrayType2.Length.Value)
                {
                    error = new($"Can't cast literal value \"{stackStringInstance.Value}\" (length of {bytes.Length}) to stack array \"{destination}\" (length of {destArrayType2.Length.Value})", stackStringInstance);
                    return false;
                }

                assignedValue = new CompiledStackString()
                {
                    Value = stackStringInstance.Value,
                    IsNullTerminated = false,
                    IsUTF8 = true,
                    Type = destination,
                    Location = stackStringInstance.Location,
                    SaveValue = stackStringInstance.SaveValue,
                };
                return true;
            }
        }

        {
            if (value.Type.Is(out FunctionType? sourceFunctionType)
                && destination.Is(out FunctionType? targetFunctionType))
            {
                if (sourceFunctionType.HasClosure && targetFunctionType.HasClosure) return true;
                if (!sourceFunctionType.HasClosure && !targetFunctionType.HasClosure) return true;
                if (sourceFunctionType.HasClosure && !targetFunctionType.HasClosure)
                {
                    error = new($"Can't convert `{sourceFunctionType}` to `{targetFunctionType}` because it would lose the closure", value);
                    return false;
                }
                if (!sourceFunctionType.HasClosure && targetFunctionType.HasClosure)
                {
                    if (!CompileAllocation(PointerSize, value.Location, out CompiledExpression? allocator))
                    {
                        return false;
                    }
                    assignedValue = new CompiledCast()
                    {
                        TypeExpression = CompiledTypeExpression.CreateAnonymous(targetFunctionType, value.Location),
                        Type = targetFunctionType,
                        Value = value,
                        Allocator = allocator,
                        Location = value.Location,
                        SaveValue = value.SaveValue,
                    };
                    return true;
                }
            }
        }

        if (value is CompiledConstantValue constantValue
            && destination.Is(out BuiltinType? builtinDstType)
            && constantValue.Value.TryCast(builtinDstType.RuntimeType, out CompiledValue assignedConstValue))
        {
            assignedValue = new CompiledConstantValue()
            {
                Value = assignedConstValue,
                Type = destination,
                Location = value.Location,
                SaveValue = value.SaveValue,
            };
            return true;
        }

        error = new($"Can't cast `{value.Type.FinalValue}` to `{destination.FinalValue}` implicitly", value);
        return false;
    }

    public static BitWidth MaxBitWidth(BitWidth a, BitWidth b) => a > b ? a : b;

    #region Initial Value

    static CompiledValue GetInitialValue(BasicType type) => type switch
    {
        BasicType.U8 => new CompiledValue(default(byte)),
        BasicType.I8 => new CompiledValue(default(sbyte)),
        BasicType.U16 => new CompiledValue(default(ushort)),
        BasicType.I16 => new CompiledValue(default(short)),
        BasicType.U32 => new CompiledValue(default(uint)),
        BasicType.I32 => new CompiledValue(default(int)),
        BasicType.F32 => new CompiledValue(default(float)),
        _ => throw new NotImplementedException($"Type \"{type}\" can't have value"),
    };

    static bool GetInitialValue(GeneralType type, out CompiledValue value)
    {
        switch (type.FinalValue)
        {
            case GenericType:
            case StructType:
            case FunctionType:
            case ArrayType:
                value = default;
                return false;
            case BuiltinType builtinType:
                value = GetInitialValue(builtinType.Type);
                return true;
            case PointerType:
                value = new CompiledValue(0);
                return true;
            default: throw new NotImplementedException();
        }
    }

    static CompiledValue GetInitialValue(NumericType type, BitWidth bitWidth) => (type, bitWidth) switch
    {
        (NumericType.Float, BitWidth._32) => new CompiledValue(default(float)),
        (NumericType.SignedInteger, BitWidth._8) => new CompiledValue(default(sbyte)),
        (NumericType.SignedInteger, BitWidth._16) => new CompiledValue(default(short)),
        (NumericType.SignedInteger, BitWidth._32) => new CompiledValue(default(int)),
        (NumericType.SignedInteger, BitWidth._64) => new CompiledValue(default(long)),
        (NumericType.UnsignedInteger, BitWidth._8) => new CompiledValue(default(byte)),
        (NumericType.UnsignedInteger, BitWidth._16) => new CompiledValue(default(ushort)),
        (NumericType.UnsignedInteger, BitWidth._32) => new CompiledValue(default(uint)),
        (NumericType.UnsignedInteger, BitWidth._64) => new CompiledValue(default(ulong)),
        _ => throw new NotImplementedException(),
    };

    #endregion

    #region Find Type

    bool FindType(Token name, Uri relevantFile, [NotNullWhen(true)] out CompiledTypeExpression? result, [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        if (TypeKeywords.BasicTypes.TryGetValue(name.Content, out BasicType builtinType))
        {
            result = new CompiledBuiltinTypeExpression(builtinType, new Location(name.Position, relevantFile));
            error = null;
            return true;
        }

        if (Frames.Last.TypeArguments.TryGetValue(name.Content, out GeneralType? typeArgument))
        {
            result = CompiledTypeExpression.CreateAnonymous(typeArgument, new Location(name.Position, relevantFile));
            error = null;
            return true;
        }

        {
            int i = Frames.Last.TypeParameters.IndexOf(name.Content);
            if (i != -1)
            {
                result = new CompiledGenericTypeExpression(Frames.Last.TypeParameters[i], relevantFile, new Location(name.Position, relevantFile));
                error = null;
                return true;
            }
        }

        for (int i = 0; i < GenericParameters.Count; i++)
        {
            for (int j = 0; j < GenericParameters[i].Length; j++)
            {
                if (GenericParameters[i][j].Content == name.Content)
                {
                    GenericParameters[i][j].AnalyzedType = TokenAnalyzedType.TypeParameter;
                    result = new CompiledGenericTypeExpression(GenericParameters[i][j], relevantFile, new Location(name.Position, relevantFile));
                    error = null;
                    return true;
                }
            }
        }

        if (GetAlias(name.Content, relevantFile, out CompiledAlias? alias, out PossibleDiagnostic? aliasError))
        {
            name.AnalyzedType = alias.Value.FinalValue switch
            {
                CompiledBuiltinTypeExpression => TokenAnalyzedType.BuiltinType,
                CompiledStructTypeExpression => TokenAnalyzedType.Struct,
                CompiledGenericTypeExpression => TokenAnalyzedType.TypeParameter,
                CompiledEnumTypeExpression => TokenAnalyzedType.Enum,
                _ => TokenAnalyzedType.Type,
            };
            alias.AddReference(new TypeInstanceSimple(name, relevantFile));

            result = new CompiledAliasTypeExpression(alias, new Location(name.Position, relevantFile));
            error = null;
            return true;
        }

        if (GetEnum(name.Content, relevantFile, out CompiledEnum? @enum, out PossibleDiagnostic? enumError))
        {
            name.AnalyzedType = @enum.Type.FinalValue switch
            {
                BuiltinType => TokenAnalyzedType.BuiltinType,
                StructType => TokenAnalyzedType.Struct,
                GenericType => TokenAnalyzedType.TypeParameter,
                AliasType => TokenAnalyzedType.TypeAlias,
                EnumType => TokenAnalyzedType.Enum,
                _ => TokenAnalyzedType.Type,
            };
            @enum.AddReference(new TypeInstanceSimple(name, relevantFile));

            result = new CompiledEnumTypeExpression(@enum, new Location(name.Position, relevantFile));
            error = null;
            return true;
        }

        if (GetStruct(name.Content, relevantFile, out CompiledStruct? @struct, out PossibleDiagnostic? structError))
        {
            name.AnalyzedType = TokenAnalyzedType.Struct;
            @struct.AddReference(new TypeInstanceSimple(name, relevantFile));

            result = new CompiledStructTypeExpression(@struct, relevantFile, new Location(name.Position, relevantFile));
            error = null;
            return true;
        }

        result = null;
        error = new PossibleDiagnostic($"Can't find type `{name.Content}`", ImmutableArray.Create(
            aliasError,
            structError,
            enumError
        ));
        return false;
    }

    bool GetLiteralType(LiteralType literal, [NotNullWhen(true)] out GeneralType? type, [NotNullWhen(false)] out PossibleDiagnostic? error) => GetUsedBy(literal switch
    {
        LiteralType.Integer => InternalTypes.Integer,
        LiteralType.Float => InternalTypes.Float,
        LiteralType.String => InternalTypes.String,
        LiteralType.Char => InternalTypes.Char,
        _ => throw new UnreachableException(),
    }, out type, out error);

    bool GetUsedBy(string by, [NotNullWhen(true)] out GeneralType? type, [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        string? ParseAttribute(AttributeUsage attribute)
        {
            if (!attribute.TryGetValue(out string? literalTypeName))
            {
                Diagnostics.Add(DiagnosticAt.Error($"Attribute \"{attribute.Identifier}\" needs one string argument", attribute));
                return default;
            }
            return literalTypeName;
        }

        type = null;

        foreach (CompiledAlias alias in CompiledAliases)
        {
            if (alias.Definition.Attributes.TryGetAttribute(AttributeConstants.InternalType, out AttributeUsage? attribute))
            {
                if (ParseAttribute(attribute) == by)
                {
                    if (type is not null)
                    {
                        error = new PossibleDiagnostic($"Multiple type definitions marked as an internal type `{by}`", attribute);
                        return false;
                    }

                    if (!CompileType(alias.Value, out GeneralType? aliasValue, out error))
                    {
                        return false;
                    }

                    type = new AliasType(aliasValue, alias);
                }
            }
        }

        foreach (CompiledEnum @enum in CompiledEnums)
        {
            if (@enum.Definition.Attributes.TryGetAttribute(AttributeConstants.InternalType, out AttributeUsage? attribute))
            {
                if (ParseAttribute(attribute) == by)
                {
                    if (type is not null)
                    {
                        error = new PossibleDiagnostic($"Multiple type definitions marked as an internal type `{by}`", attribute);
                        return false;
                    }
                    type = new EnumType(@enum);
                }
            }
        }

        foreach (CompiledStruct @struct in CompiledStructs)
        {
            if (@struct.Definition.Attributes.TryGetAttribute(AttributeConstants.InternalType, out AttributeUsage? attribute))
            {
                if (ParseAttribute(attribute) == by)
                {
                    if (type is not null)
                    {
                        error = new PossibleDiagnostic($"Multiple type definitions marked as an internal type `{by}`", attribute);
                        return false;
                    }
                    type = new StructType(@struct, @struct.Definition.File);
                }
            }
        }

        if (type is null)
        {
            error = new PossibleDiagnostic($"No type definition found with attribute `{AttributeConstants.InternalType}`", false);
            return false;
        }
        else
        {
            error = null;
            return true;
        }
    }

    void SetPredictedValue(Expression expression, CompiledValue value)
    {
        if (!Frames.Last.IsTemplateInstance) expression.PredictedValue = value;
    }
    TType SetStatementType<TType>(Expression expression, TType type)
        where TType : GeneralType
    {
        if (!Frames.Last.IsTemplateInstance) expression.CompiledType = type;
        return type;
    }
    TType SetStatementType<TType>(TypeInstance expression, TType type)
        where TType : GeneralType
    {
        switch (expression, type)
        {
            case (TypeInstanceFunction v, FunctionType w): SetStatementType(v, w); break;
            case (TypeInstancePointer v, PointerType w): SetStatementType(v, w); break;
            case (TypeInstanceReference v, ReferenceType w): SetStatementType(v, w); break;
            case (TypeInstanceStackArray v, ArrayType w): SetStatementType(v, w); break;
            case (TypeInstanceSimple v, _): SetStatementType(v, type); break;
            default:
                Diagnostics.Add(DiagnosticAt.Warning($"({expression.GetType().Name}, {type.GetType().Name})", expression));
                break; // throw new UnreachableException($"({expression.GetType().Name}, {type.GetType().Name})");
        }
        return type;
    }
    FunctionType SetStatementType(TypeInstanceFunction expression, FunctionType type)
    {
        if (Frames.Last.IsTemplateInstance) return type;

        expression.CompiledType = type;

        return type;
    }
    PointerType SetStatementType(TypeInstancePointer expression, PointerType type)
    {
        if (Frames.Last.IsTemplateInstance) return type;

        expression.CompiledType = type;

        SetStatementType(expression.To, type.To);

        return type;
    }
    ReferenceType SetStatementType(TypeInstanceReference expression, ReferenceType type)
    {
        if (Frames.Last.IsTemplateInstance) return type;

        expression.CompiledType = type;

        SetStatementType(expression.To, type.To);

        return type;
    }
    ArrayType SetStatementType(TypeInstanceStackArray expression, ArrayType type)
    {
        if (Frames.Last.IsTemplateInstance) return type;

        expression.CompiledType = type;

        SetStatementType(expression.StackArrayOf, type.Of);

        return type;
    }
    TType SetStatementType<TType>(TypeInstanceSimple expression, TType type)
        where TType : GeneralType
    {
        if (Frames.Last.IsTemplateInstance) return type;

        expression.CompiledType = type;

        return type;
    }
    void TrySetStatementReference<TRef>(Statement statement, TRef? reference) where TRef : class
    {
        if (statement is IReferenceableTo<TRef> v1) SetStatementReference(v1, reference);
        else if (statement is IReferenceableTo v2) SetStatementReference(v2, reference);
    }
    void SetStatementReference<TRef>(IReferenceableTo<TRef> statement, TRef? reference) where TRef : class
    {
        if (!Frames.Last.IsTemplateInstance) statement.Reference = reference;
    }
    void SetStatementReference(IReferenceableTo statement, object? reference)
    {
        if (!Frames.Last.IsTemplateInstance) statement.Reference = reference;
    }

    #endregion

    #region Inlining

    public class InlineContext
    {
        public required ImmutableDictionary<string, CompiledArgument> Arguments { get; init; }
        public List<CompiledArgument> InlinedArguments { get; } = new();
        public Dictionary<CompiledVariableDefinition, CompiledVariableDefinition> VariableReplacements { get; } = new();
    }

    static bool Inline(IEnumerable<CompiledArgument> statements, InlineContext context, [NotNullWhen(true)] out ImmutableArray<CompiledArgument> inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = ImmutableArray<CompiledArgument>.Empty;
        ImmutableArray<CompiledArgument>.Builder res = ImmutableArray.CreateBuilder<CompiledArgument>();

        foreach (CompiledArgument statement in statements)
        {
            if (!Inline(statement.Value, context, out CompiledExpression? v, out error)) return false;
            res.Add(new CompiledArgument()
            {
                Value = v,
                Cleanup = new CompiledCleanup()
                {
                    Location = statement.Cleanup.Location,
                    TrashType = statement.Cleanup.TrashType,
                },
                Location = statement.Location,
                SaveValue = statement.SaveValue,
                Type = statement.Type,
            });
        }

        inlined = res.ToImmutable();
        error = null;
        return true;
    }

    public static bool InlineFunction(CompiledBlock _block, InlineContext context, [NotNullWhen(true)] out CompiledStatement? inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        if (_block.Statements.Length == 1)
        {
            if (!Inline(_block.Statements[0], context, out inlined, out error))
            { return false; }
        }
        else
        {
            if (!Inline(_block, context, out inlined, out error))
            { return false; }
        }

        if (inlined is CompiledReturn compiledReturn &&
            compiledReturn.Value is not null)
        { inlined = compiledReturn.Value; }

        return true;
    }

    static bool Inline(CompiledTypeExpression statement, InlineContext context, out CompiledTypeExpression inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = statement;

        switch (statement)
        {
            case CompiledAliasTypeExpression v:
                inlined = v;
                break;
            case CompiledEnumTypeExpression v:
                inlined = v;
                break;
            case CompiledArrayTypeExpression v:
                if (!Inline(v.Of, context, out CompiledTypeExpression? ofInlined, out error)) return false;
                if (!Inline(v.Length, context, out CompiledExpression? lengthInlined, out error)) return false;
                inlined = new CompiledArrayTypeExpression(ofInlined, lengthInlined, v.Location);
                break;
            case CompiledBuiltinTypeExpression v:
                inlined = v;
                break;
            case CompiledFunctionTypeExpression v:
                CompiledTypeExpression[] parameters = new CompiledTypeExpression[v.Parameters.Length];
                if (!Inline(v.ReturnType, context, out CompiledTypeExpression? returnTypeInlined, out error)) return false;
                for (int i = 0; i < v.Parameters.Length; i++)
                {
                    if (!Inline(v.Parameters[i], context, out parameters[i], out error)) return false;
                }
                inlined = new CompiledFunctionTypeExpression(returnTypeInlined, parameters.AsImmutableUnsafe(), v.HasClosure, v.Location);
                break;
            case CompiledGenericTypeExpression v:
                inlined = v;
                break;
            case CompiledPointerTypeExpression v:
                if (!Inline(v.To, context, out CompiledTypeExpression? toInlined, out error)) return false;
                inlined = new CompiledPointerTypeExpression(toInlined, v.Location);
                break;
            case CompiledReferenceTypeExpression v:
                if (!Inline(v.To, context, out CompiledTypeExpression? toInlined2, out error)) return false;
                inlined = new CompiledReferenceTypeExpression(toInlined2, v.Location);
                break;
            case CompiledStructTypeExpression v:
                Dictionary<string, CompiledTypeExpression> typeArguments = new(v.TypeArguments.Count);
                foreach (KeyValuePair<string, CompiledTypeExpression> i in v.TypeArguments)
                {
                    if (!Inline(i.Value, context, out CompiledTypeExpression? iInlined, out error)) return false;
                    typeArguments[i.Key] = iInlined;
                }
                inlined = new CompiledStructTypeExpression(v.Struct, v.File, typeArguments.ToImmutableDictionary(), v.Location);
                break;
            default: throw new UnreachableException();
        }

        if (inlined.Equals(statement)) inlined = statement;
        error = null;
        return true;
    }
    static bool Inline(CompiledSizeof statement, InlineContext context, out CompiledExpression inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = statement;
        if (!Inline(statement.Of, context, out CompiledTypeExpression? inlinedOf, out error)) return false;
        inlined = new CompiledSizeof()
        {
            Of = inlinedOf,
            Location = statement.Location,
            SaveValue = statement.SaveValue,
            Type = statement.Type,
        };
        return true;
    }
    static bool Inline(CompiledBinaryOperatorCall statement, InlineContext context, out CompiledExpression inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = statement;
        if (!Inline(statement.Left, context, out CompiledExpression? inlinedLeft, out error)) return false;
        if (!Inline(statement.Right, context, out CompiledExpression? inlinedRight, out error)) return false;

        inlined = new CompiledBinaryOperatorCall()
        {
            Left = inlinedLeft,
            Right = inlinedRight,
            Operator = statement.Operator,
            Type = statement.Type,
            Location = statement.Location,
            SaveValue = statement.SaveValue,
        };
        return true;
    }
    static bool Inline(CompiledUnaryOperatorCall statement, InlineContext context, out CompiledExpression inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = statement;
        if (!Inline(statement.Expression, context, out CompiledExpression? inlinedLeft, out error)) return false;

        inlined = new CompiledUnaryOperatorCall()
        {
            Expression = inlinedLeft,
            Operator = statement.Operator,
            Type = statement.Type,
            Location = statement.Location,
            SaveValue = statement.SaveValue,
        };
        return true;
    }
    static bool Inline(CompiledConstantValue statement, InlineContext context, out CompiledExpression inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = statement;
        error = null;
        return true;
    }
    static bool Inline(CompiledRegisterAccess statement, InlineContext context, out CompiledExpression inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = statement;
        error = null;
        return true;
    }
    static bool Inline(CompiledExpressionVariableAccess statement, InlineContext context, out CompiledExpression inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = statement;
        error = null;
        return true;
    }
    static bool Inline(CompiledVariableAccess statement, InlineContext context, out CompiledExpression inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = statement;
        if (!context.VariableReplacements.TryGetValue(statement.Variable, out CompiledVariableDefinition? replacedVariable))
        {
            if (statement.Variable.IsGlobal)
            {
                replacedVariable = statement.Variable;
            }
            else
            {
                error = DiagnosticAt.Error($"Variable `{statement.Variable.Identifier}` not found to inline", statement);
                return false;
            }
        }

        inlined = new CompiledVariableAccess()
        {
            Variable = replacedVariable,
            Type = statement.Type,
            Location = statement.Location,
            SaveValue = statement.SaveValue,
        };
        error = null;
        return true;
    }
    static bool Inline(CompiledParameterAccess statement, InlineContext context, out CompiledExpression inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = statement;

        if (context.Arguments.TryGetValue(statement.Parameter.Identifier, out CompiledArgument? inlinedArgument))
        {
            if (inlinedArgument.Cleanup.Deallocator is not null ||
                inlinedArgument.Cleanup.Destructor is not null)
            {
                error = DiagnosticAt.Error($"Argument cleanup is not empty", statement, false);
                return false;
            }

            context.InlinedArguments.Add(inlinedArgument);
            inlined = inlinedArgument.Value;
            error = null;
            return true;
        }

        error = DiagnosticAt.Error($"Variable `{statement.Parameter.Identifier}` not found to inline", statement);
        return false;
    }
    static bool Inline(CompiledFunctionReference statement, InlineContext context, out CompiledExpression inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = statement;
        error = null;
        return true;
    }
    static bool Inline(CompiledLabelReference statement, InlineContext context, out CompiledExpression inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = statement;
        error = null;
        return true;
    }
    static bool Inline(CompiledFieldAccess statement, InlineContext context, out CompiledExpression inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = statement;
        if (!Inline(statement.Object, context, out CompiledExpression? inlinedObject, out error)) return false;

        while (inlinedObject is CompiledGetReference addressGetter)
        { inlinedObject = addressGetter.Of; }

        inlined = new CompiledFieldAccess()
        {
            Object = inlinedObject,
            Field = statement.Field,
            Type = statement.Type,
            Location = statement.Location,
            SaveValue = statement.SaveValue,
        };
        return true;
    }
    static bool Inline(CompiledElementAccess statement, InlineContext context, out CompiledExpression inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = statement;
        if (!Inline(statement.Base, context, out CompiledExpression? inlinedBase, out error)) return false;
        if (!Inline(statement.Index, context, out CompiledExpression? inlinedIndex, out error)) return false;

        inlined = new CompiledElementAccess()
        {
            Base = inlinedBase,
            Index = inlinedIndex,
            Type = statement.Type,
            Location = statement.Location,
            SaveValue = statement.SaveValue,
        };
        return true;
    }
    static bool Inline(CompiledGetReference statement, InlineContext context, out CompiledExpression inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = statement;
        if (!Inline(statement.Of, context, out CompiledExpression? inlinedOf, out error)) return false;

        inlined = new CompiledGetReference()
        {
            Of = inlinedOf,
            Type = statement.Type,
            Location = statement.Location,
            SaveValue = statement.SaveValue,
        };
        return true;
    }
    static bool Inline(CompiledDereference statement, InlineContext context, out CompiledExpression inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = statement;
        if (!Inline(statement.Address, context, out CompiledExpression? inlinedTo, out error)) return false;

        inlined = new CompiledDereference()
        {
            Address = inlinedTo,
            Type = statement.Type,
            Location = statement.Location,
            SaveValue = statement.SaveValue,
        };
        return true;
    }
    static bool Inline(CompiledStackAllocation statement, InlineContext context, out CompiledExpression inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = statement;
        if (!Inline(statement.TypeExpression, context, out CompiledTypeExpression? inlinedType, out error)) return false;
        inlined = new CompiledStackAllocation()
        {
            TypeExpression = inlinedType,
            Type = statement.Type,
            Location = statement.Location,
            SaveValue = statement.SaveValue,
        };
        return true;
    }
    static bool Inline(CompiledConstructorCall statement, InlineContext context, out CompiledExpression inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = statement;
        if (!Inline(statement.Object, context, out CompiledExpression? inlinedObject, out error)) return false;
        if (!Inline(statement.Arguments, context, out ImmutableArray<CompiledArgument> inlinedArguments, out error)) return false;

        inlined = new CompiledConstructorCall()
        {
            Object = inlinedObject,
            Arguments = inlinedArguments,
            Function = statement.Function,
            Type = statement.Type,
            Location = statement.Location,
            SaveValue = statement.SaveValue,
        };
        return true;
    }
    static bool Inline(CompiledCast statement, InlineContext context, out CompiledExpression inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = statement;

        if (!Inline(statement.Value, context, out CompiledExpression? inlinedValue, out error)) return false;
        if (!Inline(statement.TypeExpression, context, out CompiledTypeExpression? inlinedType, out error)) return false;

        inlined = new CompiledReinterpretation()
        {
            Value = inlinedValue,
            Location = statement.Location,
            SaveValue = statement.SaveValue,
            Type = statement.Type,
            TypeExpression = inlinedType,
        };
        return true;
    }
    static bool Inline(CompiledReinterpretation statement, InlineContext context, out CompiledExpression inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = statement;

        if (!Inline(statement.Value, context, out CompiledExpression? inlinedValue, out error)) return false;
        if (!Inline(statement.TypeExpression, context, out CompiledTypeExpression? inlinedType, out error)) return false;

        inlined = new CompiledReinterpretation()
        {
            Value = inlinedValue,
            TypeExpression = inlinedType,
            Location = statement.Location,
            SaveValue = statement.SaveValue,
            Type = statement.Type,
        };
        return true;
    }
    static bool Inline(CompiledRuntimeCall statement, InlineContext context, out CompiledExpression inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = statement;

        if (!Inline(statement.Arguments, context, out ImmutableArray<CompiledArgument> inlinedArguments, out error)) return false;
        if (!Inline(statement.Function, context, out CompiledExpression? inlinedFunction, out error)) return false;

        inlined = new CompiledRuntimeCall()
        {
            Function = inlinedFunction,
            Arguments = inlinedArguments,
            Location = statement.Location,
            SaveValue = statement.SaveValue,
            Type = statement.Type,
        };
        return true;
    }
    static bool Inline(CompiledFunctionCall statement, InlineContext context, out CompiledExpression inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = statement;

        if (!Inline(statement.Arguments, context, out ImmutableArray<CompiledArgument> inlinedArguments, out error)) return false;

        inlined = new CompiledFunctionCall()
        {
            Function = statement.Function,
            Arguments = inlinedArguments,
            Location = statement.Location,
            SaveValue = statement.SaveValue,
            Type = statement.Type,
        };
        return true;
    }
    static bool Inline(CompiledExternalFunctionCall statement, InlineContext context, out CompiledExpression inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = statement;

        if (!Inline(statement.Arguments, context, out ImmutableArray<CompiledArgument> inlinedArguments, out error)) return false;

        inlined = new CompiledExternalFunctionCall()
        {
            Declaration = statement.Declaration,
            Function = statement.Function,
            Arguments = inlinedArguments,
            Location = statement.Location,
            SaveValue = statement.SaveValue,
            Type = statement.Type,
        };
        return true;
    }
    static bool Inline(CompiledDummyExpression statement, InlineContext context, out CompiledExpression inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = statement;
        if (!Inline(statement.Statement, context, out CompiledStatement? inlinedStatement, out error)) return false;

        inlined = new CompiledDummyExpression()
        {
            Statement = inlinedStatement,
            Type = statement.Type,
            Location = statement.Location,
            SaveValue = statement.SaveValue,
        };
        return true;
    }
    static bool Inline(CompiledString statement, InlineContext context, out CompiledExpression inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = statement;
        error = null;
        return true;
    }
    static bool Inline(CompiledStackString statement, InlineContext context, out CompiledExpression inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = statement;
        error = null;
        return true;
    }
    static bool Inline(CompiledVariableDefinition statement, InlineContext context, out CompiledStatement inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = statement;
        if (!Inline(statement.InitialValue, context, out CompiledExpression? inlinedValue, out error)) return false;
        if (!Inline(statement.TypeExpression, context, out CompiledTypeExpression? inlinedType, out error)) return false;

        CompiledVariableDefinition _inlined = new()
        {
            InitialValue = inlinedValue,
            TypeExpression = inlinedType,
            Definition = statement.Definition,
            Cleanup = statement.Cleanup,
            Identifier = statement.Identifier,
            IsGlobal = statement.IsGlobal,
            Type = statement.Type,
            Location = statement.Location,
        };
        context.VariableReplacements[statement] = _inlined;
        inlined = _inlined;
        return true;
    }
    static bool Inline(CompiledReturn statement, InlineContext context, out CompiledStatement inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = statement;
        if (!Inline(statement.Value, context, out CompiledExpression? inlinedValue, out error)) return false;

        inlined = new CompiledReturn()
        {
            Value = inlinedValue,
            Location = statement.Location,
        };
        return true;
    }
    static bool Inline(CompiledCrash statement, InlineContext context, out CompiledStatement inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = statement;

        if (!Inline(statement.Value, context, out CompiledExpression? inlinedValue, out error)) return false;

        inlined = new CompiledCrash()
        {
            Value = inlinedValue,
            Location = statement.Location,
        };
        return true;
    }
    static bool Inline(CompiledBreak statement, InlineContext context, out CompiledStatement inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = statement;
        error = null;
        return true;
    }
    static bool Inline(CompiledDelete statement, InlineContext context, out CompiledStatement inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = statement;

        if (!Inline(statement.Value, context, out CompiledExpression? inlinedValue, out error)) return false;

        inlined = new CompiledDelete()
        {
            Value = inlinedValue,
            Cleanup = statement.Cleanup,
            Location = statement.Location,
        };
        return true;
    }
    static bool Inline(CompiledGoto statement, InlineContext context, out CompiledStatement inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = statement;

        if (!Inline(statement.Value, context, out CompiledExpression? inlinedValue, out error)) return false;

        inlined = new CompiledGoto()
        {
            Value = inlinedValue,
            Location = statement.Location,
        };
        return true;
    }
    static bool Inline(CompiledSetter statement, InlineContext context, out CompiledStatement inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = statement;

        if (!Inline(statement.Value, context, out CompiledExpression? inlinedValue, out error)) return false;
        if (!Inline(statement.Target, context, out CompiledStatement? target, out error)) return false;

        if (target is not CompiledAccessExpression accessExpression)
        {
            error = DiagnosticAt.Error($"Invalid expression", target, false);
            return false;
        }

        inlined = new CompiledSetter()
        {
            Value = inlinedValue,
            Target = accessExpression,
            IsCompoundAssignment = statement.IsCompoundAssignment,
            Location = statement.Location,
        };
        return true;
    }
    static bool Inline(CompiledWhileLoop statement, InlineContext context, out CompiledStatement inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = statement;

        if (!Inline(statement.Condition, context, out CompiledExpression? inlinedCondition, out error)) return false;
        if (!Inline(statement.Body, context, out CompiledStatement? inlinedBody, out error)) return false;

        inlined = new CompiledWhileLoop()
        {
            Condition = inlinedCondition,
            Body = inlinedBody,
            Location = statement.Location,
        };
        return true;
    }
    static bool Inline(CompiledForLoop statement, InlineContext context, out CompiledStatement inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = statement;

        CompiledStatement? inlinedVariableDeclaration = null;
        if (statement.Initialization is not null && !Inline(statement.Initialization, context, out inlinedVariableDeclaration, out error)) return false;
        if (!Inline(statement.Condition, context, out CompiledExpression? inlinedCondition, out error)) return false;
        if (!Inline(statement.Step, context, out CompiledStatement? inlinedExpression, out error)) return false;
        if (!Inline(statement.Body, context, out CompiledStatement? inlinedBody, out error)) return false;

        inlined = new CompiledForLoop()
        {
            Initialization = inlinedVariableDeclaration,
            Condition = inlinedCondition,
            Step = inlinedExpression,
            Body = inlinedBody,
            Location = statement.Location,
        };
        return true;
    }
    static bool Inline(CompiledIf statement, InlineContext context, out CompiledStatement inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = statement;

        if (!Inline(statement.Condition, context, out CompiledExpression? inlinedCondition, out error)) return false;
        if (!Inline(statement.Body, context, out CompiledStatement? inlinedBody, out error)) return false;
        if (!Inline(statement.Next, context, out CompiledStatement? inlinedNext, out error)) return false;

        inlined = new CompiledIf()
        {
            Condition = inlinedCondition,
            Body = inlinedBody,
            Next = inlinedNext switch
            {
                CompiledBranch v => v,
                null => null,
                _ => new CompiledElse()
                {
                    Body = inlinedNext,
                    Location = inlinedNext.Location,
                },
            },
            Location = statement.Location,
        };
        return true;
    }
    static bool Inline(CompiledElse statement, InlineContext context, out CompiledStatement inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = statement;

        if (!Inline(statement.Body, context, out CompiledStatement? inlinedBody, out error)) return false;

        inlined = new CompiledElse()
        {
            Body = inlinedBody,
            Location = statement.Location,
        };
        return true;
    }
    static bool Inline(CompiledBlock statement, InlineContext context, out CompiledStatement inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = statement;

        ImmutableArray<CompiledStatement>.Builder statements = ImmutableArray.CreateBuilder<CompiledStatement>(statement.Statements.Length);
        foreach (CompiledStatement v in statement.Statements)
        {
            if (!Inline(v, context, out CompiledStatement? inlinedStatement, out error))
            { return false; }

            statements.Add(inlinedStatement);

            if (v is CompiledReturn)
            { break; }
        }

        inlined = new CompiledBlock()
        {
            Statements = statements.DrainToImmutable(),
            Location = statement.Location,
        };
        error = null;
        return true;
    }
    static bool Inline(CompiledLabelDeclaration statement, InlineContext context, out CompiledStatement inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = statement;
        error = null;
        return true;
    }
    static bool Inline(CompiledEmptyStatement statement, InlineContext context, out CompiledStatement inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = statement;
        error = null;
        return true;
    }
    static bool Inline(CompiledCompilerVariableAccess statement, InlineContext context, out CompiledExpression inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        inlined = statement;
        error = null;
        return true;
    }

    static bool Inline(CompiledStatement? statement, InlineContext context, [NotNullIfNotNull(nameof(statement))] out CompiledStatement? inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        if (statement is null)
        {
            inlined = null;
            error = null;
            return true;
        }

        switch (statement)
        {
            case CompiledVariableDefinition v: return Inline(v, context, out inlined, out error);
            case CompiledReturn v: return Inline(v, context, out inlined, out error);
            case CompiledCrash v: return Inline(v, context, out inlined, out error);
            case CompiledBreak v: return Inline(v, context, out inlined, out error);
            case CompiledDelete v: return Inline(v, context, out inlined, out error);
            case CompiledGoto v: return Inline(v, context, out inlined, out error);
            case CompiledSetter v: return Inline(v, context, out inlined, out error);
            case CompiledWhileLoop v: return Inline(v, context, out inlined, out error);
            case CompiledForLoop v: return Inline(v, context, out inlined, out error);
            case CompiledIf v: return Inline(v, context, out inlined, out error);
            case CompiledElse v: return Inline(v, context, out inlined, out error);
            case CompiledBlock v: return Inline(v, context, out inlined, out error);
            case CompiledLabelDeclaration v: return Inline(v, context, out inlined, out error);
            case CompiledEmptyStatement v: return Inline(v, context, out inlined, out error);
            case CompiledExpression v:
                if (Inline(v, context, out CompiledExpression inlinedWithValue, out error))
                {
                    inlined = inlinedWithValue;
                    return true;
                }
                else
                {
                    inlined = inlinedWithValue;
                    return false;
                }
            default: throw new NotImplementedException(statement.GetType().Name);
        }
    }
    static bool Inline(CompiledExpression? statement, InlineContext context, [NotNullIfNotNull(nameof(statement))] out CompiledExpression? inlined, [NotNullWhen(false)] out DiagnosticAt? error)
    {
        if (statement is null)
        {
            inlined = null;
            error = null;
            return true;
        }

        return statement switch
        {
            CompiledSizeof v => Inline(v, context, out inlined, out error),
            CompiledBinaryOperatorCall v => Inline(v, context, out inlined, out error),
            CompiledUnaryOperatorCall v => Inline(v, context, out inlined, out error),
            CompiledConstantValue v => Inline(v, context, out inlined, out error),
            CompiledRegisterAccess v => Inline(v, context, out inlined, out error),
            CompiledVariableAccess v => Inline(v, context, out inlined, out error),
            CompiledExpressionVariableAccess v => Inline(v, context, out inlined, out error),
            CompiledParameterAccess v => Inline(v, context, out inlined, out error),
            CompiledFunctionReference v => Inline(v, context, out inlined, out error),
            CompiledLabelReference v => Inline(v, context, out inlined, out error),
            CompiledFieldAccess v => Inline(v, context, out inlined, out error),
            CompiledElementAccess v => Inline(v, context, out inlined, out error),
            CompiledGetReference v => Inline(v, context, out inlined, out error),
            CompiledDereference v => Inline(v, context, out inlined, out error),
            CompiledStackAllocation v => Inline(v, context, out inlined, out error),
            CompiledConstructorCall v => Inline(v, context, out inlined, out error),
            CompiledCast v => Inline(v, context, out inlined, out error),
            CompiledReinterpretation v => Inline(v, context, out inlined, out error),
            CompiledRuntimeCall v => Inline(v, context, out inlined, out error),
            CompiledFunctionCall v => Inline(v, context, out inlined, out error),
            CompiledExternalFunctionCall v => Inline(v, context, out inlined, out error),
            CompiledDummyExpression v => Inline(v, context, out inlined, out error),
            CompiledString v => Inline(v, context, out inlined, out error),
            CompiledStackString v => Inline(v, context, out inlined, out error),
            CompiledCompilerVariableAccess v => Inline(v, context, out inlined, out error),

            _ => throw new NotImplementedException(statement.GetType().ToString()),
        };
    }

    #endregion

    #region Control Flow Usage

    [Flags]
    public enum ControlFlowUsage
    {
        None = 0x0,
        Return = 0x1,
        ConditionalReturn = 0x2,
        Break = 0x4,
    }

    static ControlFlowUsage FindControlFlowUsage(IEnumerable<Statement> statements, bool inDepth = false)
    {
        ControlFlowUsage result = ControlFlowUsage.None;
        foreach (Statement statement in statements)
        { result |= FindControlFlowUsage(statement, inDepth); }
        return result;
    }
    static ControlFlowUsage FindControlFlowUsage(Statement? statement, bool inDepth = false) => statement switch
    {
        Block v => FindControlFlowUsage(v.Statements, true),
        KeywordCallStatement v => FindControlFlowUsage(v, inDepth),
        WhileLoopStatement v => FindControlFlowUsage(v.Body, true),
        ForLoopStatement v => FindControlFlowUsage(v.Body.Statements, true),
        IfBranchStatement v => FindControlFlowUsage(v.Body, true) | FindControlFlowUsage(v.Else, true),
        ElseBranchStatement v => FindControlFlowUsage(v.Body, true),

        SimpleAssignmentStatement => ControlFlowUsage.None,
        VariableDefinition => ControlFlowUsage.None,
        AnyCallExpression => ControlFlowUsage.None,
        ShortOperatorCall => ControlFlowUsage.None,
        CompoundAssignmentStatement => ControlFlowUsage.None,
        BinaryOperatorCallExpression => ControlFlowUsage.None,
        IdentifierExpression => ControlFlowUsage.None,
        ConstructorCallExpression => ControlFlowUsage.None,
        FieldExpression => ControlFlowUsage.None,
        EmptyStatement => ControlFlowUsage.None,
        null => ControlFlowUsage.None,

        _ => throw new NotImplementedException(statement.GetType().Name),
    };
    static ControlFlowUsage FindControlFlowUsage(KeywordCallStatement statement, bool inDepth = false)
    {
        switch (statement.Keyword.Content)
        {
            case StatementKeywords.Return:
            {
                if (inDepth) return ControlFlowUsage.ConditionalReturn;
                else return ControlFlowUsage.Return;
            }

            case StatementKeywords.Break:
            {
                return ControlFlowUsage.Break;
            }

            case StatementKeywords.Delete:
            case StatementKeywords.Crash:
                return ControlFlowUsage.None;

            default: throw new NotImplementedException(statement.ToString());
        }
    }

    public static ControlFlowUsage FindControlFlowUsage(IEnumerable<CompiledStatement> statements, bool inDepth = false)
    {
        ControlFlowUsage result = ControlFlowUsage.None;
        foreach (CompiledStatement statement in statements)
        { result |= FindControlFlowUsage(statement, inDepth); }
        return result;
    }
    public static ControlFlowUsage FindControlFlowUsage(CompiledStatement statement, bool inDepth = false) => statement switch
    {
        CompiledBlock v => FindControlFlowUsage(v.Statements, true),
        CompiledReturn => inDepth ? ControlFlowUsage.ConditionalReturn : ControlFlowUsage.Return,
        CompiledBreak => ControlFlowUsage.Break,
        CompiledWhileLoop v => FindControlFlowUsage(v.Body, true),
        CompiledForLoop v => FindControlFlowUsage(v.Body, true),
        CompiledIf v => FindControlFlowUsage(v.Body, true) | (v.Next is null ? ControlFlowUsage.None : FindControlFlowUsage(v.Next, true)),
        CompiledElse v => FindControlFlowUsage(v.Body, true),

        _ => ControlFlowUsage.None,
    };

    #endregion

    #region Compile Time Evaluation

    static bool TryComputeUnaryOperator(string @operator, CompiledValue left, [NotNullWhen(true)] out CompiledValue result, [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        // todo: wtf
        error = null;
        result = @operator switch
        {
            UnaryOperatorCallExpression.LogicalNOT => !left,
            UnaryOperatorCallExpression.BinaryNOT => ~left,
            UnaryOperatorCallExpression.UnaryPlus => +left,
            UnaryOperatorCallExpression.UnaryMinus => -left,

            _ => throw new NotImplementedException($"Unknown unary operator \"{@operator}\""),
        };
        return true;
    }
    static bool TryComputeBinaryOperator(string @operator, CompiledValue left, CompiledValue right, [NotNullWhen(true)] out CompiledValue result, [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        // todo: wtf
        try
        {
            error = null;
            result = @operator switch
            {
                BinaryOperatorCallExpression.Addition => left + right,
                BinaryOperatorCallExpression.Subtraction => left - right,
                BinaryOperatorCallExpression.Multiplication => left * right,
                BinaryOperatorCallExpression.Division => left / right,
                BinaryOperatorCallExpression.Modulo => left % right,

                BinaryOperatorCallExpression.LogicalAND => new CompiledValue((bool)left && (bool)right),
                BinaryOperatorCallExpression.LogicalOR => new CompiledValue((bool)left || (bool)right),

                BinaryOperatorCallExpression.BitwiseAND => left & right,
                BinaryOperatorCallExpression.BitwiseOR => left | right,
                BinaryOperatorCallExpression.BitwiseXOR => left ^ right,

                BinaryOperatorCallExpression.BitshiftLeft => left << right,
                BinaryOperatorCallExpression.BitshiftRight => left >> right,

                BinaryOperatorCallExpression.CompLT => new CompiledValue(left < right),
                BinaryOperatorCallExpression.CompGT => new CompiledValue(left > right),
                BinaryOperatorCallExpression.CompEQ => new CompiledValue(left == right),
                BinaryOperatorCallExpression.CompNEQ => new CompiledValue(left != right),
                BinaryOperatorCallExpression.CompLEQ => new CompiledValue(left <= right),
                BinaryOperatorCallExpression.CompGEQ => new CompiledValue(left >= right),

                _ => throw new NotImplementedException($"Unknown binary operator \"{@operator}\""),
            };
            return true;
        }
        catch (Exception)
        {
            if (left.Type != right.Type)
            {
                result = default;
                error = new PossibleDiagnostic($"Can do {@operator} operator to type {left.Type} and {right.Type}");
                return false;
            }
            throw;
        }
    }

    static bool TryComputeSimple(LiteralExpression literal, out CompiledValue value)
    {
        switch (literal)
        {
            case IntLiteralExpression intLiteral:
                value = new CompiledValue(intLiteral.Value);
                return true;
            case FloatLiteralExpression floatLiteral:
                value = new CompiledValue(floatLiteral.Value);
                return true;
            case CharLiteralExpression charLiteral:
                value = new CompiledValue(charLiteral.Value);
                return true;
            case StringLiteralExpression:
            default:
                value = CompiledValue.Null;
                return false;
        }
    }
    static bool TryComputeSimple(BinaryOperatorCallExpression @operator, out CompiledValue value)
    {
        value = CompiledValue.Null;

        if (!TryComputeSimple(@operator.Left, out CompiledValue leftValue))
        { return false; }

        if (TryComputeSimple(@operator.Right, out CompiledValue rightValue) &&
            TryComputeBinaryOperator(@operator.Operator.Content, leftValue, rightValue, out value, out _))
        { return true; }

        switch (@operator.Operator.Content)
        {
            case "&&":
            {
                if (!leftValue)
                {
                    value = new CompiledValue(false);
                    return true;
                }
                break;
            }
            case "||":
            {
                if (leftValue)
                {
                    value = new CompiledValue(true);
                    return true;
                }
                break;
            }
            default: return false;
        }

        value = leftValue;
        return true;
    }
    static bool TryComputeSimple(UnaryOperatorCallExpression @operator, out CompiledValue value)
    {
        value = CompiledValue.Null;
        return TryComputeSimple(@operator.Expression, out CompiledValue leftValue)
            && TryComputeUnaryOperator(@operator.Operator.Content, leftValue, out value, out _);
    }
    static bool TryComputeSimple(IndexCallExpression indexCall, out CompiledValue value)
    {
        if (indexCall.Object is StringLiteralExpression literal &&
            TryComputeSimple(indexCall.Index, out CompiledValue indexValue))
        {
            int index = (int)indexValue;
            if (index >= 0 && index <= literal.Value.Length)
            {
                value = new CompiledValue(index == literal.Value.Length ? '\0' : literal.Value[index]);
                return true;
            }
        }

        value = CompiledValue.Null;
        return false;
    }
    public static bool TryComputeSimple(ArgumentExpression statement, out CompiledValue value)
    {
        return TryComputeSimple(statement.Value, out value);
    }
    public static bool TryComputeSimple(Expression? statement, out CompiledValue value)
    {
        value = CompiledValue.Null;
        return statement switch
        {
            LiteralExpression v => TryComputeSimple(v, out value),
            BinaryOperatorCallExpression v => TryComputeSimple(v, out value),
            UnaryOperatorCallExpression v => TryComputeSimple(v, out value),
            IndexCallExpression v => TryComputeSimple(v, out value),
            ArgumentExpression v => TryComputeSimple(v, out value),
            _ => false,
        };
    }

    abstract class RuntimeStatement2 :
        IPositioned,
        IInFile,
        ILocated
    {
        public abstract Position Position { get; }
        public abstract Uri File { get; }

        public Location Location => new(Position, File);
    }

    abstract class RuntimeStatement2<TOriginal> : RuntimeStatement2
        where TOriginal : CompiledStatement
    {
        public TOriginal Original { get; }

        public override Position Position => Original.Location.Position;

        protected RuntimeStatement2(TOriginal original)
        {
            Original = original;
        }
    }

    class RuntimeFunctionCall2 : RuntimeStatement2<CompiledExternalFunctionCall>
    {
        public ImmutableArray<CompiledValue> Parameters { get; }
        public override Uri File => Original.Location.File;

        public RuntimeFunctionCall2(ImmutableArray<CompiledValue> parameters, CompiledExternalFunctionCall original) : base(original)
        {
            Parameters = parameters;
        }
    }

    class EvaluationScope
    {
        public readonly Dictionary<CompiledVariableDefinition, CompiledValue> Variables = new();
    }

    class EvaluationFrame
    {
        public readonly CompiledFunction Function;
        public CompiledValue? ReturnValue;
        public readonly Dictionary<string, CompiledValue> Parameters = new();
        public readonly Stack<EvaluationScope> Scopes = new();

        public EvaluationFrame(CompiledFunction function) => Function = function;
    }

    class EvaluationContext
    {
        public readonly Stack<EvaluationFrame> Frames;

        public readonly List<RuntimeStatement2> RuntimeStatements;

        public bool IsReturning;
        public bool IsBreaking;

        public static EvaluationContext Empty => new();

        public EvaluationContext()
        {
            Frames = new();
            RuntimeStatements = new();
        }

        public bool TryGetVariable(CompiledVariableDefinition name, out CompiledValue value)
        {
            value = default;

            if (Frames.LastOrDefault is null)
            { return false; }

            foreach (EvaluationScope scope in Frames.LastOrDefault.Scopes)
            {
                if (!scope.Variables.TryGetValue(name, out value)) continue;
                return true;
            }

            return false;
        }

        public bool TryGetParameter(string name, out CompiledValue value)
        {
            value = default;

            if (Frames.LastOrDefault is null)
            { return false; }

            return Frames.LastOrDefault.Parameters.TryGetValue(name, out value);
        }

        public bool TrySetVariable(CompiledVariableDefinition name, CompiledValue value)
        {
            if (Frames.LastOrDefault is null)
            { return false; }

            foreach (EvaluationScope scope in Frames.LastOrDefault.Scopes)
            {
                if (!scope.Variables.ContainsKey(name)) continue;
                scope.Variables[name] = value;
                return true;
            }

            return false;
        }

        public bool TrySetParameter(string name, CompiledValue value)
        {
            if (Frames.LastOrDefault is null)
            { return false; }

            if (Frames.LastOrDefault.Parameters.ContainsKey(name))
            { return false; }

            Frames.LastOrDefault.Parameters[name] = value;
            return true;
        }

        public void PushScope()
        {
            if (Frames.LastOrDefault is null) return;
            Frames.LastOrDefault.Scopes.Push(new());
        }

        public void PopScope()
        {
            if (Frames.LastOrDefault is null) return;
            Frames.LastOrDefault.Scopes.Pop();
        }
    }

    bool TryCompute(CompiledDereference pointer, EvaluationContext context, out CompiledValue value, [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        if (pointer.Address is CompiledGetReference addressGetter)
        { return TryCompute(addressGetter.Of, context, out value, out error); }

        value = CompiledValue.Null;
        error = new PossibleDiagnostic($"Cannot compute runtime references", pointer);
        return false;
    }
    bool TryCompute(CompiledBinaryOperatorCall @operator, EvaluationContext context, out CompiledValue value, [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        if (!TryCompute(@operator.Left, context, out CompiledValue leftValue, out error))
        {
            value = CompiledValue.Null;
            return false;
        }

        string op = @operator.Operator;

        if (TryCompute(@operator.Right, context, out CompiledValue rightValue, out error) &&
            TryComputeBinaryOperator(op, leftValue, rightValue, out value, out error))
        {
            return true;
        }

        error ??= error?.Populated(@operator);

        switch (op)
        {
            case "&&":
            {
                if (!(bool)leftValue)
                {
                    value = new CompiledValue(false);
                    return true;
                }
                break;
            }
            case "||":
            {
                if ((bool)leftValue)
                {
                    value = new CompiledValue(true);
                    return true;
                }
                break;
            }
            default:
                value = CompiledValue.Null;
                error ??= new PossibleDiagnostic($"Cannot compute this", @operator);
                return false;
        }

        value = leftValue;
        return true;
    }
    bool TryCompute(CompiledUnaryOperatorCall @operator, EvaluationContext context, out CompiledValue value, [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        if (TryCompute(@operator.Expression, context, out CompiledValue leftValue, out error)
            && TryComputeUnaryOperator(@operator.Operator, leftValue, out value, out error))
        {
            return true;
        }

        value = CompiledValue.Null;
        error = error.Populated(@operator);
        return false;
    }
    bool TryCompute(CompiledConstantValue literal, EvaluationContext context, out CompiledValue value, [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        value = literal.Value;
        error = null;
        return true;
    }
    bool TryCompute(CompiledFunctionCall functionCall, EvaluationContext context, out CompiledValue value, [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        value = CompiledValue.Null;
        error = null;

        if (!TryCompute(functionCall.Arguments, context, out ImmutableArray<CompiledValue> parameters, out error))
        {
            return false;
        }

        if (functionCall.Function.Template is IExternalFunctionDefinition externalFunctionDefinition &&
            externalFunctionDefinition.ExternalFunctionName is not null)
        {
            Debugger.Break();
            error = new PossibleDiagnostic($"meow :3", functionCall);
            return false;
        }

        CompiledFunction? found = GeneratedFunctions.FirstOrDefault(v => Utils.ReferenceEquals(v.Function, functionCall.Function.Template) && TypeArgumentsEquals(v.TypeArguments, functionCall.Function.TypeArguments));

        if (found is null)
        {
            error = new PossibleDiagnostic($"Function {functionCall.Function.Template.ToReadable()} wasn't generated (for whatever reason idk meow :3)", functionCall);
            return false;
        }

        if (!TryEvaluate(found, parameters, context, out CompiledValue? returnValue, out ImmutableArray<RuntimeStatement2> runtimeStatements, out error))
        {
            error = new PossibleDiagnostic($"Couldn't evaluate the function body", functionCall, error);
            return false;
        }

        if (!returnValue.HasValue)
        {
            error = new PossibleDiagnostic($"Function \"{found}\" didn't return anything", functionCall);
            return false;
        }

        if (runtimeStatements.Length > 0)
        {
            error = new PossibleDiagnostic($"Function \"{found}\" contains runtime statements", functionCall);
            return false;
        }

        value = returnValue.Value;
        return true;
    }
    bool TryCompute(CompiledSizeof functionCall, EvaluationContext context, out CompiledValue value, [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        if (!FindSize(functionCall.Of, out int size, out error, this))
        {
            value = CompiledValue.Null;
            error = error.Populated(functionCall);
            return false;
        }

        value = new CompiledValue(size);
        error = null;
        return true;
    }
    bool TryCompute(CompiledVariableAccess identifier, EvaluationContext context, out CompiledValue value, [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        if (!context.TryGetVariable(identifier.Variable, out value))
        {
            value = CompiledValue.Null;
            error = new PossibleDiagnostic($"Variable \"{identifier.Variable.Identifier}\" not found", identifier);
            return false;
        }

        error = null;
        return true;
    }
    bool TryCompute(CompiledParameterAccess identifier, EvaluationContext context, out CompiledValue value, [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        if (!context.TryGetParameter(identifier.Parameter.Identifier, out value))
        {
            value = CompiledValue.Null;
            error = new PossibleDiagnostic($"Parameter \"{identifier.Parameter.Identifier}\" not found", identifier);
            return false;
        }

        error = null;
        return true;
    }
    bool TryCompute(CompiledReinterpretation typeCast, EvaluationContext context, out CompiledValue value, [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        if (!TryCompute(typeCast.Value, context, out value, out error))
        {
            value = CompiledValue.Null;
            return false;
        }

        if (!typeCast.Type.Is(out BuiltinType? builtinType))
        {
            error = new PossibleDiagnostic($"This must be a built-in type to compute", typeCast.TypeExpression);
            return false;
        }

        value = CompiledValue.CreateUnsafe(value.I32, builtinType.RuntimeType);
        return true;
    }
    bool TryCompute(CompiledCast typeCast, EvaluationContext context, out CompiledValue value, [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        if (!TryCompute(typeCast.Value, context, out value, out error))
        {
            value = CompiledValue.Null;
            return false;
        }

        if (!typeCast.Type.Is(out BuiltinType? builtinType))
        {
            error = new PossibleDiagnostic($"This must be a built-in type to compute", typeCast.TypeExpression);
            return false;
        }

        if (!value.TryCast(builtinType.RuntimeType, out CompiledValue casted))
        {
            error = new PossibleDiagnostic($"Failed to cast {value.ToStringValue()} to type {builtinType.RuntimeType}", typeCast);
            return false;
        }

        value = casted;
        return true;
    }
    bool TryCompute(CompiledElementAccess indexCall, EvaluationContext context, out CompiledValue value, [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        if (!TryCompute(indexCall.Index, context, out CompiledValue index, out error))
        {
            value = CompiledValue.Null;
            return false;
        }

        if (indexCall.Base is CompiledString stringInstance)
        {
            if (index.Type == RuntimeType.F32)
            {
                error = new PossibleDiagnostic($"Invalid index type {index.Type}");
                value = CompiledValue.Null;
                return false;
            }

            if (stringInstance.IsUTF8)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(stringInstance.Value);
                if (index == bytes.Length)
                { value = new CompiledValue((byte)'\0'); }
                else if (index >= 0 && index < bytes.Length)
                { value = new CompiledValue(bytes[(int)index]); }
                else
                {
                    error = new PossibleDiagnostic($"Index {index} out of range");
                    value = CompiledValue.Null;
                    return false;
                }
            }
            else
            {
                if (index == stringInstance.Value.Length)
                { value = new CompiledValue('\0'); }
                else if (index >= 0 && index < stringInstance.Value.Length)
                { value = new CompiledValue(stringInstance.Value[(int)index]); }
                else
                {
                    error = new PossibleDiagnostic($"Index {index} out of range");
                    value = CompiledValue.Null;
                    return false;
                }
            }
            return true;
        }

        if (indexCall.Base is CompiledStackString stackString)
        {
            if (stackString.IsUTF8)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(stackString.Value);
                if (index == bytes.Length)
                {
                    if (!stackString.IsNullTerminated)
                    {
                        error = new PossibleDiagnostic($"Index {index} out of range");
                        value = CompiledValue.Null;
                        return false;
                    }

                    value = new CompiledValue((byte)'\0');
                }
                else if (index >= 0 && index < bytes.Length)
                {
                    value = new CompiledValue(bytes[(int)index]);
                }
                else
                {
                    error = new PossibleDiagnostic($"Index {index} out of range");
                    value = CompiledValue.Null;
                    return false;
                }
            }
            else
            {
                if (index == stackString.Value.Length)
                {
                    if (!stackString.IsNullTerminated)
                    {
                        error = new PossibleDiagnostic($"Index {index} out of range");
                        value = CompiledValue.Null;
                        return false;
                    }

                    value = new CompiledValue('\0');
                }
                else if (index >= 0 && index < stackString.Value.Length)
                {
                    value = new CompiledValue(stackString.Value[(int)index]);
                }
                else
                {
                    error = new PossibleDiagnostic($"Index {index} out of range");
                    value = CompiledValue.Null;
                    return false;
                }
            }

            return true;
        }

        if (indexCall.Base is CompiledList listLiteral)
        {
            return TryCompute(listLiteral.Values[(int)index], context, out value, out error);
        }

        value = CompiledValue.Null;
        error = new PossibleDiagnostic($"Cannot index into a runtime-only array", indexCall);
        return false;
    }
    bool TryCompute(CompiledFunctionReference functionAddressGetter, EvaluationContext context, out CompiledValue value, [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        value = CompiledValue.Null;
        error = new PossibleDiagnostic($"Cannot compute runtime-only references", functionAddressGetter);
        return false;
    }

    bool TryCompute([NotNullWhen(true)] CompiledExpression? statement, out CompiledValue value, [NotNullWhen(false)] out PossibleDiagnostic? error)
        => TryCompute(statement, EvaluationContext.Empty, out value, out error);
    bool TryCompute(IEnumerable<CompiledExpression>? statements, EvaluationContext context, [NotNullWhen(true)] out ImmutableArray<CompiledValue> values, [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        if (statements is null)
        {
            values = ImmutableArray<CompiledValue>.Empty;
            error = new PossibleDiagnostic($"Cannot compute nothingness");
            return false;
        }

        ImmutableArray<CompiledValue>.Builder result = ImmutableArray.CreateBuilder<CompiledValue>();
        foreach (CompiledExpression statement in statements)
        {
            if (!TryCompute(statement, context, out CompiledValue value, out error))
            {
                values = ImmutableArray<CompiledValue>.Empty;
                return false;
            }

            result.Add(value);
        }

        values = result.ToImmutable();
        error = null;
        return true;
    }
    bool TryCompute([NotNullWhen(true)] CompiledExpression? statement, EvaluationContext context, out CompiledValue value, [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        value = CompiledValue.Null;

        if (statement is null)
        {
            error = new PossibleDiagnostic($"Cannot compute nothingness");
            return false;
        }

        error = new PossibleDiagnostic($"Cannot compute runtime-only expressions ({statement.GetType().Name})", statement);
        return statement switch
        {
            CompiledConstantValue v => TryCompute(v, context, out value, out error),
            CompiledBinaryOperatorCall v => TryCompute(v, context, out value, out error),
            CompiledUnaryOperatorCall v => TryCompute(v, context, out value, out error),
            CompiledDereference v => TryCompute(v, context, out value, out error),
            CompiledFunctionCall v => TryCompute(v, context, out value, out error),
            CompiledSizeof v => TryCompute(v, context, out value, out error),
            CompiledVariableAccess v => TryCompute(v, context, out value, out error),
            CompiledParameterAccess v => TryCompute(v, context, out value, out error),
            CompiledReinterpretation v => TryCompute(v, context, out value, out error),
            CompiledCast v => TryCompute(v, context, out value, out error),
            CompiledElementAccess v => TryCompute(v, context, out value, out error),
            CompiledArgument v => TryCompute(v.Value, context, out value, out error),
            CompiledFunctionReference v => TryCompute(v, context, out value, out error),
            CompiledEnumMemberAccess v => TryCompute(v.EnumMember.Value, context, out value, out error),
            CompiledLambda => false, // todo

            CompiledString => false,
            CompiledStackString => false,
            CompiledExternalFunctionCall => false,
            CompiledRuntimeCall => false,
            CompiledFieldAccess => false,
            CompiledStackAllocation => false,
            CompiledConstructorCall => false,
            CompiledGetReference => false,
            CompiledList => false,
            CompiledDummyExpression => false,
            CompiledRegisterAccess => false,
            CompiledLabelReference => false,
            CompiledExpressionVariableAccess => false,
            CompiledCompilerVariableAccess => false,

            _ => throw new NotImplementedException(statement.GetType().ToString()),
        };
    }

    bool TryEvaluate(ICompiledFunctionDefinition function, ImmutableArray<CompiledArgument> parameters, EvaluationContext context, out CompiledValue? value, [NotNullWhen(true)] out ImmutableArray<RuntimeStatement2> runtimeStatements, [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        value = default;
        runtimeStatements = default;

        CompiledFunction? found = GeneratedFunctions.FirstOrDefault(v => Utils.ReferenceEquals(v.Function, function) && TypeArgumentsEquals(v.TypeArguments, null));

        if (found is null)
        {
            error = new PossibleDiagnostic($"Function {function.ToReadable()} not compiled", function);
            return false;
        }

        if (TryCompute(parameters, context, out ImmutableArray<CompiledValue> parameterValues, out error)
            && TryEvaluate(found, parameterValues, context, out value, out runtimeStatements, out error))
        { return true; }

        return false;
    }
    bool TryEvaluate(ICompiledFunctionDefinition function, ImmutableArray<CompiledExpression> parameters, EvaluationContext context, out CompiledValue? value, [NotNullWhen(true)] out ImmutableArray<RuntimeStatement2> runtimeStatements, [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        value = default;
        runtimeStatements = default;

        CompiledFunction? found = GeneratedFunctions.FirstOrDefault(v => Utils.ReferenceEquals(v.Function, function) && TypeArgumentsEquals(v.TypeArguments, null));

        if (found is null)
        {
            error = new PossibleDiagnostic($"Function {function.ToReadable()} not compiled", function);
            return false;
        }

        if (TryCompute(parameters, context, out ImmutableArray<CompiledValue> parameterValues, out error)
            && TryEvaluate(found, parameterValues, context, out value, out runtimeStatements, out error))
        { return true; }

        return false;
    }
    bool TryEvaluate(CompiledFunction function, ImmutableArray<CompiledValue> parameterValues, EvaluationContext context, out CompiledValue? value, [NotNullWhen(true)] out ImmutableArray<RuntimeStatement2> runtimeStatements, [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        value = null;
        runtimeStatements = default;

        {
            ImmutableArray<CompiledValue>.Builder castedParameterValues = ImmutableArray.CreateBuilder<CompiledValue>(parameterValues.Length);
            for (int i = 0; i < parameterValues.Length; i++)
            {
                if (!parameterValues[i].TryCast(function.Function.Parameters[i].Type, out CompiledValue castedValue))
                {
                    // Debugger.Break();
                    error = new PossibleDiagnostic($"Argument {i}: Can't cast value {parameterValues[i]} of type {parameterValues[i].Type} to {function.Function.Parameters[i].Type}");
                    return false;
                }

                if (!function.Function.Parameters[i].Type.SameAs(castedValue.Type))
                {
                    // Debugger.Break();
                    error = new PossibleDiagnostic($"Argument {i}: {function.Function.Parameters[i].Type} != {castedValue.Type}");
                    return false;
                }

                castedParameterValues.Add(castedValue);
            }
            parameterValues = castedParameterValues.MoveToImmutable();
        }

        if (function.Function.ReturnSomething)
        {
            if (!function.Function.Type.Is<BuiltinType>())
            {
                error = new PossibleDiagnostic($"Can't evalute functions with not built-in return types");
                return false;
            }
        }

        if (context.Frames.Count > 8)
        {
            error = new PossibleDiagnostic($"Call stack reached it's maximum value ({8})");
            return false;
        }

        using (context.Frames.PushAuto(new EvaluationFrame(function)))
        {
            for (int i = 0; i < parameterValues.Length; i++)
            {
                context.Frames.Last.Parameters.Add(function.Function.Parameters[i].Identifier, parameterValues[i]);
            }

            if (!TryEvaluate(function.Body, context, out error))
            { return false; }

            if (function.Function.ReturnSomething)
            {
                if (context.Frames?.LastOrDefault is null)
                { throw new InternalExceptionWithoutContext(); }

                if (!context.Frames.LastOrDefault.ReturnValue.HasValue)
                {
                    error = new PossibleDiagnostic($"Function \"{function.ToReadable()}\" didn't return anything");
                    return false;
                }

                value = context.Frames.LastOrDefault.ReturnValue.Value;
            }
        }

        runtimeStatements = context.RuntimeStatements.ToImmutableArray();
        error = null;
        return true;
    }
    bool TryEvaluate(CompiledWhileLoop whileLoop, EvaluationContext context, [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        const int MaxIterations = 64;
        int iterations = MaxIterations;

        while (true)
        {
            if (!TryCompute(whileLoop.Condition, context, out CompiledValue condition, out error))
            { return false; }

            if (!condition)
            { break; }

            if (iterations-- < 0)
            {
                error = new PossibleDiagnostic($"While loop reached it's maximum allowed iteration count ({MaxIterations})", whileLoop);
                return false;
            }

            if (!TryEvaluate(whileLoop.Body, context, out error))
            {
                context.IsBreaking = false;
                return false;
            }

            if (context.IsBreaking)
            { break; }

            context.IsBreaking = false;
        }

        context.IsBreaking = false;
        error = null;
        return true;
    }
    bool TryEvaluate(CompiledForLoop forLoop, EvaluationContext context, [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        const int MaxIterations = 5048;
        int iterations = MaxIterations;

        context.PushScope();

        if (forLoop.Initialization is not null && !TryEvaluate(forLoop.Initialization, context, out error))
        { return false; }

        while (true)
        {
            CompiledValue condition;
            if (forLoop.Condition is null)
            { condition = true; }
            else if (!TryCompute(forLoop.Condition, context, out condition, out error))
            { return false; }

            if (!condition)
            { break; }

            if (iterations-- < 0)
            {
                error = new PossibleDiagnostic($"For loop reached it's maximum allowed iteration count ({MaxIterations})", forLoop);
                return false;
            }

            if (!TryEvaluate(forLoop.Body, context, out error))
            {
                context.IsBreaking = false;
                return false;
            }

            if (context.IsBreaking)
            { break; }

            if (forLoop.Step is not null && !TryEvaluate(forLoop.Step, context, out error))
            { return false; }

            context.IsBreaking = false;
        }

        context.IsBreaking = false;
        context.PopScope();
        error = null;
        return true;
    }
    bool TryEvaluate(CompiledIf ifContainer, EvaluationContext context, [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        CompiledBranch current = ifContainer;
        while (true)
        {
            switch (current)
            {
                case CompiledIf _if:
                {
                    if (!TryCompute(_if.Condition, context, out CompiledValue condition, out error))
                    { return false; }

                    if (condition)
                    { return TryEvaluate(_if.Body, context, out error); }

                    if (_if.Next is null)
                    { return true; }

                    current = _if.Next;
                    break;
                }
                case CompiledElse _else:
                {
                    return TryEvaluate(_else.Body, context, out error);
                }
                default:
                    throw new NotImplementedException();
            }
        }
    }
    bool TryEvaluate(CompiledBlock block, EvaluationContext context, [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        context.PushScope();
        bool result = TryEvaluate(block.Statements, context, out error);
        context.PopScope();
        return result;
    }
    bool TryEvaluate(CompiledVariableDefinition variableDefinition, EvaluationContext context, [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        CompiledValue value;

        if (context.Frames.LastOrDefault is null)
        {
            error = new PossibleDiagnostic($"Unexpected variable definition", variableDefinition);
            return false;
        }

        if (variableDefinition.InitialValue is null &&
            variableDefinition.Type.ToString() != StatementKeywords.Var)
        {
            if (!GetInitialValue(variableDefinition.Type, out value))
            {
                error = new PossibleDiagnostic($"Couldn't get an initial value for {variableDefinition.Type}", variableDefinition.TypeExpression);
                return false;
            }
        }
        else
        {
            if (!TryCompute(variableDefinition.InitialValue, context, out value, out error))
            { return false; }
        }

        if (context.Frames.LastOrDefault.Scopes.LastOrDefault is null)
        {
            error = new PossibleDiagnostic($"Unexpected variable definition", variableDefinition);
            return false;
        }

        if (!context.Frames.LastOrDefault.Scopes.LastOrDefault.Variables.TryAdd(variableDefinition, value))
        {
            error = new PossibleDiagnostic($"Variable \"{variableDefinition.Identifier}\" already exists", variableDefinition);
            return false;
        }

        error = null;
        return true;
    }
    bool TryEvaluate(CompiledSetter anyAssignment, EvaluationContext context, [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        if (!TryCompute(anyAssignment.Value, context, out CompiledValue value, out error))
        { return false; }

        if (anyAssignment.Target is CompiledVariableAccess targetVariable)
        {
            if (!context.TrySetVariable(targetVariable.Variable, value))
            {
                error = new PossibleDiagnostic($"Variable \"{targetVariable.Variable}\" not found", targetVariable);
                return false;
            }
        }
        else if (anyAssignment.Target is CompiledParameterAccess targetParameter)
        {
            if (!context.TrySetParameter(targetParameter.Parameter.Identifier, value))
            {
                error = new PossibleDiagnostic($"Variable \"{targetParameter.Parameter.Identifier}\" not found", targetParameter);
                return false;
            }
        }
        else
        {
            error = new PossibleDiagnostic($"Unsupported assign target {anyAssignment.Target.GetType().Name}", anyAssignment.Target);
            return false;
        }

        return true;
    }
    bool TryEvaluate(CompiledReturn keywordCall, EvaluationContext context, [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        context.IsReturning = true;

        if (keywordCall.Value is not null)
        {
            if (!TryCompute(keywordCall.Value, context, out CompiledValue returnValue, out error))
            { return false; }

            context.Frames.Last.ReturnValue = returnValue;
        }

        error = null;
        return true;
    }
    bool TryEvaluate(CompiledBreak keywordCall, EvaluationContext context, [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        context.IsBreaking = true;
        error = null;
        return true;
    }
    bool TryEvaluate(CompiledCrash keywordCall, EvaluationContext context, [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        error = new PossibleDiagnostic($"Cannot evaluate crash call", keywordCall);
        return false;
    }
    bool TryEvaluate(CompiledGoto keywordCall, EvaluationContext context, [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        error = new PossibleDiagnostic($"Cannot evaluate goto", keywordCall);
        return false;
    }
    bool TryEvaluate(CompiledDelete keywordCall, EvaluationContext context, [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        error = new PossibleDiagnostic($"Cannot evaluate delete call", keywordCall);
        return false;
    }
    bool TryEvaluate(CompiledStatement statement, EvaluationContext context, [NotNullWhen(false)] out PossibleDiagnostic? error) => statement switch
    {
        CompiledExpression v => TryCompute(v, context, out _, out error),
        CompiledBlock v => TryEvaluate(v, context, out error),
        CompiledVariableDefinition v => TryEvaluate(v, context, out error),
        CompiledWhileLoop v => TryEvaluate(v, context, out error),
        CompiledForLoop v => TryEvaluate(v, context, out error),
        CompiledSetter v => TryEvaluate(v, context, out error),
        CompiledReturn v => TryEvaluate(v, context, out error),
        CompiledCrash v => TryEvaluate(v, context, out error),
        CompiledBreak v => TryEvaluate(v, context, out error),
        CompiledGoto v => TryEvaluate(v, context, out error),
        CompiledDelete v => TryEvaluate(v, context, out error),
        CompiledIf v => TryEvaluate(v, context, out error),
        _ => throw new NotImplementedException(statement.GetType().ToString()),
    };
    bool TryEvaluate(ImmutableArray<CompiledStatement> statements, EvaluationContext context, [NotNullWhen(false)] out PossibleDiagnostic? error)
    {
        foreach (CompiledStatement statement in statements)
        {
            if (!TryEvaluate(statement, context, out error))
            { return false; }

            if (context.IsReturning || context.IsBreaking)
            { break; }
        }
        error = null;
        return true;
    }

    #endregion

    #region Find Size

    public static bool FindBitWidth(GeneralType type, out BitWidth size, [NotNullWhen(false)] out PossibleDiagnostic? error, IRuntimeInfoProvider runtime)
    {
        size = default;
        if (!FindSize(type, out int s, out error, runtime)) return false;
        switch (s)
        {
            case 1: size = BitWidth._8; return true;
            case 2: size = BitWidth._16; return true;
            case 4: size = BitWidth._32; return true;
            case 8: size = BitWidth._64; return true;
            default:
                error = new PossibleDiagnostic($"E");
                return false;
        }
    }

    public static bool FindSize(GeneralType type, out int size, [NotNullWhen(false)] out PossibleDiagnostic? error, IRuntimeInfoProvider runtime) => type switch
    {
        PointerType v => FindSize(v, out size, out error, runtime),
        ReferenceType v => FindSize(v, out size, out error, runtime),
        ArrayType v => FindSize(v, out size, out error, runtime),
        FunctionType v => FindSize(v, out size, out error, runtime),
        StructType v => FindSize(v, out size, out error, runtime),
        GenericType v => FindSize(v, out size, out error, runtime),
        BuiltinType v => FindSize(v, out size, out error, runtime),
        AliasType v => FindSize(v, out size, out error, runtime),
        EnumType v => FindSize(v, out size, out error, runtime),
        _ => throw new NotImplementedException(),
    };
    static bool FindSize(PointerType type, out int size, [NotNullWhen(false)] out PossibleDiagnostic? error, IRuntimeInfoProvider runtime)
    {
        size = runtime.PointerSize;
        error = null;
        return true;
    }
    static bool FindSize(ReferenceType type, out int size, [NotNullWhen(false)] out PossibleDiagnostic? error, IRuntimeInfoProvider runtime)
    {
        size = runtime.PointerSize;
        error = null;
        return true;
    }
    static bool FindSize(FunctionType type, out int size, [NotNullWhen(false)] out PossibleDiagnostic? error, IRuntimeInfoProvider runtime)
    {
        size = runtime.PointerSize;
        error = null;
        return true;
    }
    static bool FindSize(ArrayType type, out int size, [NotNullWhen(false)] out PossibleDiagnostic? error, IRuntimeInfoProvider runtime)
    {
        size = default;

        if (type.Length is null)
        {
            error = new PossibleDiagnostic($"Array type doesn't have a size");
            return false;
        }

        if (!FindSize(type.Of, out int elementSize, out error, runtime)) return false;

        size = elementSize * type.Length.Value;
        error = null;
        return true;
    }
    static bool FindSize(StructType type, out int size, [NotNullWhen(false)] out PossibleDiagnostic? error, IRuntimeInfoProvider runtime)
    {
        size = 0;

        foreach (CompiledField field in type.Struct.Fields)
        {
            GeneralType fieldType = type.ReplaceType(field.Type, out error);
            if (error is not null) return false;
            if (!FindSize(fieldType, out int fieldSize, out error, runtime)) return false;
            size += fieldSize;
        }

        error = null;
        return true;
    }
    static bool FindSize(GenericType type, out int size, [NotNullWhen(false)] out PossibleDiagnostic? error, IRuntimeInfoProvider runtime)
    {
        size = default;
        error = new PossibleDiagnostic($"Generic type doesn't have a size");
        return false;
    }
    static bool FindSize(BuiltinType type, out int size, [NotNullWhen(false)] out PossibleDiagnostic? error, IRuntimeInfoProvider runtime)
    {
        size = default;
        error = default;
        switch (type.Type)
        {
            case BasicType.Void: error = new PossibleDiagnostic($"Can't get the size of type \"{type}\""); return false;
            case BasicType.Any: error = new PossibleDiagnostic($"Can't get the size of type \"{type}\""); return false;
            case BasicType.U8: size = 1; return true;
            case BasicType.I8: size = 1; return true;
            case BasicType.U16: size = 2; return true;
            case BasicType.I16: size = 2; return true;
            case BasicType.U32: size = 4; return true;
            case BasicType.I32: size = 4; return true;
            case BasicType.U64: size = 8; return true;
            case BasicType.I64: size = 8; return true;
            case BasicType.F32: size = 4; return true;
            default: throw new UnreachableException();
        }
    }
    static bool FindSize(AliasType type, out int size, [NotNullWhen(false)] out PossibleDiagnostic? error, IRuntimeInfoProvider runtime)
    {
        return FindSize(type.Value, out size, out error, runtime);
    }
    static bool FindSize(EnumType type, out int size, [NotNullWhen(false)] out PossibleDiagnostic? error, IRuntimeInfoProvider runtime)
    {
        return FindSize(type.Definition.Type, out size, out error, runtime);
    }

    public static bool FindSize(CompiledTypeExpression type, out int size, [NotNullWhen(false)] out PossibleDiagnostic? error, IRuntimeInfoProvider runtime) => type switch
    {
        CompiledPointerTypeExpression v => FindSize(v, out size, out error, runtime),
        CompiledReferenceTypeExpression v => FindSize(v, out size, out error, runtime),
        CompiledArrayTypeExpression v => FindSize(v, out size, out error, runtime),
        CompiledFunctionTypeExpression v => FindSize(v, out size, out error, runtime),
        CompiledStructTypeExpression v => FindSize(v, out size, out error, runtime),
        CompiledGenericTypeExpression v => FindSize(v, out size, out error, runtime),
        CompiledBuiltinTypeExpression v => FindSize(v, out size, out error, runtime),
        CompiledAliasTypeExpression v => FindSize(v, out size, out error, runtime),
        CompiledEnumTypeExpression v => FindSize(v, out size, out error, runtime),
        _ => throw new NotImplementedException(),
    };
    static bool FindSize(CompiledPointerTypeExpression type, out int size, [NotNullWhen(false)] out PossibleDiagnostic? error, IRuntimeInfoProvider runtime)
    {
        size = runtime.PointerSize;
        error = null;
        return true;
    }
    static bool FindSize(CompiledReferenceTypeExpression type, out int size, [NotNullWhen(false)] out PossibleDiagnostic? error, IRuntimeInfoProvider runtime)
    {
        size = runtime.PointerSize;
        error = null;
        return true;
    }
    static bool FindSize(CompiledFunctionTypeExpression type, out int size, [NotNullWhen(false)] out PossibleDiagnostic? error, IRuntimeInfoProvider runtime)
    {
        size = runtime.PointerSize;
        error = null;
        return true;
    }
    static bool FindSize(CompiledArrayTypeExpression type, out int size, [NotNullWhen(false)] out PossibleDiagnostic? error, IRuntimeInfoProvider runtime)
    {
        size = default;

        if (type.Length is null)
        {
            error = new PossibleDiagnostic($"Array type doesn't have a size", type);
            return false;
        }

        if (!FindSize(type.Of, out int elementSize, out error, runtime)) return false;

        if (type.Length is not CompiledConstantValue evaluatedStatement)
        {
            error = new PossibleDiagnostic($"Can't compute the array type's length", type.Length);
            return false;
        }

        size = elementSize * (int)evaluatedStatement.Value;
        error = null;
        return true;
    }
    static bool FindSize(CompiledStructTypeExpression type, out int size, [NotNullWhen(false)] out PossibleDiagnostic? error, IRuntimeInfoProvider runtime)
    {
        size = 0;

        foreach (CompiledField field in type.Struct.Fields)
        {
            if (!FindSize(field.Type, out int fieldSize, out error, runtime)) return false;
            size += fieldSize;
        }

        error = null;
        return true;
    }
    static bool FindSize(CompiledGenericTypeExpression type, out int size, [NotNullWhen(false)] out PossibleDiagnostic? error, IRuntimeInfoProvider runtime)
    {
        size = default;
        error = new PossibleDiagnostic($"Generic type doesn't have a size", type);
        return false;
    }
    static bool FindSize(CompiledBuiltinTypeExpression type, out int size, [NotNullWhen(false)] out PossibleDiagnostic? error, IRuntimeInfoProvider runtime)
    {
        size = default;
        error = default;
        switch (type.Type)
        {
            case BasicType.Void: error = new PossibleDiagnostic($"Can't get the size of type \"{type}\"", type); return false;
            case BasicType.Any: error = new PossibleDiagnostic($"Can't get the size of type \"{type}\"", type); return false;
            case BasicType.U8: size = 1; return true;
            case BasicType.I8: size = 1; return true;
            case BasicType.U16: size = 2; return true;
            case BasicType.I16: size = 2; return true;
            case BasicType.U32: size = 4; return true;
            case BasicType.I32: size = 4; return true;
            case BasicType.U64: size = 8; return true;
            case BasicType.I64: size = 8; return true;
            case BasicType.F32: size = 4; return true;
            default: throw new UnreachableException();
        }
    }
    static bool FindSize(CompiledAliasTypeExpression type, out int size, [NotNullWhen(false)] out PossibleDiagnostic? error, IRuntimeInfoProvider runtime)
    {
        return FindSize(type.Value, out size, out error, runtime);
    }
    static bool FindSize(CompiledEnumTypeExpression type, out int size, [NotNullWhen(false)] out PossibleDiagnostic? error, IRuntimeInfoProvider runtime)
    {
        return FindSize(type.Definition.Type, out size, out error, runtime);
    }

    #endregion

    #region IsObservable

    [Flags]
    enum StatementComplexity
    {
        None = 0,
        Volatile = 1,
        Complex = 2,
        Bruh = 4,
    }

    StatementComplexity GetStatementComplexity(IEnumerable<CompiledExpression> statements)
    {
        StatementComplexity res = StatementComplexity.None;
        foreach (CompiledExpression statement in statements) res |= GetStatementComplexity(statement);
        return res;
    }

    StatementComplexity GetStatementComplexity(CompiledTypeExpression statement) => statement.FinalValue switch
    {
        CompiledArrayTypeExpression v => (v.Length is null || v.ComputedLength.HasValue) ? StatementComplexity.None : GetStatementComplexity(v.Length),
        CompiledBuiltinTypeExpression => StatementComplexity.None,
        CompiledFunctionTypeExpression v => GetStatementComplexity(v.ReturnType) | v.Parameters.Select(GetStatementComplexity).Aggregate(StatementComplexity.None, (a, b) => a | b),
        CompiledGenericTypeExpression => StatementComplexity.Bruh,
        CompiledPointerTypeExpression v => GetStatementComplexity(v.To),
        CompiledReferenceTypeExpression v => GetStatementComplexity(v.To),
        CompiledStructTypeExpression v => v.TypeArguments.Values.Select(GetStatementComplexity).Aggregate(StatementComplexity.None, (a, b) => a | b),
        _ => throw new NotImplementedException(statement.GetType().ToString()),
    };

    StatementComplexity GetStatementComplexity(CompiledExpression statement) => statement switch
    {
        CompiledSizeof v => GetStatementComplexity(v.Of),
        CompiledArgument v => GetStatementComplexity(v.Value) | ((v.Cleanup.Destructor is not null || v.Cleanup.Deallocator is not null) ? StatementComplexity.Complex | StatementComplexity.Volatile : StatementComplexity.None),
        CompiledBinaryOperatorCall v => GetStatementComplexity(v.Left) | GetStatementComplexity(v.Right) | StatementComplexity.Complex,
        CompiledUnaryOperatorCall v => GetStatementComplexity(v.Expression) | StatementComplexity.Complex,
        CompiledConstantValue => StatementComplexity.None,
        CompiledRegisterAccess => StatementComplexity.None,
        CompiledVariableAccess => StatementComplexity.None,
        CompiledExpressionVariableAccess => StatementComplexity.None,
        CompiledParameterAccess => StatementComplexity.None,
        CompiledFunctionReference => StatementComplexity.None,
        CompiledLabelReference => StatementComplexity.None,
        CompiledFieldAccess v => GetStatementComplexity(v.Object),
        CompiledElementAccess v => GetStatementComplexity(v.Base) | GetStatementComplexity(v.Index),
        CompiledGetReference v => GetStatementComplexity(v.Of),
        CompiledDereference v => GetStatementComplexity(v.Address),
        CompiledStackAllocation => StatementComplexity.Bruh,
        CompiledConstructorCall v => GetStatementComplexity(v.Arguments) | StatementComplexity.Complex | StatementComplexity.Volatile,
        CompiledCast v => GetStatementComplexity(v.Value) | StatementComplexity.Complex,
        CompiledReinterpretation v => GetStatementComplexity(v.Value),
        CompiledRuntimeCall v => GetStatementComplexity(v.Arguments) | StatementComplexity.Complex | StatementComplexity.Volatile,
        CompiledFunctionCall v => GetStatementComplexity(v.Arguments) | StatementComplexity.Complex | StatementComplexity.Volatile,
        CompiledExternalFunctionCall v => GetStatementComplexity(v.Arguments) | StatementComplexity.Volatile,
        CompiledDummyExpression => StatementComplexity.Bruh,
        CompiledString => StatementComplexity.Complex | StatementComplexity.Volatile,
        CompiledStackString => StatementComplexity.Bruh,
        _ => throw new NotImplementedException(statement.GetType().ToString()),
    };

    #endregion

    #region Visit

    static IEnumerable<CompiledStatement> Visit(IEnumerable<CompiledTypeExpression> type)
    {
        foreach (CompiledTypeExpression v in type)
        {
            foreach (CompiledStatement v2 in Visit(v)) yield return v2;
        }
    }

    static IEnumerable<CompiledStatement> Visit(CompiledTypeExpression? type)
    {
        switch (type)
        {
            case CompiledBuiltinTypeExpression:
                break;
            case CompiledAliasTypeExpression v:
                foreach (CompiledStatement v2 in Visit(v.Value)) yield return v2;
                break;
            case CompiledEnumTypeExpression:
                break;
            case CompiledPointerTypeExpression v:
                foreach (CompiledStatement v2 in Visit(v.To)) yield return v2;
                break;
            case CompiledReferenceTypeExpression v:
                foreach (CompiledStatement v2 in Visit(v.To)) yield return v2;
                break;
            case CompiledFunctionTypeExpression v:
                foreach (CompiledStatement v2 in Visit(v.ReturnType)) yield return v2;
                foreach (CompiledStatement v2 in Visit(v.Parameters)) yield return v2;
                break;
            case CompiledGenericTypeExpression:
                break;
            case CompiledStructTypeExpression v:
                foreach (CompiledStatement v2 in Visit(v.TypeArguments.Values)) yield return v2;
                break;
            case CompiledArrayTypeExpression v:
                foreach (CompiledStatement v2 in Visit(v.Of)) yield return v2;
                foreach (CompiledStatement v2 in Visit(v.Length)) yield return v2;
                break;
        }
    }

    public static IEnumerable<CompiledStatement> Visit(IEnumerable<CompiledStatement> type)
    {
        foreach (CompiledStatement v in type)
        {
            foreach (CompiledStatement v2 in Visit(v)) yield return v2;
        }
    }

    public static IEnumerable<CompiledStatement> Visit(CompiledStatement? statement)
    {
        switch (statement)
        {
            case CompiledVariableDefinition v:
                yield return v;
                if (v.InitialValue is not null)
                {
                    foreach (CompiledStatement v2 in Visit(v.InitialValue)) yield return v2;
                }
                foreach (CompiledStatement v2 in Visit(v.TypeExpression)) yield return v2;
                foreach (CompiledStatement v2 in Visit(v.Cleanup)) yield return v2;
                break;
            case CompiledSizeof v:
                yield return v;
                foreach (CompiledStatement v2 in Visit(v.Of)) yield return v2;
                break;
            case CompiledReturn v:
                yield return v;
                if (v.Value is not null)
                {
                    foreach (CompiledStatement v2 in Visit(v.Value)) yield return v2;
                }
                break;
            case CompiledCrash v:
                yield return v;
                foreach (CompiledStatement v2 in Visit(v.Value)) yield return v2;
                break;
            case CompiledBreak v:
                yield return v;
                break;
            case CompiledDelete v:
                yield return v;
                foreach (CompiledStatement v2 in Visit(v.Value)) yield return v2;
                foreach (CompiledStatement v2 in Visit(v.Cleanup)) yield return v2;
                break;
            case CompiledGoto v:
                yield return v;
                foreach (CompiledStatement v2 in Visit(v.Value)) yield return v2;
                break;
            case CompiledBinaryOperatorCall v:
                yield return v;
                foreach (CompiledStatement v2 in Visit(v.Left)) yield return v2;
                foreach (CompiledStatement v2 in Visit(v.Right)) yield return v2;
                break;
            case CompiledUnaryOperatorCall v:
                yield return v;
                foreach (CompiledStatement v2 in Visit(v.Expression)) yield return v2;
                break;
            case CompiledConstantValue v:
                yield return v;
                break;
            case CompiledRegisterAccess v:
                yield return v;
                break;
            case CompiledVariableAccess v:
                yield return v;
                break;
            case CompiledExpressionVariableAccess v:
                yield return v;
                break;
            case CompiledParameterAccess v:
                yield return v;
                break;
            case CompiledFunctionReference v:
                yield return v;
                break;
            case CompiledLabelReference v:
                yield return v;
                break;
            case CompiledFieldAccess v:
                yield return v;
                foreach (CompiledStatement v2 in Visit(v.Object)) yield return v2;
                break;
            case CompiledElementAccess v:
                yield return v;
                foreach (CompiledStatement v2 in Visit(v.Base)) yield return v2;
                foreach (CompiledStatement v2 in Visit(v.Index)) yield return v2;
                break;
            case CompiledGetReference v:
                yield return v;
                foreach (CompiledStatement v2 in Visit(v.Of)) yield return v2;
                break;
            case CompiledSetter v:
                yield return v;
                foreach (CompiledStatement v2 in Visit(v.Value)) yield return v2;
                foreach (CompiledStatement v2 in Visit(v.Target)) yield return v2;
                break;
            case CompiledDereference v:
                yield return v;
                foreach (CompiledStatement v2 in Visit(v.Address)) yield return v2;
                break;
            case CompiledWhileLoop v:
                yield return v;
                foreach (CompiledStatement v2 in Visit(v.Condition)) yield return v2;
                foreach (CompiledStatement v2 in Visit(v.Body)) yield return v2;
                break;
            case CompiledForLoop v:
                yield return v;
                foreach (CompiledStatement v2 in Visit(v.Initialization)) yield return v2;
                foreach (CompiledStatement v2 in Visit(v.Condition)) yield return v2;
                foreach (CompiledStatement v2 in Visit(v.Step)) yield return v2;
                foreach (CompiledStatement v2 in Visit(v.Body)) yield return v2;
                break;
            case CompiledIf v:
                yield return v;
                foreach (CompiledStatement v2 in Visit(v.Condition)) yield return v2;
                foreach (CompiledStatement v2 in Visit(v.Body)) yield return v2;
                foreach (CompiledStatement v2 in Visit(v.Next)) yield return v2;
                break;
            case CompiledElse v:
                yield return v;
                foreach (CompiledStatement v2 in Visit(v.Body)) yield return v2;
                break;
            case CompiledStackAllocation v:
                yield return v;
                foreach (CompiledStatement v2 in Visit(v.TypeExpression)) yield return v2;
                break;
            case CompiledConstructorCall v:
                yield return v;
                foreach (CompiledStatement v2 in Visit(v.Object)) yield return v2;
                foreach (CompiledStatement v2 in Visit(v.Arguments)) yield return v2;
                break;
            case CompiledCast v:
                yield return v;
                foreach (CompiledStatement v2 in Visit(v.TypeExpression)) yield return v2;
                foreach (CompiledStatement v2 in Visit(v.Value)) yield return v2;
                break;
            case CompiledReinterpretation v:
                yield return v;
                foreach (CompiledStatement v2 in Visit(v.Value)) yield return v2;
                break;
            case CompiledRuntimeCall v:
                yield return v;
                foreach (CompiledStatement v2 in Visit(v.Function)) yield return v2;
                foreach (CompiledStatement v2 in Visit(v.Arguments)) yield return v2;
                break;
            case CompiledFunctionCall v:
                yield return v;
                foreach (CompiledStatement v2 in Visit(v.Arguments)) yield return v2;
                break;
            case CompiledExternalFunctionCall v:
                yield return v;
                foreach (CompiledStatement v2 in Visit(v.Arguments)) yield return v2;
                break;
            case CompiledBlock v:
                yield return v;
                foreach (CompiledStatement v2 in Visit(v.Statements)) yield return v2;
                break;
            case CompiledLabelDeclaration v:
                yield return v;
                break;
            case CompiledDummyExpression v:
                yield return v;
                break;
            case CompiledString v:
                yield return v;
                foreach (CompiledStatement v2 in Visit(v.Allocator)) yield return v2;
                break;
            case CompiledStackString v:
                yield return v;
                break;
            case CompiledEmptyStatement v:
                yield return v;
                break;
            case CompiledArgument v:
                yield return v;
                foreach (CompiledStatement v2 in Visit(v.Value)) yield return v2;
                foreach (CompiledStatement v2 in Visit(v.Cleanup)) yield return v2;
                break;
            case CompiledCleanup v:
                yield return v;
                break;
            case null:
                break;
            default:
                throw new NotImplementedException($"Unimplemented statement \"{statement.GetType().Name}\"");
        }
    }

    #endregion

    static ImmutableArray<CompiledStatement> ReduceStatements<TStatement>(ImmutableArray<TStatement> statements, DiagnosticsCollection diagnostics, bool didNotify = false)
        where TStatement : CompiledStatement
    {
        ImmutableArray<CompiledStatement>.Builder result = ImmutableArray.CreateBuilder<CompiledStatement>();

        foreach (TStatement statement in statements)
        {
            result.AddRange(ReduceStatements(statement, diagnostics, didNotify));
        }

        return result.ToImmutable();
    }

    static ImmutableArray<CompiledStatement> ReduceStatements(CompiledStatement statement, DiagnosticsCollection diagnostics, bool didNotify = false)
    {
        if (statement is CompiledEmptyStatement)
        {
            return ImmutableArray<CompiledStatement>.Empty;
        }

        if (statement is CompiledBlock compiledBlock)
        {
            if (!compiledBlock.Statements.Any(v => v is CompiledVariableDefinition))
            {
                return ReduceStatements(compiledBlock.Statements, diagnostics, didNotify);
            }
            return ImmutableArray.Create(statement);
        }

        if (statement is not CompiledExpression statementWithValue)
        {
            return ImmutableArray.Create(statement);
        }

        if (statementWithValue
            is CompiledRuntimeCall
            or CompiledFunctionCall
            or CompiledExternalFunctionCall)
        {
            return ImmutableArray.Create(statement);
        }

        if (statementWithValue.SaveValue)
        {
            return ImmutableArray.Create(statement);
        }

        if (statementWithValue
            is CompiledSizeof
            or CompiledConstantValue
            or CompiledRegisterAccess
            or CompiledVariableAccess
            or CompiledExpressionVariableAccess
            or CompiledParameterAccess
            or CompiledFunctionReference
            or CompiledLabelReference
            or CompiledFieldAccess
            or CompiledStackAllocation
            or CompiledString
            or CompiledList
            or CompiledStackString)
        {
            if (!didNotify)
            {
                diagnostics.Add(DiagnosticAt.Warning($"Trimming unnecessary expression", statementWithValue));
                didNotify = true;
            }

            return ImmutableArray<CompiledStatement>.Empty;
        }

        return statementWithValue switch
        {
            CompiledArgument v => ReduceStatements(v.Value, diagnostics, didNotify),
            CompiledBinaryOperatorCall v => ReduceStatements(v.Left, diagnostics, didNotify).AddRange(ReduceStatements(v.Right, diagnostics, didNotify)),
            CompiledUnaryOperatorCall v => ReduceStatements(v.Expression, diagnostics, didNotify),
            CompiledElementAccess v => ReduceStatements(v.Base, diagnostics, didNotify).AddRange(ReduceStatements(v.Index, diagnostics, didNotify)),
            CompiledGetReference v => ReduceStatements(v.Of, diagnostics, didNotify),
            CompiledDereference v => ReduceStatements(v.Address, diagnostics, didNotify),
            CompiledConstructorCall v => ReduceStatements(v.Arguments, diagnostics, didNotify),
            CompiledCast v => ReduceStatements(v.Value, diagnostics, didNotify),
            CompiledReinterpretation v => ReduceStatements(v.Value, diagnostics, didNotify),
            CompiledDummyExpression v => ReduceStatements(v.Statement, diagnostics, didNotify),
            _ => throw new NotImplementedException(statementWithValue.GetType().Name),
        };
    }

    bool CompileType(TypeInstance type, [NotNullWhen(true)] out GeneralType? result, DiagnosticsCollection diagnostics)
    {
        if (!CompileStatement(type, out CompiledTypeExpression? typeExpression, diagnostics))
        {
            result = null;
            return false;
        }

        if (!CompileType(typeExpression, out result, out PossibleDiagnostic? typeError))
        {
            diagnostics.Add(typeError.ToError(type));
            result = null;
            return false;
        }

        SetStatementType(type, result);

        return true;
    }
    public static bool CompileType(CompiledTypeExpression typeExpression, [NotNullWhen(true)] out GeneralType? type, [NotNullWhen(false)] out PossibleDiagnostic? error, bool ignoreValues = false)
    {
        type = null;
        error = null;

        switch (typeExpression)
        {
            case CompiledAliasTypeExpression v:
            {
                if (!CompileType(v.Value, out GeneralType? aliasValue, out error, ignoreValues)) return false;
                type = new AliasType(aliasValue, v.Definition);
                return true;
            }
            case CompiledEnumTypeExpression v:
            {
                type = new EnumType(v.Definition);
                return true;
            }
            case CompiledArrayTypeExpression v:
            {
                if (!CompileType(v.Of, out GeneralType? ofType, out error, ignoreValues)) return false;
                if (v.Length is not null)
                {
                    static bool IsValidArrayLength(CompiledExpression expression, out int result, [NotNullWhen(false)] out PossibleDiagnostic? error)
                    {
                        result = default;
                        error = null;

                        if (expression is not CompiledConstantValue constantLength)
                        {
                            error = new PossibleDiagnostic($"Array type's length must be constant", expression);
                            return false;
                        }
                        if (constantLength.Value.Type == RuntimeType.F32)
                        {
                            error = new PossibleDiagnostic($"Array type's length cannot be a float", expression);
                            return false;
                        }
                        if (constantLength.Value.Type == RuntimeType.Null)
                        {
                            error = new PossibleDiagnostic($"Array type's length cannot be null", expression);
                            return false;
                        }
                        if (constantLength.Value > int.MaxValue)
                        {
                            error = new PossibleDiagnostic($"Array type's length cannot be more than {int.MaxValue}", expression);
                            return false;
                        }
                        if (constantLength.Value < 1)
                        {
                            error = new PossibleDiagnostic($"Array type's length cannot be less than {1}", expression);
                            return false;
                        }
                        if (constantLength.Value != (int)constantLength.Value)
                        {
                            error = new PossibleDiagnostic($"Invalid array length {constantLength.Value}", expression);
                            return false;
                        }

                        result = (int)constantLength.Value;
                        return true;
                    }

                    if (IsValidArrayLength(v.Length, out int length, out PossibleDiagnostic? lengthError))
                    {
                        type = new ArrayType(ofType, length);
                    }
                    else
                    {
                        if (ignoreValues)
                        {
                            type = new ArrayType(ofType, null);
                        }
                        else
                        {
                            error = lengthError;
                            return false;
                        }
                    }
                }
                else
                {
                    type = new ArrayType(ofType, null);
                }
                return true;
            }
            case CompiledBuiltinTypeExpression v:
            {
                type = new BuiltinType(v.Type);
                return true;
            }
            case CompiledFunctionTypeExpression v:
            {
                if (!CompileType(v.ReturnType, out GeneralType? returnType, out error, ignoreValues)) return false;
                GeneralType[] parameters = new GeneralType[v.Parameters.Length];
                for (int i = 0; i < v.Parameters.Length; i++)
                {
                    if (!CompileType(v.Parameters[i], out parameters[i]!, out error, ignoreValues)) return false;
                }
                type = new FunctionType(returnType, parameters.AsImmutableUnsafe(), v.HasClosure);
                return true;
            }
            case CompiledGenericTypeExpression v:
            {
                type = new GenericType(v.Identifier, v.File);
                return true;
            }
            case CompiledPointerTypeExpression v:
            {
                if (!CompileType(v.To, out GeneralType? toType, out error, ignoreValues)) return false;
                type = new PointerType(toType);
                return true;
            }
            case CompiledReferenceTypeExpression v:
            {
                if (!CompileType(v.To, out GeneralType? toType, out error, ignoreValues)) return false;
                type = new ReferenceType(toType);
                return true;
            }
            case CompiledStructTypeExpression v:
            {
                Dictionary<string, GeneralType> typeArguments = new(v.TypeArguments.Count);
                foreach (KeyValuePair<string, CompiledTypeExpression> item in v.TypeArguments)
                {
                    if (!CompileType(item.Value, out GeneralType? itemV, out error, ignoreValues)) return false;
                    typeArguments.Add(item.Key, itemV);
                }
                type = new StructType(v.Struct, v.File, typeArguments.ToImmutableDictionary());
                return true;
            }
            default: throw new UnreachableException();
        }
    }
}
