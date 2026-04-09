using LanguageCore.Parser;
using LanguageCore.Parser.Statements;
using LanguageCore.Runtime;
using LanguageCore.Tokenizing;

namespace LanguageCore.Compiler;

public partial class StatementCompiler
{
    bool CompileArguments(IReadOnlyList<ArgumentExpression> arguments, ICompiledFunctionDefinition compiledFunction, ImmutableDictionary<string, GeneralType>? typeArguments, [NotNullWhen(true)] out ImmutableArray<CompiledArgument> compiledArguments, int alreadyPassed = 0)
    {
        compiledArguments = ImmutableArray<CompiledArgument>.Empty;

        ImmutableArray<CompiledArgument>.Builder result = ImmutableArray.CreateBuilder<CompiledArgument>(arguments.Count);

        // int partial = 0;
        // for (int i = 0; i < compiledFunction.Parameters.Count; i++)
        // {
        //     if (compiledFunction.Parameters[i].DefaultValue is null) partial = i + 1;
        //     else break;
        // }

        // todo:
        // if (arguments.Count < partial)
        // {
        //     Diagnostics.Add(Diagnostic.Error($"Wrong number of arguments passed to function \"{callee.ToReadable()}\": required {compiledFunction.ParameterCount} passed {arguments.Count}", caller));
        //     return false;
        // }

        // todo: A hint if the passed value is the same as the default value

        for (int i = 0; i < arguments.Count; i++)
        {
            CompiledParameter parameter = compiledFunction.Parameters[i + alreadyPassed];
            ArgumentExpression argument = arguments[i];
            GeneralType parameterType = GeneralType.TryInsertTypeParameters(parameter.Type, typeArguments);

            if (!CompileExpression(argument, out CompiledExpression? compiledArgument, parameterType)) return false;

            if (parameterType.Is<PointerType>() &&
                parameter.Definition.Modifiers.Any(v => v.Content == ModifierKeywords.This) &&
                !compiledArgument.Type.Is<PointerType>())
            {
                if (!CompileExpression(new GetReferenceExpression(
                    Token.CreateAnonymous("&", TokenType.Operator, argument.Position.Before()),
                    argument,
                    argument.File
                ), out compiledArgument, parameterType))
                { return false; }
            }

            if (!CanCastImplicitly(compiledArgument, parameterType, out CompiledExpression? assignedArgument, out PossibleDiagnostic? error))
            { Diagnostics.Add(error.ToError(argument)); }
            compiledArgument = assignedArgument;

            if (!FindSize(compiledArgument.Type, out int argumentSize, out PossibleDiagnostic? argumentSizeError, this))
            { Diagnostics.Add(argumentSizeError.ToError(compiledArgument)); }
            else if (!FindSize(parameterType, out int parameterSize, out PossibleDiagnostic? parameterSizeError, this))
            { Diagnostics.Add(parameterSizeError.ToError(parameter.Definition)); }
            else if (argumentSize != parameterSize)
            { Diagnostics.Add(DiagnosticAt.Internal($"Bad argument type passed: expected \"{parameterType}\" ({parameterSize} bytes) passed \"{compiledArgument.Type}\" ({argumentSize} bytes)", argument)); }

            bool typeAllowsTemp = AllowDeallocate(compiledArgument.Type);

            bool calleeAllowsTemp = parameter.Definition.Modifiers.Contains(ModifierKeywords.Temp);

            bool callerAllowsTemp = StatementCanBeDeallocated(argument, out bool explicitDeallocate);

            if (callerAllowsTemp)
            {
                if (explicitDeallocate && !calleeAllowsTemp)
                { Diagnostics.Add(DiagnosticAt.Warning($"Can not deallocate this value: parameter definition does not have a \"{ModifierKeywords.Temp}\" modifier", argument)); }
                if (explicitDeallocate && !typeAllowsTemp)
                { Diagnostics.Add(DiagnosticAt.Warning($"Can not deallocate this type", argument)); }
            }
            else
            {
                if (explicitDeallocate)
                { Diagnostics.Add(DiagnosticAt.Warning($"Can not deallocate this value", argument)); }
            }

            CompiledCleanup? compiledCleanup = null;
            if (calleeAllowsTemp && callerAllowsTemp && typeAllowsTemp)
            {
                CompileCleanup(compiledArgument.Type, argument.Location, out compiledCleanup);
            }

            result.Add(new CompiledArgument()
            {
                Value = compiledArgument,
                Type = compiledArgument.Type,
                Cleanup = compiledCleanup ?? new CompiledCleanup()
                {
                    Location = compiledArgument.Location,
                    TrashType = compiledArgument.Type,
                },
                Location = compiledArgument.Location,
                SaveValue = compiledArgument.SaveValue,
            });
        }

        int remaining = compiledFunction.Parameters.Length - arguments.Count - alreadyPassed;

        ImmutableArray<Scope> savedScopes = Frames.Last.Scopes.ToImmutableArray();
        Frames.Last.Scopes.Clear();
        try
        {
            for (int i = 0; i < remaining; i++)
            {
                CompiledParameter parameter = compiledFunction.Parameters[arguments.Count + i + alreadyPassed];
                Expression? argument = parameter.Definition.DefaultValue;
                GeneralType parameterType = GeneralType.TryInsertTypeParameters(parameter.Type, typeArguments);

                if (argument is null)
                {
                    Diagnostics.Add(DiagnosticAt.Internal($"Can't explain this error", parameter.Definition));
                    return false;
                }
                else
                {
                    Diagnostics.Add(DiagnosticAt.Warning($"WIP", argument));
                }

                if (!CompileExpression(argument, out CompiledExpression? compiledArgument, parameterType)) return false;

                if (!CanCastImplicitly(compiledArgument, parameterType, out CompiledExpression? assignedArgument, out PossibleDiagnostic? error))
                { Diagnostics.Add(error.ToError(argument)); }
                compiledArgument = assignedArgument;

                if (!FindSize(compiledArgument.Type, out int argumentSize, out PossibleDiagnostic? argumentSizeError, this))
                { Diagnostics.Add(argumentSizeError.ToError(compiledArgument)); }
                else if (!FindSize(parameterType, out int parameterSize, out PossibleDiagnostic? parameterSizeError, this))
                { Diagnostics.Add(parameterSizeError.ToError(parameter.Definition)); }
                else if (argumentSize != parameterSize)
                { Diagnostics.Add(DiagnosticAt.Internal($"Bad argument type passed: expected \"{parameterType}\" ({parameterSize} bytes) passed \"{compiledArgument.Type}\" ({argumentSize} bytes)", argument)); }

                bool typeAllowsTemp = AllowDeallocate(compiledArgument.Type);

                bool calleeAllowsTemp = parameter.Definition.Modifiers.Contains(ModifierKeywords.Temp);

                bool callerAllowsTemp = StatementCanBeDeallocated(ArgumentExpression.Wrap(argument), out bool explicitDeallocate);

                if (callerAllowsTemp)
                {
                    if (explicitDeallocate && !calleeAllowsTemp)
                    { Diagnostics.Add(DiagnosticAt.Warning($"Can not deallocate this value: parameter definition does not have a \"{ModifierKeywords.Temp}\" modifier", argument)); }
                    if (explicitDeallocate && !typeAllowsTemp)
                    { Diagnostics.Add(DiagnosticAt.Warning($"Can not deallocate this type", argument)); }
                }
                else
                {
                    if (explicitDeallocate)
                    { Diagnostics.Add(DiagnosticAt.Warning($"Can not deallocate this value", argument)); }
                }

                CompiledCleanup? compiledCleanup = null;
                if (calleeAllowsTemp && callerAllowsTemp && typeAllowsTemp)
                {
                    CompileCleanup(compiledArgument.Type, argument.Location, out compiledCleanup);
                }

                result.Add(new CompiledArgument()
                {
                    Value = compiledArgument,
                    Type = compiledArgument.Type,
                    Cleanup = compiledCleanup ?? new CompiledCleanup()
                    {
                        Location = compiledArgument.Location,
                        TrashType = compiledArgument.Type,
                    },
                    Location = compiledArgument.Location,
                    SaveValue = compiledArgument.SaveValue,
                });
            }
        }
        finally
        {
            Frames.Last.Scopes.AddRange(savedScopes);
        }

        compiledArguments = result.ToImmutable();
        return true;
    }
    bool CompileArguments(IReadOnlyList<Expression> arguments, FunctionType function, [NotNullWhen(true)] out ImmutableArray<CompiledArgument> compiledArguments)
    {
        compiledArguments = ImmutableArray<CompiledArgument>.Empty;

        ImmutableArray<CompiledArgument>.Builder result = ImmutableArray.CreateBuilder<CompiledArgument>(arguments.Count);

        for (int i = 0; i < arguments.Count; i++)
        {
            Expression argument = arguments[i];
            GeneralType parameterType = function.Parameters[i];

            if (!CompileExpression(argument, out CompiledExpression? compiledArgument, parameterType)) return false;

            if (!CanCastImplicitly(compiledArgument, parameterType, out CompiledExpression? assignedArgument, out PossibleDiagnostic? error))
            { Diagnostics.Add(error.ToError(compiledArgument)); }
            compiledArgument = assignedArgument;

            bool canDeallocate = true; // temp type maybe?

            canDeallocate = canDeallocate && AllowDeallocate(compiledArgument.Type);

            if (StatementCanBeDeallocated(ArgumentExpression.Wrap(argument), out bool explicitDeallocate))
            {
                if (explicitDeallocate && !canDeallocate)
                { Diagnostics.Add(DiagnosticAt.Warning($"Can not deallocate this value: parameter definition does not have a \"{ModifierKeywords.Temp}\" modifier", argument)); }
            }
            else
            {
                if (explicitDeallocate)
                { Diagnostics.Add(DiagnosticAt.Warning($"Can not deallocate this value", compiledArgument)); }
                canDeallocate = false;
            }

            CompiledCleanup? compiledCleanup = null;
            if (canDeallocate)
            {
                CompileCleanup(compiledArgument.Type, compiledArgument.Location, out compiledCleanup);
            }

            result.Add(new CompiledArgument()
            {
                Value = compiledArgument,
                Type = compiledArgument.Type,
                Cleanup = compiledCleanup ?? new CompiledCleanup()
                {
                    Location = compiledArgument.Location,
                    TrashType = compiledArgument.Type,
                },
                Location = compiledArgument.Location,
                SaveValue = compiledArgument.SaveValue,
            });
        }

        compiledArguments = result.ToImmutable();
        return true;
    }

    bool CompileFunctionCall_External<TFunction>(ImmutableArray<CompiledArgument> arguments, bool saveValue, TFunction compiledFunction, IExternalFunction externalFunction, Location location, [NotNullWhen(true)] out CompiledExpression? compiledStatement)
        where TFunction : ICompiledFunctionDefinition, ICompiledDefinition<FunctionThingDefinition>
    {
        //CheckExternalFunctionDeclaration(this, compiledFunction, externalFunction, Diagnostics);

        compiledStatement = new CompiledExternalFunctionCall()
        {
            Function = externalFunction,
            Arguments = arguments,
            Type = compiledFunction.Type,
            Location = location,
            SaveValue = saveValue,
            Declaration = compiledFunction,
        };
        return true;
    }
    bool CompileFunctionCall<TFunction>(Expression caller, ImmutableArray<ArgumentExpression> arguments, FunctionQueryResult<TFunction> _callee, [NotNullWhen(true)] out CompiledExpression? compiledStatement)
        where TFunction : ICompiledFunctionDefinition, IExternalFunctionDefinition, ICompiledDefinition<FunctionThingDefinition>
    {
        (TFunction callee, ImmutableDictionary<string, GeneralType>? typeArguments) = _callee;
        _callee.ReplaceArgumentsIfNeeded(ref arguments);

        if (_callee.Function.Definition.Attributes.Any(v => v.Identifier.Content == AttributeConstants.MSILIncompatibleIdentifier))
        {
            Frames.LastRef.IsMsilCompatible = false;
        }
        Frames.Last.CapturesGlobalVariables = null;

        if (GeneralType.TryInsertTypeParameters(callee.Type, typeArguments).Is(out StructType? returnStructType) &&
            Utils.ReferenceEquals(returnStructType.Struct, GeneratorStructDefinition?.Struct))
        {
            if (!CompileAllocation(CompiledTypeExpression.CreateAnonymous(new StructType(GetGeneratorState(callee.Definition).Struct, caller.File), caller), out CompiledExpression? allocation))
            {
                compiledStatement = null;
                return false;
            }

            TemplateInstance<CompiledConstructorDefinition> compliableTemplate = AddCompilable(new TemplateInstance<CompiledConstructorDefinition>(
                GeneratorStructDefinition!.Constructor,
                returnStructType.TypeArguments
            ));

            FunctionType calleeFunctionType = new(GeneralType.TryInsertTypeParameters(callee.Type, typeArguments), callee.Parameters.ToImmutableArray(v => GeneralType.TryInsertTypeParameters(v.Type, typeArguments)), false);

            compiledStatement = new CompiledConstructorCall()
            {
                Object = new CompiledStackAllocation()
                {
                    TypeExpression = CompiledTypeExpression.CreateAnonymous(GeneralType.TryInsertTypeParameters(callee.Type, typeArguments), caller.Location),
                    Type = GeneralType.TryInsertTypeParameters(callee.Type, typeArguments),
                    Location = caller.Location,
                    SaveValue = true,
                },
                Function = compliableTemplate,
                Arguments = ImmutableArray.Create(
                    new CompiledArgument()
                    {
                        Value = new CompiledFunctionReference()
                        {
                            Function = TemplateInstance.New<ICompiledFunctionDefinition>(callee, typeArguments),
                            Type = calleeFunctionType,
                            Location = caller.Location,
                            SaveValue = true,
                        },
                        Cleanup = new CompiledCleanup()
                        {
                            Location = callee.Location,
                            TrashType = calleeFunctionType,
                        },
                        Type = calleeFunctionType,
                        Location = caller.Location,
                        SaveValue = true,
                    },
                    new CompiledArgument()
                    {
                        Value = new CompiledReinterpretation()
                        {
                            Value = allocation,
                            TypeExpression = CompiledTypeExpression.CreateAnonymous(PointerType.Any, caller.Location),
                            Type = PointerType.Any,
                            Location = caller.Location,
                            SaveValue = true,
                        },
                        Cleanup = new CompiledCleanup()
                        {
                            Location = callee.Location,
                            TrashType = PointerType.Any,
                        },
                        Type = PointerType.Any,
                        Location = caller.Location,
                        SaveValue = true,
                    }
                ),
                Type = GeneralType.TryInsertTypeParameters(callee.Type, typeArguments),
                Location = caller.Location,
                SaveValue = caller.SaveValue,
            };
            GeneratorStructDefinition.Constructor.AddReference(
                new ConstructorCallExpression(
                    Token.CreateAnonymous(StatementKeywords.New),
                    new TypeInstanceSimple(
                        Token.CreateAnonymous(GeneratorStructDefinition.Struct.Identifier),
                        caller.Location.File
                    ),
                    ArgumentListExpression.CreateAnonymous(caller.File),
                    caller.File
                ),
                caller.Location
            );

            return true;
        }

        compiledStatement = null;
        SetStatementType(caller, GeneralType.TryInsertTypeParameters(callee.Type, typeArguments));

        if (!callee.Definition.CanUse(caller.File))
        {
            Diagnostics.Add(DiagnosticAt.Error($"Function \"{callee.ToReadable()}\" could not be called due to its protection level", caller)
                .WithRelatedInfo(new DiagnosticRelatedInformationAt($"Function \"{callee.ToReadable()}\" defined here", callee.Location)));
            return false;
        }

        int partial = 0;
        for (int i = 0; i < callee.Definition.Parameters.Length; i++)
        {
            if (callee.Definition.Parameters[i].DefaultValue is null) partial = i + 1;
            else break;
        }

        if (arguments.Length < partial)
        {
            Diagnostics.Add(DiagnosticAt.Error($"Wrong number of arguments passed to function \"{callee.ToReadable()}\": required {callee.Definition.ParameterCount} passed {arguments.Length}", caller));
            return false;
        }

        if (!CompileArguments(arguments, callee, typeArguments, out ImmutableArray<CompiledArgument> compiledArguments)) return false;

        if (callee is IInContext<CompiledStruct> calleeInContext &&
            calleeInContext.Context is not null &&
            Utils.ReferenceEquals(calleeInContext.Context, GeneratorStructDefinition?.Struct))
        {
            CompiledGeneratorState stateStruct = GetGeneratorState(callee.Definition);
            List<CompiledArgument> modifiedArguments = new(compiledArguments.Length)
            {
                new()
                {
                    Value = new CompiledFieldAccess()
                    {
                        Field = GeneratorStructDefinition!.StateField,
                        Object = compiledArguments[0].Value,
                        Type = GeneratorStructDefinition.StateField.Type,
                        Location = caller.Location,
                        SaveValue = true,
                    },
                    Cleanup = new CompiledCleanup()
                    {
                        Location = caller.Location,
                        TrashType = GeneratorStructDefinition.StateField.Type,
                    },
                    Location = caller.Location,
                    SaveValue = true,
                    Type = GeneratorStructDefinition.StateField.Type,
                }
            };
            modifiedArguments.AddRange(compiledArguments[1..]);
            compiledStatement = new CompiledRuntimeCall()
            {
                Function = new CompiledFieldAccess()
                {
                    Field = GeneratorStructDefinition.FunctionField,
                    Object = compiledArguments[0].Value,
                    // fixme: dirty ahh
                    Type = GeneralType.InsertTypeParameters(GeneratorStructDefinition.FunctionField.Type, ((StructType)((PointerType)callee.Parameters[0].Type).To).TypeArguments),
                    Location = caller.Location,
                    SaveValue = true,
                },
                Arguments = modifiedArguments.ToImmutableArray(),
                Location = callee.Location,
                SaveValue = caller.SaveValue,
                Type = GeneralType.TryInsertTypeParameters(callee.Type, typeArguments),
            };
            //Debugger.Break();
            return true;
        }

        if (callee.ExternalFunctionName is not null)
        {
            if (ExternalFunctions.TryGet(callee.ExternalFunctionName, out IExternalFunction? externalFunction, out PossibleDiagnostic? exception))
            {
                return CompileFunctionCall_External(compiledArguments, caller.SaveValue, callee, externalFunction, caller.Location, out compiledStatement);
            }

            if (callee.Definition.Block is null)
            {
                Diagnostics.Add(exception.ToError(caller));
                return false;
            }
        }

        CompileFunction(callee, typeArguments);

        if ((Settings.Optimizations.HasFlag(OptimizationSettings.FunctionEvaluating) || Settings.OptimizationDiagnostics) &&
            TryEvaluate(callee, compiledArguments, new EvaluationContext(), out CompiledValue? returnValue, out ImmutableArray<RuntimeStatement2> runtimeStatements) &&
            returnValue.HasValue &&
            runtimeStatements.Length == 0)
        {
            SetPredictedValue(caller, returnValue.Value);
            Diagnostics.Add(DiagnosticAt.OptimizationNotice($"Function evaluated with result \"{returnValue.Value}\"", caller));
            if (Settings.Optimizations.HasFlag(OptimizationSettings.FunctionEvaluating))
            {
                compiledStatement = new CompiledConstantValue()
                {
                    Value = returnValue.Value,
                    Location = caller.Location,
                    SaveValue = caller.SaveValue,
                    Type = GeneralType.TryInsertTypeParameters(callee.Type, typeArguments),
                };
                return true;
            }
        }

        if ((Settings.Optimizations.HasFlag(OptimizationSettings.FunctionInlining) || Settings.OptimizationDiagnostics) &&
            callee.Definition.IsInline)
        {
            CompiledFunction? f = GeneratedFunctions.FirstOrDefault(v => Utils.ReferenceEquals(v.Function, callee) && TypeArgumentsEquals(v.TypeArguments, typeArguments));
            if (f is not null)
            {
                InlineContext inlineContext = new()
                {
                    Arguments = f.Function.Parameters
                        .Select((value, index) => (value.Identifier, compiledArguments[index]))
                        .ToImmutableDictionary(v => v.Identifier, v => v.Item2),
                };

                if (InlineFunction(f.Body, inlineContext, out CompiledStatement? inlined1, out DiagnosticAt? inlineError))
                {
                    {
                        ImmutableArray<CompiledArgument> volatileArguments =
                            compiledArguments
                            .Where(v => GetStatementComplexity(v.Value).HasFlag(StatementComplexity.Volatile))
                            .ToImmutableArray();
                        int i = 0;
                        foreach (CompiledArgument? item in volatileArguments)
                        {
                            for (int j = i; j < inlineContext.InlinedArguments.Count; j++)
                            {
                                if (Utils.ReferenceEquals(inlineContext.InlinedArguments[j], item))
                                {
                                    i = j;
                                    goto ok;
                                }
                            }
                            Debugger.Break();
                            Diagnostics.Add(DiagnosticAt.FailedOptimization($"Can't inline \"{callee.ToReadable()}\" because the behavior might change", item));
                            goto bad;
                        ok:;
                        }
                    }

                    foreach (CompiledArgument argument in compiledArguments)
                    {
                        StatementComplexity complexity = GetStatementComplexity(argument.Value);

                        if (complexity.HasFlag(StatementComplexity.Bruh))
                        {
                            Debugger.Break();
                            Diagnostics.Add(DiagnosticAt.FailedOptimization($"Can't inline \"{callee.ToReadable()}\" because of this argument", argument));
                            goto bad;
                        }

                        if (complexity.HasFlag(StatementComplexity.Complex))
                        {
                            if (inlineContext.InlinedArguments.Count(v => Utils.ReferenceEquals(v, argument)) > 1)
                            {
                                //Debugger.Break();
                                Diagnostics.Add(DiagnosticAt.FailedOptimization($"Can't inline \"{callee.ToReadable()}\" because this expression might be complex", argument));
                                goto bad;
                            }
                        }
                    }

                    ControlFlowUsage controlFlowUsage = inlined1 is CompiledBlock _block2 ? FindControlFlowUsage(_block2.Statements) : FindControlFlowUsage(inlined1);
                    if (!callee.ReturnSomething &&
                        controlFlowUsage == ControlFlowUsage.None)
                    {
                        Diagnostics.Add(DiagnosticAt.OptimizationNotice($"Function inlined", caller));
                        if (Settings.Optimizations.HasFlag(OptimizationSettings.FunctionInlining))
                        {
                            compiledStatement = new CompiledDummyExpression()
                            {
                                Statement = inlined1,
                                Location = inlined1.Location,
                                SaveValue = false,
                                Type = BuiltinType.Void,
                            };
                            return true;
                        }
                    }
                    else if (callee.ReturnSomething &&
                             controlFlowUsage == ControlFlowUsage.None &&
                             inlined1 is CompiledExpression statementWithValue)
                    {
                        Diagnostics.Add(DiagnosticAt.OptimizationNotice($"Function inlined", caller));

                        if (!CanCastImplicitly(statementWithValue.Type, GeneralType.TryInsertTypeParameters(callee.Type, typeArguments), out PossibleDiagnostic? castError))
                        { Diagnostics.Add(castError.ToError(statementWithValue)); }

                        if (Settings.Optimizations.HasFlag(OptimizationSettings.FunctionInlining))
                        {
                            statementWithValue.SaveValue = caller.SaveValue;
                            compiledStatement = statementWithValue;
                            return true;
                        }
                    }
                    else
                    {
                        Debugger.Break();
                        Diagnostics.Add(DiagnosticAt.Warning($"Unknown function inline scenario", caller));
                    }

                bad:;
                }
                else
                {
                    // Debugger.Break();
                    //InlineFunction(f.Body, new InlineContext()
                    //{
                    //    Arguments = f.Function.Parameters
                    //        .Select((value, index) => (value.Identifier.Content, compiledArguments[index]))
                    //        .ToImmutableDictionary(v => v.Content, v => v.Item2),
                    //}, out inlined1);
                    //Diagnostics.Add(DiagnosticAt.Warning($"Failed to inline \"{callee.ToReadable()}\"", caller).WithSuberrors(inlineError));
                }
            }
            else
            {
                Diagnostics.Add(DiagnosticAt.FailedOptimization($"Can't inline \"{callee.ToReadable()}\" because of an internal error", caller));
            }
        }

        compiledStatement = new CompiledFunctionCall()
        {
            Arguments = compiledArguments,
            Function = TemplateInstance.New<ICompiledFunctionDefinition>(callee, typeArguments),
            Location = caller.Location,
            Type = GeneralType.TryInsertTypeParameters(callee.Type, typeArguments),
            SaveValue = caller.SaveValue,
        };
        return true;
    }

    bool CompileExpression(AnyCallExpression anyCall, [NotNullWhen(true)] out CompiledExpression? compiledStatement, GeneralType? expectedType = null)
    {
        compiledStatement = null;

        if (anyCall.Expression is IdentifierExpression _identifier &&
            _identifier.Content == StatementKeywords.Sizeof)
        {
            _identifier.AnalyzedType = TokenAnalyzedType.Keyword;

            if (anyCall.Arguments.Arguments.Length != 1)
            {
                Diagnostics.Add(DiagnosticAt.Error($"Wrong number of arguments passed to \"sizeof\": required {1} passed {anyCall.Arguments.Arguments.Length}", anyCall));
                return false;
            }

            Expression argument = anyCall.Arguments.Arguments[0];
            CompiledTypeExpression? paramType;
            if (argument is ArgumentExpression argumentExpression
                && argumentExpression.Modifier is null)
            {
                argument = argumentExpression.Value;
            }

            if (argument is IdentifierExpression identifier)
            {
                if (FindType(identifier.Identifier, identifier.File, out paramType, out PossibleDiagnostic? typeError))
                {
                    //SetStatementType(identifier, paramType);
                    //paramType = _paramType;
                }
                else
                {
                    Diagnostics.Add(typeError.ToError(identifier));
                    return false;
                }
            }
            else
            {
                Diagnostics.Add(DiagnosticAt.Error($"Type \"{argument}\" not found", argument));
                return false;
            }

            GeneralType resultType = SizeofStatementType;
            if (expectedType is not null &&
                CanCastImplicitly(resultType, expectedType, out _))
            {
                resultType = expectedType;
            }

            SetStatementType(anyCall, resultType);

            compiledStatement = new CompiledSizeof()
            {
                Of = paramType,
                Location = argument.Location,
                Type = resultType,
                SaveValue = anyCall.SaveValue,
            };
            return true;
        }

        if (anyCall.ToFunctionCall(out FunctionCallExpression? functionCall))
        {
            if (GetFunction(functionCall, out FunctionQueryResult<CompiledFunctionDefinition>? result, out PossibleDiagnostic? notFound, AddCompilable))
            {
                if (anyCall.Expression is IdentifierExpression _identifier2)
                { _identifier2.AnalyzedType = TokenAnalyzedType.FunctionName; }

                if (anyCall.Expression is FieldExpression _field)
                { _field.Identifier.AnalyzedType = TokenAnalyzedType.FunctionName; }

                SetStatementReference(anyCall, result.Function);
                TrySetStatementReference(anyCall.Expression, result);
                SetStatementType(anyCall, result.Function.Type);

                result.Function.AddReference(anyCall, anyCall.Location);

                return CompileFunctionCall(functionCall, functionCall.MethodArguments, result, out compiledStatement);
            }
        }

        if (!CompileExpression(anyCall.Expression, out CompiledExpression? functionValue))
        {
            Diagnostics.Add(DiagnosticAt.Error("Function not found", anyCall.Expression));
            return false;
        }

        {
            List<ArgumentExpression> arguments = new();
            arguments.Add(ArgumentExpression.Wrap(anyCall.Expression));
            arguments.AddRange(anyCall.Arguments.Arguments);
            if (GetFunction(
                    GetOperators(),
                    "operator",
                    null,

                    FunctionQuery.Create<CompiledOperatorDefinition, string, string>(
                        "()",
                        arguments.ToImmutableArray(),
                        FunctionArgumentConverter,
                        anyCall.File,
                        null,
                        AddCompilable),

                    out FunctionQueryResult<CompiledOperatorDefinition>? res1,
                    out PossibleDiagnostic? err1
                ) && res1.Success)
            {
                CompiledOperatorDefinition compiledFunction = res1.Function;
                compiledFunction.AddReference(anyCall);
                return CompileFunctionCall(anyCall, arguments.ToImmutableArray(), res1, out compiledStatement);
            }
        }

        if (!functionValue.Type.Is(out FunctionType? functionType))
        {
            Diagnostics.Add(DiagnosticAt.Error($"This isn't a function", anyCall.Expression));
            return false;
        }

        SetStatementType(anyCall, functionType.ReturnType);

        if (anyCall.Arguments.Arguments.Length != functionType.Parameters.Length)
        {
            Diagnostics.Add(DiagnosticAt.Error($"Wrong number of arguments passed to function \"{functionType}\": required {functionType.Parameters.Length} passed {anyCall.Arguments.Arguments.Length}", new Position(anyCall.Arguments.Arguments.As<IPositioned>().DefaultIfEmpty(anyCall.Arguments.Brackets)), anyCall.File));
            return false;
        }

        if (!CompileArguments(anyCall.Arguments.Arguments, functionType, out ImmutableArray<CompiledArgument> compiledArguments)) return false;

        PossibleDiagnostic? argumentError = null;
        if (!Utils.SequenceEquals(compiledArguments, functionType.Parameters, (argument, parameter) =>
        {
            if (argument.Type.SameAs(parameter))
            { return true; }

            if (CanCastImplicitly(argument.Type, parameter, out argumentError))
            { return true; }

            argumentError = argumentError.TrySetLocation(argument);

            return false;
        }))
        {
            Diagnostics.Add(DiagnosticAt.Error($"Argument types of caller \"...({string.Join(", ", compiledArguments.Select(v => v.Type))})\" doesn't match with callee \"{functionType}\"", anyCall).WithSuberrors(argumentError?.ToError(anyCall)));
            return false;
        }

        Frames.Last.CapturesGlobalVariables = null;
        compiledStatement = new CompiledRuntimeCall()
        {
            Function = functionValue,
            Arguments = compiledArguments,
            Location = anyCall.Location,
            SaveValue = anyCall.SaveValue,
            Type = functionType.ReturnType,
        };
        return true;
    }
    bool CompileExpression(BinaryOperatorCallExpression @operator, [NotNullWhen(true)] out CompiledExpression? compiledStatement, GeneralType? expectedType = null)
    {
        compiledStatement = null;

        if (GetOperator(@operator, @operator.File, out FunctionQueryResult<CompiledOperatorDefinition>? result, out PossibleDiagnostic? notFoundError))
        {
            @operator.Operator.AnalyzedType = TokenAnalyzedType.FunctionName;
            SetStatementReference(@operator, result.Function);

            if (result.DidReplaceArguments) throw new UnreachableException();

            CompiledOperatorDefinition? operatorDefinition = result.Function;

            if (operatorDefinition.Definition.Attributes.Any(v => v.Identifier.Content == AttributeConstants.MSILIncompatibleIdentifier))
            {
                Frames.LastRef.IsMsilCompatible = false;
            }

            SetStatementType(@operator, operatorDefinition.Type);

            if (!CompileFunctionCall(@operator, @operator.Arguments.ToImmutableArray(ArgumentExpression.Wrap), result, out compiledStatement)) return false;

            return true;
        }
        else if (LanguageOperators.BinaryOperators.Contains(@operator.Operator.Content))
        {
            if (@operator.Operator.Content is
                "==" or "!=" or "<" or ">" or "<=" or ">=")
            {
                expectedType = null;
            }

            Expression left = @operator.Left;
            Expression right = @operator.Right;

            if (!CompileExpression(left, out CompiledExpression? compiledLeft, expectedType)) return false;
            if (!CompileExpression(right, out CompiledExpression? compiledRight, expectedType)) return false;

            GeneralType leftType = compiledLeft.Type;
            GeneralType rightType = compiledRight.Type;

            if (leftType.Is(out ReferenceType? lr))
            {
                leftType = lr.To;
            }
            else if (rightType.Is(out ReferenceType? rr))
            {
                rightType = rr.To;
            }

            if (!leftType.TryGetNumericType(out NumericType leftNType) ||
                !rightType.TryGetNumericType(out NumericType rightNType))
            {
                Diagnostics.Add(notFoundError.ToError(@operator));
                return false;
            }

            if (!FindBitWidth(leftType, out BitWidth leftBitWidth, out PossibleDiagnostic? e1, this))
            {
                Diagnostics.Add(e1.ToError(left));
                return false;
            }

            if (!FindBitWidth(rightType, out BitWidth rightBitWidth, out PossibleDiagnostic? e2, this))
            {
                Diagnostics.Add(e2.ToError(right));
                return false;
            }

            leftType = BuiltinType.CreateNumeric(leftNType, leftBitWidth);
            rightType = BuiltinType.CreateNumeric(rightNType, rightBitWidth);

            if (!CompileExpression(left, out compiledLeft, leftType)) return false;

            if (compiledLeft.Type.Is(out ReferenceType? lr2))
            {
                compiledLeft = new CompiledDereference()
                {
                    Address = compiledLeft,
                    Location = compiledLeft.Location,
                    SaveValue = compiledLeft.SaveValue,
                    Type = lr2.To,
                };
            }
            else if (compiledRight.Type.Is(out ReferenceType? rr2))
            {
                compiledRight = new CompiledDereference()
                {
                    Address = compiledRight,
                    Location = compiledRight.Location,
                    SaveValue = compiledRight.SaveValue,
                    Type = rr2.To,
                };
            }

            if (leftNType != NumericType.Float &&
                rightNType == NumericType.Float)
            {
                compiledLeft = CompiledCast.Wrap(compiledLeft, BuiltinType.F32);
                leftType = BuiltinType.F32;
                leftNType = NumericType.Float;
            }

            if (!CompileExpression(right, out compiledRight, rightType)) return false;

            if (leftType.SameAs(BasicType.F32) &&
                !rightType.SameAs(BasicType.F32))
            {
                compiledRight = CompiledCast.Wrap(compiledRight, BuiltinType.F32);
                // rightType = BuiltinType.F32;
                rightNType = NumericType.Float;
            }

            if ((leftNType is NumericType.Float) != (rightNType is NumericType.Float))
            {
                Diagnostics.Add(notFoundError.ToError(@operator));
                return false;
            }

            GeneralType resultType;

            {
                if (leftType.Is(out BuiltinType? leftBType) &&
                    rightType.Is(out BuiltinType? rightBType))
                {
                    bool isFloat =
                        leftBType.Type == BasicType.F32 ||
                        rightBType.Type == BasicType.F32;

                    if (!FindBitWidth(leftType, out leftBitWidth, out e1, this))
                    {
                        Diagnostics.Add(e1.ToError(@operator.Left));
                        return false;
                    }

                    if (!FindBitWidth(rightType, out rightBitWidth, out e2, this))
                    {
                        Diagnostics.Add(e2.ToError(@operator.Right));
                        return false;
                    }

                    BitWidth bitWidth = MaxBitWidth(leftBitWidth, rightBitWidth);

                    if (!leftBType.TryGetNumericType(out NumericType leftNType1) ||
                        !rightBType.TryGetNumericType(out NumericType rightNType1))
                    {
                        Diagnostics.Add(DiagnosticAt.Error($"Unknown operator \"{leftType}\" \"{@operator.Operator.Content}\" \"{rightType}\"", @operator.Operator, @operator.File));
                        return false;
                    }
                    NumericType numericType = leftNType1 > rightNType1 ? leftNType1 : rightNType1;

                    BuiltinType numericResultType = BuiltinType.CreateNumeric(numericType, bitWidth);

                    switch (@operator.Operator.Content)
                    {
                        case BinaryOperatorCallExpression.CompLT:
                        case BinaryOperatorCallExpression.CompGT:
                        case BinaryOperatorCallExpression.CompLEQ:
                        case BinaryOperatorCallExpression.CompGEQ:
                        case BinaryOperatorCallExpression.CompEQ:
                        case BinaryOperatorCallExpression.CompNEQ:
                            if (!GetUsedBy(InternalTypes.Boolean, out GeneralType? booleanType, out PossibleDiagnostic? internalTypeError))
                            {
                                resultType = BooleanType;
                            }
                            else
                            {
                                resultType = booleanType;
                            }
                            break;

                        case BinaryOperatorCallExpression.LogicalOR:
                        case BinaryOperatorCallExpression.LogicalAND:
                        case BinaryOperatorCallExpression.BitwiseAND:
                        case BinaryOperatorCallExpression.BitwiseOR:
                        case BinaryOperatorCallExpression.BitwiseXOR:
                        case BinaryOperatorCallExpression.BitshiftLeft:
                        case BinaryOperatorCallExpression.BitshiftRight:
                            resultType = numericResultType;
                            break;

                        case BinaryOperatorCallExpression.Addition:
                        case BinaryOperatorCallExpression.Subtraction:
                        case BinaryOperatorCallExpression.Multiplication:
                        case BinaryOperatorCallExpression.Division:
                        case BinaryOperatorCallExpression.Modulo:
                            resultType = isFloat ? BuiltinType.F32 : numericResultType;
                            break;

                        default:
                            return false;
                    }

                    SetStatementType(@operator, resultType);

                    goto OK;
                }
                else
                {
                    bool ok = true;

                    if (!leftType.TryGetNumericType(out leftNType))
                    {
                        Diagnostics.Add(DiagnosticAt.Error($"Type \"{leftType}\" aint a numeric type", @operator.Left));
                        ok = false;
                    }

                    if (!rightType.TryGetNumericType(out rightNType))
                    {
                        Diagnostics.Add(DiagnosticAt.Error($"Type \"{rightType}\" aint a numeric type", @operator.Right));
                        ok = false;
                    }

                    if (!FindBitWidth(leftType, out BitWidth leftBitwidth, out PossibleDiagnostic? error, this))
                    {
                        Diagnostics.Add(error.ToError(@operator.Left));
                        ok = false;
                    }

                    if (!FindBitWidth(rightType, out BitWidth rightBitwidth, out error, this))
                    {
                        Diagnostics.Add(error.ToError(@operator.Right));
                        ok = false;
                    }

                    if (!ok) { return false; }

                    CompiledValue leftValue = GetInitialValue(leftNType, leftBitwidth);
                    CompiledValue rightValue = GetInitialValue(rightNType, rightBitwidth);

                    if (!TryComputeBinaryOperator(@operator.Operator.Content, leftValue, rightValue, out CompiledValue predictedValue, out PossibleDiagnostic? evaluateError))
                    {
                        Diagnostics.Add(evaluateError.ToError(@operator));
                        return false;
                    }

                    if (!CompileType(predictedValue.Type, out resultType!, out PossibleDiagnostic? typeError))
                    {
                        Diagnostics.Add(typeError.ToError(@operator));
                        return false;
                    }
                }
            }

        OK:

            if (expectedType is not null &&
                CanCastImplicitly(resultType, expectedType, out _))
            {
                resultType = expectedType;
            }

            SetStatementType(@operator, resultType);

            compiledStatement = new CompiledBinaryOperatorCall()
            {
                Operator = @operator.Operator.Content,
                Left = compiledLeft,
                Right = compiledRight,
                Type = resultType,
                Location = @operator.Location,
                SaveValue = @operator.SaveValue,
            };

            if ((Settings.Optimizations.HasFlag(OptimizationSettings.StatementEvaluating) || Settings.OptimizationDiagnostics) &&
                TryCompute(compiledStatement, out CompiledValue evaluated, out _) &&
                evaluated.TryCast(compiledStatement.Type, out CompiledValue casted))
            {
                Diagnostics.Add(DiagnosticAt.OptimizationNotice($"Operator call evaluated with result \"{casted}\"", @operator));
                SetPredictedValue(@operator, casted);
                if (Settings.Optimizations.HasFlag(OptimizationSettings.StatementEvaluating))
                {
                    compiledStatement = CompiledConstantValue.Create(casted, compiledStatement);
                }
            }

            return true;
        }
        else if (@operator.Operator.Content == "=")
        {
            throw new NotImplementedException();
        }
        else
        {
            Diagnostics.Add(DiagnosticAt.Error($"Unknown operator \"{@operator.Operator.Content}\"", @operator.Operator, @operator.File));
            return false;
        }
    }
    bool CompileExpression(UnaryOperatorCallExpression @operator, [NotNullWhen(true)] out CompiledExpression? compiledStatement, GeneralType? expectedType = null)
    {
        compiledStatement = null;

        if (GetOperator(@operator, @operator.File, out FunctionQueryResult<CompiledOperatorDefinition>? operatorDefinition, out PossibleDiagnostic? operatorNotFoundError))
        {
            SetStatementReference(@operator, operatorDefinition.Function);
            @operator.Operator.AnalyzedType = TokenAnalyzedType.FunctionName;

            if (operatorDefinition.DidReplaceArguments) throw new UnreachableException();

            SetStatementType(@operator, operatorDefinition.Function.Type);

            if (operatorDefinition.Function.Definition.Attributes.Any(v => v.Identifier.Content == AttributeConstants.MSILIncompatibleIdentifier))
            {
                Frames.LastRef.IsMsilCompatible = false;
            }

            if (!operatorDefinition.Function.Definition.CanUse(@operator.File))
            {
                Diagnostics.Add(DiagnosticAt.Error($"Operator \"{operatorDefinition.Function.ToReadable()}\" cannot be called due to its protection level", @operator.Operator, @operator.File));
                return false;
            }

            if (UnaryOperatorCallExpression.ParameterCount != operatorDefinition.Function.Definition.ParameterCount)
            {
                Diagnostics.Add(DiagnosticAt.Error($"Wrong number of arguments passed to operator \"{operatorDefinition.Function.ToReadable()}\": required {operatorDefinition.Function.Definition.ParameterCount} passed {UnaryOperatorCallExpression.ParameterCount}", @operator));
                return false;
            }

            if (!CompileArguments(@operator.Arguments.ToImmutableArray(ArgumentExpression.Wrap), operatorDefinition.Function, operatorDefinition.TypeArguments, out ImmutableArray<CompiledArgument> compiledArguments)) return false;

            if (operatorDefinition.Function.ExternalFunctionName is not null)
            {
                if (!ExternalFunctions.TryGet(operatorDefinition.Function.ExternalFunctionName, out IExternalFunction? externalFunction, out PossibleDiagnostic? exception))
                {
                    Diagnostics.Add(exception.ToError(@operator.Operator, @operator.File));
                    return false;
                }

                operatorDefinition.Function.AddReference(@operator);
                return CompileFunctionCall_External(compiledArguments, @operator.SaveValue, operatorDefinition.Function, externalFunction, @operator.Location, out compiledStatement);
            }

            compiledStatement = new CompiledFunctionCall()
            {
                Function = TemplateInstance.New<ICompiledFunctionDefinition>(operatorDefinition.Function, operatorDefinition.TypeArguments),
                Arguments = compiledArguments,
                Location = @operator.Location,
                SaveValue = @operator.SaveValue,
                Type = operatorDefinition.Function.Type,
            };
            Frames.Last.CapturesGlobalVariables = null;

            if ((Settings.Optimizations.HasFlag(OptimizationSettings.StatementEvaluating) || Settings.OptimizationDiagnostics) &&
                TryCompute(compiledStatement, out CompiledValue evaluated, out _) &&
                evaluated.TryCast(compiledStatement.Type, out CompiledValue casted))
            {
                Diagnostics.Add(DiagnosticAt.OptimizationNotice($"Operator call evaluated with result \"{casted}\"", @operator));
                SetPredictedValue(@operator, casted);
                if (Settings.Optimizations.HasFlag(OptimizationSettings.StatementEvaluating))
                {
                    operatorDefinition.Function.AddReference(@operator, true);
                    compiledStatement = CompiledConstantValue.Create(casted, compiledStatement);
                    return true;
                }
            }

            operatorDefinition.Function.AddReference(@operator);
            return true;
        }
        else if (LanguageOperators.UnaryOperators.Contains(@operator.Operator.Content))
        {
            switch (@operator.Operator.Content)
            {
                case UnaryOperatorCallExpression.LogicalNOT:
                {
                    if (!CompileExpression(@operator.Expression, out CompiledExpression? left)) return false;

                    compiledStatement = new CompiledUnaryOperatorCall()
                    {
                        Left = left,
                        Location = @operator.Location,
                        Operator = @operator.Operator.Content,
                        SaveValue = @operator.SaveValue,
                        Type = BuiltinType.U8,
                    };

                    if ((Settings.Optimizations.HasFlag(OptimizationSettings.StatementEvaluating) || Settings.OptimizationDiagnostics) &&
                        TryCompute(compiledStatement, out CompiledValue evaluated, out _) &&
                        evaluated.TryCast(compiledStatement.Type, out CompiledValue casted))
                    {
                        Diagnostics.Add(DiagnosticAt.OptimizationNotice($"Operator call evaluated with result \"{casted}\"", @operator));
                        SetPredictedValue(@operator, casted);
                        if (Settings.Optimizations.HasFlag(OptimizationSettings.StatementEvaluating))
                        {
                            compiledStatement = CompiledConstantValue.Create(casted, compiledStatement);
                        }
                    }

                    return true;
                }
                case UnaryOperatorCallExpression.BinaryNOT:
                {
                    if (!CompileExpression(@operator.Expression, out CompiledExpression? left)) return false;

                    compiledStatement = new CompiledUnaryOperatorCall()
                    {
                        Left = left,
                        Location = @operator.Location,
                        Operator = @operator.Operator.Content,
                        SaveValue = @operator.SaveValue,
                        Type = left.Type,
                    };

                    if ((Settings.Optimizations.HasFlag(OptimizationSettings.StatementEvaluating) || Settings.OptimizationDiagnostics) &&
                        TryCompute(compiledStatement, out CompiledValue evaluated, out _) &&
                        evaluated.TryCast(compiledStatement.Type, out CompiledValue casted))
                    {
                        Diagnostics.Add(DiagnosticAt.OptimizationNotice($"Operator call evaluated with result \"{casted}\"", @operator));
                        SetPredictedValue(@operator, casted);
                        if (Settings.Optimizations.HasFlag(OptimizationSettings.StatementEvaluating))
                        {
                            compiledStatement = CompiledConstantValue.Create(casted, compiledStatement);
                        }
                    }

                    return true;
                }
                case UnaryOperatorCallExpression.UnaryMinus:
                {
                    if (!CompileExpression(@operator.Expression, out CompiledExpression? left)) return false;

                    compiledStatement = new CompiledUnaryOperatorCall()
                    {
                        Left = left,
                        Location = @operator.Location,
                        Operator = @operator.Operator.Content,
                        SaveValue = @operator.SaveValue,
                        Type = left.Type,
                    };

                    if ((Settings.Optimizations.HasFlag(OptimizationSettings.StatementEvaluating) || Settings.OptimizationDiagnostics) &&
                        TryCompute(compiledStatement, out CompiledValue evaluated, out _) &&
                        evaluated.TryCast(compiledStatement.Type, out CompiledValue casted))
                    {
                        Diagnostics.Add(DiagnosticAt.OptimizationNotice($"Operator call evaluated with result \"{casted}\"", @operator));
                        SetPredictedValue(@operator, casted);
                        if (Settings.Optimizations.HasFlag(OptimizationSettings.StatementEvaluating))
                        {
                            compiledStatement = CompiledConstantValue.Create(casted, compiledStatement);
                        }
                    }

                    return true;
                }
                case UnaryOperatorCallExpression.UnaryPlus:
                {
                    if (!CompileExpression(@operator.Expression, out CompiledExpression? left)) return false;

                    compiledStatement = new CompiledUnaryOperatorCall()
                    {
                        Left = left,
                        Location = @operator.Location,
                        Operator = @operator.Operator.Content,
                        SaveValue = @operator.SaveValue,
                        Type = left.Type,
                    };

                    if ((Settings.Optimizations.HasFlag(OptimizationSettings.StatementEvaluating) || Settings.OptimizationDiagnostics) &&
                        TryCompute(compiledStatement, out CompiledValue evaluated, out _) &&
                        evaluated.TryCast(compiledStatement.Type, out CompiledValue casted))
                    {
                        Diagnostics.Add(DiagnosticAt.OptimizationNotice($"Operator call evaluated with result \"{casted}\"", @operator));
                        SetPredictedValue(@operator, casted);
                        if (Settings.Optimizations.HasFlag(OptimizationSettings.StatementEvaluating))
                        {
                            compiledStatement = CompiledConstantValue.Create(casted, compiledStatement);
                        }
                    }

                    return true;
                }
                default:
                {
                    Diagnostics.Add(DiagnosticAt.Error($"Unknown operator \"{@operator.Operator.Content}\"", @operator.Operator, @operator.File));
                    return false;
                }
            }
        }
        else
        {
            Diagnostics.Add(DiagnosticAt.Error($"Unknown operator \"{@operator.Operator.Content}\"", @operator.Operator, @operator.File).WithSuberrors(operatorNotFoundError.ToError(@operator)));
            return false;
        }
    }
    bool CompileExpression(LambdaExpression lambdaStatement, [NotNullWhen(true)] out CompiledExpression? compiledStatement, GeneralType? expectedType = null)
    {
        compiledStatement = null;

        FunctionType? functionType = expectedType as FunctionType;

        ImmutableArray<CompiledParameter>.Builder compiledParameters = ImmutableArray.CreateBuilder<CompiledParameter>();

        for (int i = 0; i < lambdaStatement.Parameters.Length; i++)
        {
            if (!CompileType(lambdaStatement.Parameters[i].Type, out GeneralType? parameterType, Diagnostics))
            {
                return false;
            }

            compiledParameters.Add(new CompiledParameter(parameterType, lambdaStatement.Parameters[i]));
        }

        ImmutableArray<CompiledLabelDeclaration>.Builder localInstructionLabels = ImmutableArray.CreateBuilder<CompiledLabelDeclaration>();

        foreach (Statement item in StatementWalker.Visit(lambdaStatement.Body, StatementWalkerFilter.FrameOnlyFilter))
        {
            if (item is InstructionLabelDeclaration instructionLabel)
            {
                localInstructionLabels.Add(new CompiledLabelDeclaration()
                {
                    Identifier = instructionLabel.Identifier.Content,
                    Location = instructionLabel.Location,
                });
            }
        }

        using (StackAuto<CompiledFrame> frame = Frames.PushAuto(new CompiledFrame()
        {
            TypeArguments = Frames.Last.TypeArguments,
            IsTemplateInstance = Frames.Last.IsTemplateInstance,
            IsTemplate = Frames.Last.IsTemplate,
            TypeParameters = Frames.Last.TypeParameters,
            CompiledParameters = compiledParameters.ToImmutable(),
            InstructionLabels = localInstructionLabels.ToImmutable(),
            Scopes = new(),
            CurrentReturnType = functionType?.ReturnType,
            CompiledGeneratorContext = null,
            IsTopLevel = false,
        }))
        {
            CompiledStatement? _body;
            using (Frames.Last.Scopes.PushAuto(new Scope(ImmutableArray<CompiledVariableConstant>.Empty)))
            {
                if (!CompileStatement(lambdaStatement.Body, out _body)) return false;
            }

            CompiledBlock block;

            if (_body is CompiledBlock _block)
            {
                block = _block;
            }
            else if (_body is CompiledExpression _compiledStatementWithValue)
            {
                if (frame.Value.CurrentReturnType is null || frame.Value.CurrentReturnType.SameAs(BuiltinType.Void))
                {
                    _compiledStatementWithValue.SaveValue = false;
                    block = new CompiledBlock()
                    {
                        Location = _body.Location,
                        Statements = ImmutableArray.Create<CompiledStatement>(_compiledStatementWithValue),
                    };
                }
                else
                {
                    _compiledStatementWithValue.SaveValue = true;
                    block = new CompiledBlock()
                    {
                        Location = _body.Location,
                        Statements = ImmutableArray.Create<CompiledStatement>(new CompiledReturn()
                        {
                            Location = _body.Location,
                            Value = _compiledStatementWithValue,
                        }),
                    };
                    if (!frame.Value.CurrentReturnType.SameAs(_compiledStatementWithValue.Type))
                    {
                        Diagnostics.Add(DiagnosticAt.Error($"Lambda expression value's type ({_compiledStatementWithValue.Type}) doesn't match the return type {frame.Value.CurrentReturnType}", _compiledStatementWithValue));
                    }
                }
            }
            else
            {
                block = new CompiledBlock()
                {
                    Location = _body.Location,
                    Statements = ImmutableArray.Create(_body),
                };
            }

            if (!frame.Value.IsMsilCompatible)
            {
                Frames.LastRef.IsMsilCompatible = false;
            }

            ImmutableArray<CapturedLocal>.Builder closureBuilder = ImmutableArray.CreateBuilder<CapturedLocal>(frame.Value.CapturedVariables.Count + frame.Value.CapturedParameters.Count);
            foreach (CompiledParameter item in frame.Value.CapturedParameters)
            {
                closureBuilder.Add(new()
                {
                    ByRef = false,
                    Parameter = item,
                    Variable = null,
                });
            }
            foreach (CompiledVariableDefinition item in frame.Value.CapturedVariables)
            {
                closureBuilder.Add(new()
                {
                    ByRef = false,
                    Parameter = null,
                    Variable = item,
                });
            }
            ImmutableArray<CapturedLocal> closure = closureBuilder.MoveToImmutable();

            functionType = new FunctionType(frame.Value.CurrentReturnType ?? BuiltinType.Void, functionType?.Parameters ?? frame.Value.CompiledParameters.ToImmutableArray(v => v.Type), !closure.IsEmpty);

            CompiledExpression? allocator = null;
            if (!closure.IsEmpty)
            {
                compiledParameters.Insert(0, new CompiledParameter(PointerType.Any, new ParameterDefinition(
                    ImmutableArray<Token>.Empty,
                    null!,
                    Token.CreateAnonymous("closure"),
                    null,
                    lambdaStatement.File
                )));

                int closureSize = 0;
                foreach (CapturedLocal? item in closure)
                {
                    if (!FindSize((item.Variable?.Type ?? item.Parameter?.Type)!, out int itemSize, out PossibleDiagnostic? sizeError, this))
                    {
                        Diagnostics.Add(sizeError.ToError((item.Variable?.Location ?? item.Parameter?.Definition.Location)!));
                    }
                    closureSize += itemSize;
                }
                closureSize += PointerSize;
                if (!CompileAllocation(closureSize, lambdaStatement.Location, out allocator)) return false;
                if (!Frames.Last.IsTemplateInstance) lambdaStatement.AllocatorReference = allocator is CompiledFunctionCall cfc ? cfc.Function.Template : null;
            }

            if (!frame.Value.CapturesGlobalVariables.HasValue) Frames.Last.CapturesGlobalVariables = null;
            else if (frame.Value.CapturesGlobalVariables.Value) Frames.Last.CapturesGlobalVariables = true;

            compiledStatement = new CompiledLambda(
                functionType.ReturnType,
                compiledParameters.ToImmutable(),
                block,
                lambdaStatement.Parameters,
                closure,
                lambdaStatement.File
            )
            {
                Location = lambdaStatement.Location,
                SaveValue = lambdaStatement.SaveValue,
                Type = functionType,
                Allocator = allocator,
            };

            return true;
        }
    }
    bool CompileExpression(LiteralExpression literal, [NotNullWhen(true)] out CompiledExpression? compiledStatement, GeneralType? expectedType = null)
    {
        switch (literal)
        {
            case IntLiteralExpression intLiteral:
            {
                if (expectedType is not null)
                {
                    if (expectedType.SameAs(BasicType.U8))
                    {
                        if (intLiteral.Value is >= byte.MinValue and <= byte.MaxValue)
                        {
                            SetStatementType(literal, expectedType);
                            compiledStatement = new CompiledConstantValue()
                            {
                                Location = literal.Location,
                                SaveValue = literal.SaveValue,
                                Type = expectedType,
                                Value = new CompiledValue((byte)intLiteral.Value),
                            };
                            return true;
                        }
                    }
                    else if (expectedType.SameAs(BasicType.I8))
                    {
                        if (intLiteral.Value is >= sbyte.MinValue and <= sbyte.MaxValue)
                        {
                            SetStatementType(literal, expectedType);
                            compiledStatement = new CompiledConstantValue()
                            {
                                Location = literal.Location,
                                SaveValue = literal.SaveValue,
                                Type = expectedType,
                                Value = new CompiledValue((sbyte)intLiteral.Value),
                            };
                            return true;
                        }
                    }
                    else if (expectedType.SameAs(BasicType.U16))
                    {
                        if (intLiteral.Value is >= ushort.MinValue and <= ushort.MaxValue)
                        {
                            SetStatementType(literal, expectedType);
                            compiledStatement = new CompiledConstantValue()
                            {
                                Location = literal.Location,
                                SaveValue = literal.SaveValue,
                                Type = expectedType,
                                Value = new CompiledValue((ushort)intLiteral.Value),
                            };
                            return true;
                        }
                    }
                    else if (expectedType.SameAs(BasicType.I16))
                    {
                        if (intLiteral.Value is >= short.MinValue and <= short.MaxValue)
                        {
                            SetStatementType(literal, expectedType);
                            compiledStatement = new CompiledConstantValue()
                            {
                                Location = literal.Location,
                                SaveValue = literal.SaveValue,
                                Type = expectedType,
                                Value = new CompiledValue((short)intLiteral.Value),
                            };
                            return true;
                        }
                    }
                    else if (expectedType.SameAs(BasicType.U32))
                    {
                        SetStatementType(literal, expectedType);
                        compiledStatement = new CompiledConstantValue()
                        {
                            Location = literal.Location,
                            SaveValue = literal.SaveValue,
                            Type = expectedType,
                            Value = new CompiledValue(intLiteral.Value.U32()),
                        };
                        return true;
                    }
                    else if (expectedType.SameAs(BasicType.I32))
                    {
                        SetStatementType(literal, expectedType);
                        compiledStatement = new CompiledConstantValue()
                        {
                            Location = literal.Location,
                            SaveValue = literal.SaveValue,
                            Type = expectedType,
                            Value = new CompiledValue((int)intLiteral.Value),
                        };
                        return true;
                    }
                    else if (expectedType.SameAs(BasicType.F32))
                    {
                        SetStatementType(literal, expectedType);
                        compiledStatement = new CompiledConstantValue()
                        {
                            Location = literal.Location,
                            SaveValue = literal.SaveValue,
                            Type = expectedType,
                            Value = new CompiledValue((float)intLiteral.Value),
                        };
                        return true;
                    }
                }

                if (!GetLiteralType(LiteralType.Integer, out GeneralType? literalType, out _))
                { literalType = BuiltinType.I32; }

                SetStatementType(literal, literalType);
                compiledStatement = new CompiledConstantValue()
                {
                    Location = literal.Location,
                    SaveValue = literal.SaveValue,
                    Type = literalType,
                    Value = new CompiledValue((int)intLiteral.Value),
                };
                return true;
            }
            case FloatLiteralExpression floatLiteral:
            {
                if (!GetLiteralType(LiteralType.Float, out GeneralType? literalType, out _))
                { literalType = BuiltinType.F32; }

                SetStatementType(literal, literalType);
                compiledStatement = new CompiledConstantValue()
                {
                    Location = literal.Location,
                    SaveValue = literal.SaveValue,
                    Type = literalType,
                    Value = new CompiledValue(floatLiteral.Value),
                };
                return true;
            }
            case StringLiteralExpression stringLiteral:
            {
                if (expectedType is not null &&
                    expectedType.Is(out PointerType? pointerType) &&
                    pointerType.To.Is(out ArrayType? arrayType) &&
                    arrayType.Of.SameAs(BasicType.U8))
                {
                    SetStatementType(literal, expectedType);

                    compiledStatement = null;
                    if (!FindSize(BuiltinType.U8, out int charSize, out PossibleDiagnostic? sizeError, this))
                    {
                        Diagnostics.Add(sizeError.ToError(literal));
                    }
                    if (!CompileAllocation((1 + stringLiteral.Value.Length) * charSize, literal.Location, out CompiledExpression? allocator)) return false;

                    compiledStatement = new CompiledString()
                    {
                        Value = stringLiteral.Value,
                        IsASCII = true,
                        Location = literal.Location,
                        SaveValue = true,
                        Type = expectedType,
                        Allocator = allocator,
                    };
                    return true;
                }
                else if (expectedType is not null &&
                    expectedType.Is(out PointerType? pointerType2) &&
                    pointerType2.To.Is(out ArrayType? arrayType2) &&
                    arrayType2.Of.SameAs(BasicType.U16))
                {
                    SetStatementType(literal, expectedType);

                    compiledStatement = null;
                    if (!FindSize(BuiltinType.Char, out int charSize, out PossibleDiagnostic? sizeError, this))
                    {
                        Diagnostics.Add(sizeError.ToError(literal));
                    }
                    if (!CompileAllocation((1 + stringLiteral.Value.Length) * charSize, literal.Location, out CompiledExpression? allocator)) return false;

                    compiledStatement = new CompiledString()
                    {
                        Value = stringLiteral.Value,
                        IsASCII = false,
                        Location = literal.Location,
                        SaveValue = true,
                        Type = expectedType,
                        Allocator = allocator,
                    };
                    return true;
                }
                else if (expectedType is not null &&
                    expectedType.Is(out ArrayType? arrayType3) &&
                    arrayType3.Of.SameAs(BasicType.U8))
                {
                    SetStatementType(literal, expectedType);

                    compiledStatement = new CompiledStackString()
                    {
                        Value = stringLiteral.Value,
                        IsASCII = true,
                        Location = literal.Location,
                        SaveValue = true,
                        Type = expectedType,
                        IsNullTerminated = arrayType3.Length.HasValue && arrayType3.Length.Value > stringLiteral.Value.Length,
                    };
                    return true;
                }
                else if (expectedType is not null &&
                    expectedType.Is(out ArrayType? arrayType4) &&
                    arrayType4.Of.SameAs(BasicType.U16))
                {
                    SetStatementType(literal, expectedType);

                    compiledStatement = new CompiledStackString()
                    {
                        Value = stringLiteral.Value,
                        IsASCII = false,
                        Location = literal.Location,
                        SaveValue = true,
                        Type = expectedType,
                        IsNullTerminated = arrayType4.Length.HasValue && arrayType4.Length.Value > stringLiteral.Value.Length,
                    };
                    return true;
                }
                else
                {
                    compiledStatement = null;
                    if (!FindSize(BuiltinType.Char, out int charSize, out PossibleDiagnostic? sizeError, this))
                    {
                        Diagnostics.Add(sizeError.ToError(literal));
                    }
                    if (!CompileAllocation((1 + stringLiteral.Value.Length) * charSize, literal.Location, out CompiledExpression? allocator)) return false;

                    if (!GetLiteralType(LiteralType.String, out GeneralType? stringType, out _))
                    {
                        if (!GetLiteralType(LiteralType.Char, out GeneralType? charType, out _))
                        { charType = BuiltinType.Char; }

                        stringType = new PointerType(new ArrayType(charType, stringLiteral.Value.Length + 1));
                    }

                    SetStatementType(literal, stringType);

                    compiledStatement = new CompiledString()
                    {
                        Value = stringLiteral.Value,
                        IsASCII = false,
                        Location = literal.Location,
                        SaveValue = true,
                        Type = stringType,
                        Allocator = allocator,
                    };
                    return true;
                }
            }
            case CharLiteralExpression charLiteral:
            {
                if (expectedType is not null)
                {
                    if (expectedType.SameAs(BasicType.U8))
                    {
                        if ((int)charLiteral.Value is >= byte.MinValue and <= byte.MaxValue)
                        {
                            SetStatementType(literal, expectedType);
                            compiledStatement = new CompiledConstantValue()
                            {
                                Location = literal.Location,
                                SaveValue = literal.SaveValue,
                                Type = expectedType,
                                Value = new CompiledValue((byte)charLiteral.Value),
                            };
                            return true;
                        }
                    }
                    else if (expectedType.SameAs(BasicType.I8))
                    {
                        if ((int)charLiteral.Value is >= sbyte.MinValue and <= sbyte.MaxValue)
                        {
                            SetStatementType(literal, expectedType);
                            compiledStatement = new CompiledConstantValue()
                            {
                                Location = literal.Location,
                                SaveValue = literal.SaveValue,
                                Type = expectedType,
                                Value = new CompiledValue((sbyte)charLiteral.Value),
                            };
                            return true;
                        }
                    }
                    else if (expectedType.SameAs(BasicType.U16))
                    {
                        SetStatementType(literal, expectedType);
                        compiledStatement = new CompiledConstantValue()
                        {
                            Location = literal.Location,
                            SaveValue = literal.SaveValue,
                            Type = expectedType,
                            Value = new CompiledValue((char)charLiteral.Value),
                        };
                        return true;
                    }
                    else if (expectedType.SameAs(BasicType.I16))
                    {
                        if ((int)charLiteral.Value is >= short.MinValue and <= short.MaxValue)
                        {
                            SetStatementType(literal, expectedType);
                            compiledStatement = new CompiledConstantValue()
                            {
                                Location = literal.Location,
                                SaveValue = literal.SaveValue,
                                Type = expectedType,
                                Value = new CompiledValue((short)charLiteral.Value),
                            };
                            return true;
                        }
                    }
                    else if (expectedType.SameAs(BasicType.U32))
                    {
                        if (charLiteral.Value >= uint.MinValue)
                        {
                            SetStatementType(literal, expectedType);
                            compiledStatement = new CompiledConstantValue()
                            {
                                Location = literal.Location,
                                SaveValue = literal.SaveValue,
                                Type = expectedType,
                                Value = new CompiledValue((uint)charLiteral.Value),
                            };
                            return true;
                        }
                    }
                    else if (expectedType.SameAs(BasicType.I32))
                    {
                        if (charLiteral.Value >= int.MinValue)
                        {
                            SetStatementType(literal, expectedType);
                            compiledStatement = new CompiledConstantValue()
                            {
                                Location = literal.Location,
                                SaveValue = literal.SaveValue,
                                Type = expectedType,
                                Value = new CompiledValue((int)charLiteral.Value),
                            };
                            return true;
                        }
                    }
                    else if (expectedType.SameAs(BasicType.F32))
                    {
                        SetStatementType(literal, expectedType);
                        compiledStatement = new CompiledConstantValue()
                        {
                            Location = literal.Location,
                            SaveValue = literal.SaveValue,
                            Type = expectedType,
                            Value = new CompiledValue((float)charLiteral.Value),
                        };
                        return true;
                    }
                }

                if (!GetLiteralType(LiteralType.Char, out GeneralType? literalType, out _))
                { literalType = BuiltinType.Char; }

                SetStatementType(literal, literalType);
                compiledStatement = new CompiledConstantValue()
                {
                    Location = literal.Location,
                    SaveValue = literal.SaveValue,
                    Type = literalType,
                    Value = new CompiledValue((char)charLiteral.Value),
                };
                return true;
            }
            default: throw new UnreachableException();
        }
    }
    bool CompileExpression(IdentifierExpression variable, [NotNullWhen(true)] out CompiledExpression? compiledStatement, GeneralType? expectedType = null, bool resolveReference = true)
    {
        compiledStatement = null;

        if (variable is IMissingNode)
        {
            Diagnostics.Add(DiagnosticAt.Error($"Incomplete AST", variable, false));
            return false;
        }

        if (variable.Content.StartsWith('#'))
        {
            compiledStatement = new CompiledConstantValue()
            {
                Value = new CompiledValue(PreprocessorVariables.Contains(variable.Content[1..])),
                Type = BooleanType,
                Location = variable.Location,
                SaveValue = variable.SaveValue,
            };
            return true;
        }

        if (RegisterKeywords.TryGetValue(variable.Content, out (Register Register, BuiltinType Type) registerKeyword))
        {
            compiledStatement = new CompiledRegisterAccess()
            {
                Register = registerKeyword.Register,
                Type = registerKeyword.Type,
                Location = variable.Location,
                SaveValue = variable.SaveValue,
            };
            return true;
        }

        if (!Settings.ExpressionVariables.IsDefault)
        {
            foreach (ExpressionVariable item in Settings.ExpressionVariables)
            {
                if (item.Name != variable.Content) continue;
                variable.AnalyzedType = TokenAnalyzedType.VariableName;
                SetStatementReference(variable, item);
                SetStatementType(variable, item.Type);

                compiledStatement = new CompiledExpressionVariableAccess()
                {
                    Variable = item,
                    Location = variable.Location,
                    SaveValue = variable.SaveValue,
                    Type = item.Type,
                };
                return true;
            }
        }

        if (GetConstant(variable.Content, variable.File, out CompiledVariableConstant? constant, out PossibleDiagnostic? constantNotFoundError))
        {
            SetStatementType(variable, constant.Type);
            SetPredictedValue(variable, constant.Value);
            SetStatementReference(variable, constant);
            variable.AnalyzedType = TokenAnalyzedType.ConstantName;

            if (constant.Definition.Attributes.Any(v => v.Identifier.Content == AttributeConstants.MSILIncompatibleIdentifier))
            {
                Frames.LastRef.IsMsilCompatible = false;
            }

            if (constant.Definition.InternalConstantName is not null)
            {
                compiledStatement = new CompiledCompilerVariableAccess()
                {
                    Identifier = constant.Definition.InternalConstantName,
                    Type = constant.Type,
                    Location = variable.Location,
                    SaveValue = variable.SaveValue,
                };
                return true;
            }
            else
            {
                CompiledValue value = constant.Value;
                GeneralType type = constant.Type;

                if (expectedType is not null &&
                    constant.Value.TryCast(expectedType, out CompiledValue castedValue))
                {
                    value = castedValue;
                    type = expectedType;
                }

                compiledStatement = new CompiledConstantValue()
                {
                    Value = value,
                    Type = type,
                    Location = variable.Location,
                    SaveValue = variable.SaveValue,
                };
                return true;
            }
        }

        if (GetParameter(variable.Content, out CompiledParameter? param, out PossibleDiagnostic? parameterNotFoundError))
        {
            GeneralType paramType = GeneralType.TryInsertTypeParameters(param.Type, Frames.Last.TypeArguments);

            if (variable.Content != StatementKeywords.This)
            { variable.AnalyzedType = TokenAnalyzedType.ParameterName; }
            SetStatementReference(variable, param);
            SetStatementType(variable, paramType);

            compiledStatement = new CompiledParameterAccess()
            {
                Parameter = param,
                Location = variable.Location,
                SaveValue = variable.SaveValue,
                Type = paramType,
            };
            return true;
        }

        if (GetVariable(variable.Content, out CompiledVariableDefinition? val, out PossibleDiagnostic? variableNotFoundError))
        {
            variable.AnalyzedType = TokenAnalyzedType.VariableName;
            SetStatementReference(variable, val);
            SetStatementType(variable, val.Type);

            if (val.IsGlobal)
            { Diagnostics.Add(DiagnosticAt.Internal($"Trying to get local variable \"{val.Identifier}\" but it was compiled as a global variable.", variable)); }

            if (Frames.Last.CompiledGeneratorContext is not null)
            {
                //Debugger.Break();
                if (GeneratorStructDefinition is null)
                {
                    Diagnostics.Add(DiagnosticAt.Error($"No struct found with an [{AttributeConstants.BuiltinIdentifier}(\"generator\")] attribute.", variable));
                    return false;
                }

                CompiledField field = Frames.Last.CompiledGeneratorContext.State.AddVariable(val.Identifier, val.Type);
                if (!GetParameter("this", out CompiledParameter? thisParameter, out parameterNotFoundError))
                {
                    Diagnostics.Add(parameterNotFoundError.ToError(variable));
                    return false;
                }

                compiledStatement = new CompiledFieldAccess()
                {
                    Field = field,
                    Object = new CompiledParameterAccess()
                    {
                        Parameter = thisParameter,
                        Type = thisParameter.Type,
                        SaveValue = true,
                        Location = variable.Location,
                    },
                    Type = field.Type,
                    SaveValue = variable.SaveValue,
                    Location = variable.Location,
                };
                return true;
            }

            compiledStatement = new CompiledVariableAccess()
            {
                Variable = val,
                Type = val.Type,
                Location = variable.Location,
                SaveValue = variable.SaveValue,
            };
            return true;
        }

        if (GetGlobalVariable(variable.Content, variable.File, out CompiledVariableDefinition? globalVariable, out PossibleDiagnostic? globalVariableNotFoundError))
        {
            variable.AnalyzedType = TokenAnalyzedType.VariableName;
            SetStatementReference(variable, globalVariable);
            SetStatementType(variable, globalVariable.Type);
            Frames.Last.CapturesGlobalVariables = true;

            if (!globalVariable.IsGlobal)
            { Diagnostics.Add(DiagnosticAt.Internal($"Trying to get global variable \"{globalVariable.Identifier}\" but it was compiled as a local variable.", variable)); }

            compiledStatement = new CompiledVariableAccess()
            {
                Variable = globalVariable,
                Type = globalVariable.Type,
                Location = variable.Location,
                SaveValue = variable.SaveValue,
            };
            return true;
        }

        if (GetFunction(variable.Content, expectedType, out FunctionQueryResult<CompiledFunctionDefinition>? compiledFunction, out PossibleDiagnostic? functionNotFoundError, AddCompilable))
        {
            if (compiledFunction.Function.Definition.Attributes.Any(v => v.Identifier.Content == AttributeConstants.MSILIncompatibleIdentifier))
            {
                Frames.LastRef.IsMsilCompatible = false;
            }

            FunctionType functionType = new(compiledFunction.Function.Type, compiledFunction.Function.Parameters.ToImmutableArray(v => v.Type), false);

            compiledFunction.Function.AddReference(variable, variable.Location);
            variable.AnalyzedType = TokenAnalyzedType.FunctionName;
            SetStatementReference(variable, compiledFunction.Function);
            SetStatementType(variable, functionType);

            compiledStatement = new CompiledFunctionReference()
            {
                Function = TemplateInstance.New<ICompiledFunctionDefinition>(compiledFunction.Function, compiledFunction.TypeArguments),
                Type = functionType,
                Location = variable.Location,
                SaveValue = variable.SaveValue,
            };
            return true;
        }

        if (GetInstructionLabel(variable.Content, out CompiledLabelDeclaration? instructionLabel, out PossibleDiagnostic? instructionLabelError))
        {
            SetStatementReference(variable, instructionLabel);
            variable.AnalyzedType = TokenAnalyzedType.InstructionLabel;
            SetStatementType(variable, CompiledLabelDeclaration.Type);

            compiledStatement = new CompiledLabelReference()
            {
                InstructionLabel = instructionLabel,
                Type = CompiledLabelDeclaration.Type,
                Location = variable.Location,
                SaveValue = variable.SaveValue,
            };
            return true;
        }

        for (int i = Frames.Count - 2; i >= 0; i--)
        {
            if (GetParameter(variable.Content, Frames[i], out CompiledParameter? outerParameter, out _))
            {
                variable.AnalyzedType = TokenAnalyzedType.VariableName;
                SetStatementReference(variable, outerParameter);
                SetStatementType(variable, outerParameter.Type);

                compiledStatement = new CompiledParameterAccess()
                {
                    Parameter = outerParameter,
                    Type = outerParameter.Type,
                    Location = variable.Location,
                    SaveValue = variable.SaveValue,
                };
                for (int j = i + 1; j < Frames.Count; j++)
                {
                    Frames[j].CapturedParameters.Add(outerParameter);
                }
                return true;
            }

            if (GetVariable(variable.Content, Frames[i], out CompiledVariableDefinition? outerLocal, out _))
            {
                variable.AnalyzedType = TokenAnalyzedType.VariableName;
                SetStatementReference(variable, outerLocal);
                SetStatementType(variable, outerLocal.Type);

                if (outerLocal.IsGlobal)
                { Diagnostics.Add(DiagnosticAt.Internal($"Trying to get local variable \"{outerLocal.Identifier}\" but it was compiled as a global variable.", variable)); }

                if (Frames.Last.CompiledGeneratorContext is not null)
                {
                    Diagnostics.Add(DiagnosticAt.Internal($"aaaaaaa", variable));
                    return false;
                }

                compiledStatement = new CompiledVariableAccess()
                {
                    Variable = outerLocal,
                    Type = outerLocal.Type,
                    Location = variable.Location,
                    SaveValue = variable.SaveValue,
                };
                for (int j = i + 1; j < Frames.Count; j++)
                {
                    Frames[j].CapturedVariables.Add(outerLocal);
                }
                return true;
            }
        }

        Diagnostics.Add(DiagnosticAt.Error($"Symbol \"{variable.Content}\" not found", variable)
            .WithSuberrors(
                constantNotFoundError.ToError(variable),
                parameterNotFoundError.ToError(variable),
                variableNotFoundError.ToError(variable),
                globalVariableNotFoundError.ToError(variable),
                functionNotFoundError.ToError(variable),
                instructionLabelError.ToError(variable)
            ));
        return false;
    }
    bool CompileExpression(GetReferenceExpression addressGetter, [NotNullWhen(true)] out CompiledExpression? compiledStatement, GeneralType? expectedType = null)
    {
        compiledStatement = null;

        if (!CompileExpression(addressGetter.Expression, out CompiledExpression? of)) return false;

        compiledStatement = new CompiledGetReference()
        {
            Of = of,
            Type = new PointerType(of.Type),
            Location = addressGetter.Location,
            SaveValue = addressGetter.SaveValue,
        };
        return true;
    }
    bool CompileExpression(DereferenceExpression pointer, [NotNullWhen(true)] out CompiledExpression? compiledStatement, GeneralType? expectedType = null)
    {
        compiledStatement = null;

        if (!CompileExpression(pointer.Expression, out CompiledExpression? to)) return false;

        GeneralType addressType = to.Type;
        if (!addressType.Is(out PointerType? pointerType))
        {
            Diagnostics.Add(DiagnosticAt.Error($"This isn't a pointer", pointer.Expression));
            return false;
        }

        compiledStatement = new CompiledDereference()
        {
            Address = to,
            Type = pointerType.To,
            Location = pointer.Location,
            SaveValue = pointer.SaveValue,
        };
        return true;
    }
    bool CompileExpression(NewInstanceExpression newInstance, [NotNullWhen(true)] out CompiledExpression? compiledStatement, GeneralType? expectedType = null)
    {
        compiledStatement = null;
        if (!CompileStatement(newInstance.Type, out CompiledTypeExpression? instanceType, Diagnostics))
        {
            return false;
        }

        switch (instanceType)
        {
            case CompiledPointerTypeExpression pointerType:
            {
                if (!CompileAllocation(pointerType.To, out compiledStatement))
                { return false; }

                if (!CompileType(instanceType, out GeneralType? compiledType, out PossibleDiagnostic? typeError, true))
                {
                    Diagnostics.Add(typeError.ToError(instanceType));
                    return false;
                }

                SetStatementType(newInstance.Type, compiledType);
                SetStatementType(newInstance, compiledType);

                compiledStatement = new CompiledReinterpretation()
                {
                    Value = compiledStatement,
                    TypeExpression = instanceType,
                    Type = compiledType,
                    Location = compiledStatement.Location,
                    SaveValue = compiledStatement.SaveValue,
                };
                return true;
            }

            case CompiledStructTypeExpression structType:
            {
                if (!CompileType(structType, out GeneralType? compiledType, out PossibleDiagnostic? typeError))
                {
                    Diagnostics.Add(typeError.ToError(structType));
                    return false;
                }

                SetStatementType(newInstance.Type, compiledType);
                SetStatementType(newInstance, compiledType);
                structType.Struct.AddReference(newInstance.Type);

                compiledStatement = new CompiledStackAllocation()
                {
                    Type = compiledType,
                    TypeExpression = structType,
                    Location = newInstance.Location,
                    SaveValue = newInstance.SaveValue,
                };
                return true;
            }

            case CompiledArrayTypeExpression arrayType:
            {
                if (!CompileType(arrayType, out GeneralType? compiledType, out PossibleDiagnostic? typeError))
                {
                    Diagnostics.Add(typeError.ToError(arrayType));
                    return false;
                }

                SetStatementType(newInstance.Type, compiledType);
                SetStatementType(newInstance, compiledType);

                compiledStatement = new CompiledStackAllocation()
                {
                    Type = compiledType,
                    TypeExpression = arrayType,
                    Location = newInstance.Location,
                    SaveValue = newInstance.SaveValue,
                };
                return true;
            }

            default:
            {
                Diagnostics.Add(DiagnosticAt.Error($"Unknown type \"{instanceType}\"", newInstance.Type, newInstance.File));
                return false;
            }
        }
    }
    bool CompileExpression(ConstructorCallExpression constructorCall, [NotNullWhen(true)] out CompiledExpression? compiledStatement, GeneralType? expectedType = null)
    {
        compiledStatement = null;
        if (!CompileType(constructorCall.Type, out GeneralType? instanceType, Diagnostics))
        {
            return false;
        }

        if (!FindStatementTypes(constructorCall.Arguments.Arguments, out ImmutableArray<GeneralType> parameters, Diagnostics))
        {
            return false;
        }

        if (!GetConstructor(instanceType, parameters, constructorCall.File, out FunctionQueryResult<CompiledConstructorDefinition>? compiledFunction, out PossibleDiagnostic? notFound, v => AddCompilable(v)))
        {
            Diagnostics.Add(notFound.ToError(constructorCall.Type, constructorCall.File));
            return false;
        }

        if (compiledFunction.Function.Definition.Attributes.Any(v => v.Identifier.Content == AttributeConstants.MSILIncompatibleIdentifier))
        {
            Frames.LastRef.IsMsilCompatible = false;
        }

        compiledFunction.Function.AddReference(constructorCall);
        SetStatementReference(constructorCall, compiledFunction.Function);

        SetStatementType(constructorCall, compiledFunction.Function.Type);

        if (!compiledFunction.Function.Definition.CanUse(constructorCall.File))
        {
            Diagnostics.Add(DiagnosticAt.Error($"Constructor \"{compiledFunction.Function.ToReadable()}\" could not be called due to its protection level", constructorCall.Type, constructorCall.File)
                .WithRelatedInfo(new DiagnosticRelatedInformationAt($"Constructor \"{compiledFunction.Function.ToReadable()}\" defined here", compiledFunction.Function.Location)));
            return false;
        }

        ImmutableArray<ArgumentExpression> arguments = constructorCall.Arguments.Arguments;
        compiledFunction.ReplaceArgumentsIfNeeded(ref arguments);

        if (!CompileExpression(constructorCall.ToInstantiation(), out CompiledExpression? _object)) return false;
        if (!CompileArguments(arguments, compiledFunction.Function, compiledFunction.TypeArguments, out ImmutableArray<CompiledArgument> compiledArguments, 1)) return false;

        Frames.Last.CapturesGlobalVariables = null;
        compiledStatement = new CompiledConstructorCall()
        {
            Arguments = compiledArguments,
            Function = TemplateInstance.New(compiledFunction),
            Object = _object,
            Location = constructorCall.Location,
            SaveValue = constructorCall.SaveValue,
            Type = instanceType,
        };
        return true;
    }
    bool CompileExpression(FieldExpression field, [NotNullWhen(true)] out CompiledExpression? compiledStatement, GeneralType? expectedType = null)
    {
        compiledStatement = null;
        field.Identifier.AnalyzedType = TokenAnalyzedType.FieldName;

        if (!CompileExpression(field.Object, out CompiledExpression? prev)) return false;

        if (field.Identifier is IMissingNode)
        {
            Diagnostics.Add(DiagnosticAt.Error($"Incomplete AST", field.Identifier, field.File, false));
            return false;
        }

        if (prev.Type.Is(out ArrayType? arrayType) && field.Identifier.Content == "Length")
        {
            if (!arrayType.Length.HasValue)
            {
                Diagnostics.Add(DiagnosticAt.Error("I will eventually implement this", field));
                return false;
            }

            SetStatementType(field, ArrayLengthType);
            SetPredictedValue(field, arrayType.Length.Value);

            compiledStatement = new CompiledConstantValue()
            {
                Value = arrayType.Length.Value,
                Type = ArrayLengthType,
                Location = field.Location,
                SaveValue = field.SaveValue,
            };
            return true;
        }

        if (prev.Type.Is(out PointerType? pointerType2))
        {
            GeneralType prevType = pointerType2.To;

            while (prevType.Is(out pointerType2))
            {
                prevType = pointerType2.To;
            }

            if (!prevType.Is(out StructType? structPointerType))
            {
                Diagnostics.Add(DiagnosticAt.Error($"Could not get the field offsets of type \"{prevType}\"", field.Object));
                return false;
            }

            if (!structPointerType.GetField(field.Identifier.Content, out CompiledField? fieldDefinition, out PossibleDiagnostic? error1))
            {
                Diagnostics.Add(error1.ToError(field.Identifier, field.File));
                return false;
            }

            SetStatementType(field, fieldDefinition.Type);
            SetStatementReference(field, fieldDefinition);

            compiledStatement = new CompiledFieldAccess()
            {
                Object = prev,
                Field = fieldDefinition,
                Location = field.Location,
                SaveValue = field.SaveValue,
                Type = GeneralType.TryInsertTypeParameters(fieldDefinition.Type, structPointerType.TypeArguments),
            };
            return true;
        }

        if (!prev.Type.Is(out StructType? structType))
        {
            Diagnostics.Add(DiagnosticAt.Error($"Type `{prev.Type}` doesn't have any fields", field.Identifier, field.File));
            return false;
        }

        if (!structType.GetField(field.Identifier.Content, out CompiledField? compiledField, out PossibleDiagnostic? error2))
        {
            Diagnostics.Add(error2.ToError(field.Identifier, field.File));
            return false;
        }

        SetStatementType(field, compiledField.Type);
        SetStatementReference(field, compiledField);

        compiledStatement = new CompiledFieldAccess()
        {
            Field = compiledField,
            Object = prev,
            Location = field.Location,
            SaveValue = field.SaveValue,
            Type = GeneralType.TryInsertTypeParameters(compiledField.Type, structType.TypeArguments),
        };
        return true;
    }
    bool CompileExpression(IndexCallExpression index, [NotNullWhen(true)] out CompiledExpression? compiledStatement, GeneralType? expectedType = null)
    {
        compiledStatement = null;

        if (!CompileExpression(index.Object, out CompiledExpression? baseStatement)) return false;
        if (!CompileExpression(index.Index, out CompiledExpression? indexStatement)) return false;

        if (GetIndexGetter(index, out FunctionQueryResult<CompiledFunctionDefinition>? indexer, out PossibleDiagnostic? notFoundError, AddCompilable))
        {
            indexer.Function.AddReference(index, index.Location);
            SetStatementReference(index, indexer.Function);

            return CompileFunctionCall(index, ImmutableArray.Create(ArgumentExpression.Wrap(index.Object), index.Index), indexer, out compiledStatement);
        }

        if (baseStatement.Type.Is(out ArrayType? arrayType))
        {
            if (TryCompute(indexStatement, out CompiledValue computedIndexData, out _))
            {
                SetPredictedValue(index.Index, computedIndexData);

                if (computedIndexData < 0 || (arrayType.Length.HasValue && computedIndexData >= arrayType.Length.Value))
                { Diagnostics.Add(DiagnosticAt.Warning($"Index out of range", index.Index)); }
            }

            compiledStatement = new CompiledElementAccess()
            {
                Base = baseStatement,
                Index = indexStatement,
                Type = arrayType.Of,
                Location = index.Location,
                SaveValue = index.SaveValue,
            };
            return true;
        }

        if (baseStatement.Type.Is(out PointerType? pointerType) && pointerType.To.Is(out arrayType))
        {
            compiledStatement = new CompiledElementAccess()
            {
                Base = baseStatement,
                Index = indexStatement,
                Type = arrayType.Of,
                Location = index.Location,
                SaveValue = index.SaveValue,
            };
            return true;
        }

        Diagnostics.Add(DiagnosticAt.Error($"Index getter for type \"{baseStatement.Type}\" not found", index).WithSuberrors(notFoundError.ToError(index)));
        return false;
    }
    bool CompileExpression(ArgumentExpression modifiedStatement, [NotNullWhen(true)] out CompiledExpression? compiledStatement, GeneralType? expectedType = null)
    {
        if (modifiedStatement.Modifier is null) return CompileExpression(modifiedStatement.Value, out compiledStatement, expectedType);

        return modifiedStatement.Modifier.Content switch
        {
            ModifierKeywords.Temp => CompileExpression(modifiedStatement.Value, out compiledStatement, expectedType),
            _ => throw new NotImplementedException(modifiedStatement.Modifier.Content),
        };
    }
    bool CompileExpression(ListExpression listValue, [NotNullWhen(true)] out CompiledExpression? compiledStatement, GeneralType? expectedType = null)
    {
        compiledStatement = null;
        GeneralType? itemType = (expectedType as ArrayType)?.Of;

        ImmutableArray<CompiledExpression>.Builder result = ImmutableArray.CreateBuilder<CompiledExpression>(listValue.Values.Length);
        for (int i = 0; i < listValue.Values.Length; i++)
        {
            if (!CompileExpression(listValue.Values[i], out CompiledExpression? item, itemType)) return false;

            if (itemType is null)
            {
                itemType = item.Type;
            }
            else if (!item.Type.SameAs(itemType))
            {
                Diagnostics.Add(DiagnosticAt.Error($"List element at index {i} should be a {itemType} and not {item.Type}", item));
            }

            result.Add(item);
        }

        if (itemType is null)
        {
            Diagnostics.Add(DiagnosticAt.Error($"Could not infer the list element type", listValue));
            itemType = BuiltinType.Any;
        }

        ArrayType type = new(itemType, listValue.Values.Length);

        compiledStatement = new CompiledList()
        {
            Values = result.ToImmutable(),
            Type = type,
            Location = listValue.Location,
            SaveValue = listValue.SaveValue,
        };
        return true;
    }
    bool CompileExpression(ReinterpretExpression reinterpret, [NotNullWhen(true)] out CompiledExpression? compiledStatement, GeneralType? expectedType = null)
    {
        compiledStatement = null;

        if (!CompileType(reinterpret.Type, out GeneralType? targetType, Diagnostics))
        {
            return false;
        }

        if (!CompileExpression(reinterpret.PrevStatement, out CompiledExpression? value)) return false;

        if (value.Type.Equals(targetType))
        {
            Diagnostics.Add(DiagnosticAt.Hint($"Redundant type conversion", reinterpret.Keyword, reinterpret.File));
            compiledStatement = value;
            SetStatementType(reinterpret, targetType);
            return true;
        }

        if (!CompileExpression(reinterpret.PrevStatement, out value, targetType)) return false;

        if (value.Type is PointerType statementPointerType
            && targetType is PointerType targetPointerType
            && targetPointerType.To is ArrayType targetArrayPointerType
            && !targetArrayPointerType.Length.HasValue
            && FindSize(statementPointerType.To, out int size1, out _, this)
            && FindSize(targetArrayPointerType.Of, out int size2, out _, this)
            && size1 % size2 == 0)
        {
            targetType = new PointerType(new ArrayType(targetArrayPointerType.Of, size1 / size2));
        }

        SetStatementType(reinterpret, targetType);
        SetStatementType(reinterpret.Type, targetType);

        if (!FindSize(targetType, out int targetSize, out PossibleDiagnostic? sizeError, this))
        {
            Diagnostics.Add(sizeError.ToError(reinterpret.Type));
        }
        else if (!FindSize(value.Type, out int valueSize, out sizeError, this))
        {
            Diagnostics.Add(sizeError.ToError(value));
        }
        else if (targetSize != valueSize)
        {
            Diagnostics.Add(DiagnosticAt.Error($"Cannot reinterpret type {value.Type} ({valueSize} bytes) as {targetType} ({targetSize} bytes)", reinterpret));
        }

        compiledStatement = new CompiledReinterpretation()
        {
            Value = value,
            TypeExpression = CompiledTypeExpression.CreateAnonymous(targetType, reinterpret.Type),
            Type = targetType,
            Location = reinterpret.Location,
            SaveValue = reinterpret.SaveValue,
        };
        return true;
    }
    bool CompileExpression(ManagedTypeCastExpression typeCast, [NotNullWhen(true)] out CompiledExpression? compiledStatement, GeneralType? expectedType = null)
    {
        compiledStatement = null;
        if (!CompileType(typeCast.Type, out GeneralType? targetType, Diagnostics))
        {
            return false;
        }
        SetStatementType(typeCast, targetType);

        if (!CompileExpression(typeCast.Expression, out CompiledExpression? prev, targetType)) return false;

        if (prev.Type.Equals(targetType))
        {
            // Diagnostics.Add(Diagnostic.Hint($"Redundant type conversion", typeCast.Type, typeCast.File));
            compiledStatement = prev;
            return true;
        }

        if ((Settings.Optimizations.HasFlag(OptimizationSettings.StatementEvaluating) || Settings.OptimizationDiagnostics) &&
            targetType.Is(out BuiltinType? targetBuiltinType) &&
            TryComputeSimple(typeCast.Expression, out CompiledValue prevValue) &&
            prevValue.TryCast(targetBuiltinType.RuntimeType, out CompiledValue castedValue))
        {
            Diagnostics.Add(DiagnosticAt.OptimizationNotice($"Type cast evaluated, converting {prevValue} ({prevValue.Type}) to {castedValue} ({castedValue.Type})", typeCast));
            if (Settings.Optimizations.HasFlag(OptimizationSettings.StatementEvaluating))
            {
                compiledStatement = new CompiledConstantValue()
                {
                    Value = castedValue,
                    Type = targetBuiltinType,
                    Location = typeCast.Location,
                    SaveValue = typeCast.SaveValue,
                };
                return true;
            }
        }

        // f32 -> i32
        if (prev.Type.SameAs(BuiltinType.F32) &&
            targetType.SameAs(BuiltinType.I32))
        {
            compiledStatement = new CompiledCast()
            {
                Value = prev,
                Type = targetType,
                TypeExpression = CompiledTypeExpression.CreateAnonymous(targetType, typeCast.Type),
                Allocator = null,
                Location = typeCast.Location,
                SaveValue = typeCast.SaveValue,
            };
            return true;
        }

        // i32 -> f32
        if (prev.Type.SameAs(BuiltinType.I32) &&
            targetType.SameAs(BuiltinType.F32))
        {
            compiledStatement = new CompiledCast()
            {
                Value = prev,
                Type = targetType,
                TypeExpression = CompiledTypeExpression.CreateAnonymous(targetType, typeCast.Type.Location),
                Allocator = null,
                Location = typeCast.Location,
                SaveValue = typeCast.SaveValue,
            };
            return true;
        }

        // fixme
        compiledStatement = new CompiledCast()
        {
            Value = prev,
            Type = targetType,
            Allocator = null,
            Location = typeCast.Location,
            SaveValue = typeCast.SaveValue,
            TypeExpression = CompiledTypeExpression.CreateAnonymous(targetType, typeCast.Location),
        };
        return true;
    }
    bool CompileExpression(Expression statement, [NotNullWhen(true)] out CompiledExpression? compiledStatement, GeneralType? expectedType = null, bool resolveReference = true)
    {
        Settings.CancellationToken.ThrowIfCancellationRequested();

        if (statement is IMissingNode)
        {
            Diagnostics.Add(DiagnosticAt.Error($"Incomplete AST", statement, false));
            compiledStatement = null;
            return false;
        }

        return statement switch
        {
            ListExpression v => CompileExpression(v, out compiledStatement, expectedType),
            BinaryOperatorCallExpression v => CompileExpression(v, out compiledStatement, expectedType),
            UnaryOperatorCallExpression v => CompileExpression(v, out compiledStatement),
            LiteralExpression v => CompileExpression(v, out compiledStatement, expectedType),
            IdentifierExpression v => CompileExpression(v, out compiledStatement, expectedType, resolveReference),
            GetReferenceExpression v => CompileExpression(v, out compiledStatement, expectedType),
            DereferenceExpression v => CompileExpression(v, out compiledStatement, expectedType),
            NewInstanceExpression v => CompileExpression(v, out compiledStatement, expectedType),
            ConstructorCallExpression v => CompileExpression(v, out compiledStatement, expectedType),
            IndexCallExpression v => CompileExpression(v, out compiledStatement, expectedType),
            FieldExpression v => CompileExpression(v, out compiledStatement, expectedType),
            ReinterpretExpression v => CompileExpression(v, out compiledStatement, expectedType),
            ManagedTypeCastExpression v => CompileExpression(v, out compiledStatement, expectedType),
            ArgumentExpression v => CompileExpression(v, out compiledStatement, expectedType),
            AnyCallExpression v => CompileExpression(v, out compiledStatement, expectedType),
            LambdaExpression v => CompileExpression(v, out compiledStatement, expectedType),
            _ => throw new NotImplementedException($"Expression {statement.GetType().Name} is not implemented"),
        };
    }
}
