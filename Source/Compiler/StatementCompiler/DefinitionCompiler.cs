using LanguageCore.Parser;
using LanguageCore.Parser.Statements;
using LanguageCore.Runtime;
using LanguageCore.Tokenizing;

namespace LanguageCore.Compiler;

public partial class StatementCompiler
{
    CompiledStruct CompileStructNoFields(StructDefinition @struct)
    {
        if (LanguageConstants.KeywordList.Contains(@struct.Identifier.Content))
        { Diagnostics.Add(DiagnosticAt.Error($"Illegal struct name \"{@struct.Identifier.Content}\"", @struct.Identifier, @struct.File)); }

        @struct.Identifier.AnalyzedType = TokenAnalyzedType.Struct;

        if (@struct.Template is not null)
        {
            GenericParameters.Push(@struct.Template.Parameters);
            foreach (Token typeParameter in @struct.Template.Parameters)
            { typeParameter.AnalyzedType = TokenAnalyzedType.TypeParameter; }
        }

        if (@struct.Template is not null)
        { GenericParameters.Pop(); }

        // Oh no wtf
        return new CompiledStruct(@struct.Fields.ToImmutableArray(v => new CompiledField(BuiltinType.Any, null!, v)), @struct);
    }

    void CompileStructFields(CompiledStruct @struct)
    {
        if (@struct.Definition.Template is not null)
        {
            GenericParameters.Push(@struct.Definition.Template.Parameters);
            foreach (Token typeParameter in @struct.Definition.Template.Parameters)
            { typeParameter.AnalyzedType = TokenAnalyzedType.TypeParameter; }
        }

        ImmutableArray<CompiledField>.Builder compiledFields = ImmutableArray.CreateBuilder<CompiledField>(@struct.Fields.Length);

        for (int i = 0; i < @struct.Fields.Length; i++)
        {
            FieldDefinition field = @struct.Fields[i].Definition;

            if (!CompileType(field.Type, out GeneralType? fieldType, Diagnostics)) continue;
            compiledFields.Add(new CompiledField(fieldType, null! /* CompiledStruct constructor will set this */, field));
        }

        if (@struct.Definition.Template is not null)
        { GenericParameters.Pop(); }

        if (compiledFields.Count != compiledFields.Capacity) return;

        @struct.SetFields(compiledFields.MoveToImmutable());
    }

    void CompileFunctionAttributes<TCompiledDefinition>(TCompiledDefinition function)
        where TCompiledDefinition : ICompiledFunctionDefinition, ICompiledDefinition<FunctionThingDefinition>
    {
        GeneralType type = function.Type;

        foreach (AttributeUsage attribute in function.Definition.Attributes)
        {
            switch (attribute.Identifier.Content)
            {
                case AttributeConstants.ExternalIdentifier:
                {
                    if (attribute.Parameters.Length != 1)
                    {
                        Diagnostics.Add(DiagnosticAt.Error($"Wrong number of arguments passed to attribute \"{attribute.Identifier}\": required {1}, passed {attribute.Parameters.Length}", attribute));
                        break;
                    }

                    if (attribute.Parameters[0] is not StringLiteralExpression stringLiteral)
                    {
                        Diagnostics.Add(DiagnosticAt.Error($"Invalid parameter type for attribute \"{attribute.Identifier}\" at {0}: expected string", attribute));
                        break;
                    }

                    if (!ExternalFunctions.TryGet(stringLiteral.Value, out IExternalFunction? externalFunction, out PossibleDiagnostic? exception))
                    {
                        if (function.Definition.Block is null) Diagnostics.Add(exception.ToWarning(attribute, function.File));
                        break;
                    }

                    CheckExternalFunctionDeclaration(this, function.Definition, externalFunction, type, (function as ICompiledFunctionDefinition).Parameters.ToImmutableArray(v => v.Type), Diagnostics);

                    break;
                }
                case AttributeConstants.BuiltinIdentifier:
                {
                    if (attribute.Parameters.Length != 1)
                    {
                        Diagnostics.Add(DiagnosticAt.Error($"Wrong number of arguments passed to attribute \"{attribute.Identifier}\": required {1}, passed {attribute.Parameters.Length}", attribute));
                        break;
                    }

                    if (attribute.Parameters[0] is not StringLiteralExpression stringLiteral)
                    {
                        Diagnostics.Add(DiagnosticAt.Error($"Invalid parameter type for attribute \"{attribute.Identifier}\" at {0}: expected string", attribute));
                        break;
                    }

                    if (!BuiltinFunctions.Prototypes.TryGetValue(stringLiteral.Value, out BuiltinFunction? builtinFunction))
                    {
                        Diagnostics.Add(DiagnosticAt.Warning($"{AttributeConstants.BuiltinIdentifier} function \"{stringLiteral.Value}\" not found", attribute, function.File));
                        break;
                    }

                    if (builtinFunction.Parameters.Length != (function as ICompiledFunctionDefinition).Parameters.Length)
                    {
                        Diagnostics.Add(DiagnosticAt.Error($"Wrong number of arguments passed to function \"{stringLiteral.Value}\"", function.Definition.Identifier, function.File));
                    }

                    if (!builtinFunction.Type.Invoke(type))
                    {
                        Diagnostics.Add(DiagnosticAt.Error($"Wrong type defined for function \"{stringLiteral.Value}\"", (function as IHaveType)?.Type.Location ?? new Location(function.Definition.Identifier.Position, function.File)));
                    }

                    for (int i = 0; i < builtinFunction.Parameters.Length; i++)
                    {
                        if (i >= (function as ICompiledFunctionDefinition).Parameters.Length) break;

                        Predicate<GeneralType> definedParameterType = builtinFunction.Parameters[i];
                        GeneralType? passedParameterType = (function as ICompiledFunctionDefinition).Parameters[i].Type;

                        if (definedParameterType.Invoke(passedParameterType))
                        { continue; }

                        Diagnostics.Add(DiagnosticAt.Error($"Wrong type of parameter passed to function \"{stringLiteral.Value}\". Parameter index: {i} Required type: \"{definedParameterType}\" Passed: \"{passedParameterType}\"", function.Definition.Parameters[i].Type, function.Parameters[i].File));
                    }
                    break;
                }
                case AttributeConstants.ExposeIdentifier:
                {
                    if (attribute.Parameters.Length == 0)
                    {
                        break;
                    }

                    if (attribute.Parameters.Length != 1)
                    {
                        Diagnostics.Add(DiagnosticAt.Error($"Wrong number of arguments passed to attribute \"{attribute.Identifier}\": required {1}, passed {attribute.Parameters.Length}", attribute));
                        break;
                    }

                    if (attribute.Parameters[0] is not StringLiteralExpression)
                    {
                        Diagnostics.Add(DiagnosticAt.Error($"Invalid parameter type for attribute \"{attribute.Identifier}\" at {0}: expected string", attribute));
                        break;
                    }

                    break;
                }
                default:
                {
                    if (!AttributeConstants.List.Contains(attribute.Identifier.Content)
                        && !CompileUserAttribute(function, attribute))
                    {
                        Diagnostics.Add(DiagnosticAt.Warning($"Attribute `{attribute.Identifier}` not found", attribute.Identifier, attribute.File));
                    }
                    break;
                }
            }
        }
    }

    void CompileVariableAttributes(VariableDefinition variable)
    {
        foreach (AttributeUsage attribute in variable.Attributes)
        {
            switch (attribute.Identifier.Content)
            {
                case AttributeConstants.ExternalIdentifier:
                {
                    if (attribute.Parameters.Length != 1)
                    {
                        Diagnostics.Add(DiagnosticAt.Error($"Wrong number of arguments passed to attribute \"{attribute.Identifier}\": required {1}, passed {attribute.Parameters.Length}", attribute));
                        break;
                    }

                    if (attribute.Parameters[0] is not StringLiteralExpression)
                    {
                        Diagnostics.Add(DiagnosticAt.Error($"Invalid parameter type for attribute \"{attribute.Identifier}\" at {0}: expected string", attribute));
                        break;
                    }

                    break;
                }
                default:
                {
                    if (!AttributeConstants.List.Contains(attribute.Identifier.Content)
                        && !CompileUserAttribute(variable, attribute))
                    {
                        Diagnostics.Add(DiagnosticAt.Warning($"Attribute `{attribute.Identifier}` not found", attribute.Identifier, attribute.File));
                    }

                    break;
                }
            }
        }
    }

    bool CompileUserAttribute(IHaveAttributes context, AttributeUsage attribute)
    {
        foreach (UserDefinedAttribute userDefinedAttribute in UserDefinedAttributes)
        {
            if (userDefinedAttribute.Name != attribute.Identifier.Content) continue;

            if (!userDefinedAttribute.CanUseOn.HasFlag(context.AttributeUsageKind))
            { Diagnostics.Add(DiagnosticAt.Error($"Can't use attribute \"{attribute.Identifier}\" on \"{context.GetType().Name}\". Valid usages: {userDefinedAttribute.CanUseOn}", attribute)); }

            if (attribute.Parameters.Length != userDefinedAttribute.Parameters.Length)
            {
                Diagnostics.Add(DiagnosticAt.Error($"Wrong number of arguments passed to attribute \"{attribute.Identifier}\": required {userDefinedAttribute.Parameters.Length}, passed {attribute.Parameters.Length}", attribute));
                break;
            }

            for (int i = 0; i < attribute.Parameters.Length; i++)
            {
                if (attribute.Parameters[i].Type != userDefinedAttribute.Parameters[i])
                {
                    Diagnostics.Add(DiagnosticAt.Error($"Invalid parameter type \"{attribute.Parameters[i].Type}\" for attribute \"{attribute.Identifier}\" at {i}: expected \"{userDefinedAttribute.Parameters[i]}\"", attribute));
                }
            }

            if (userDefinedAttribute.Verifier is not null &&
                !userDefinedAttribute.Verifier.Invoke(context, attribute, out PossibleDiagnostic? error))
            {
                Diagnostics.Add(error.ToError(attribute));
            }

            return true;
        }

        return false;
    }

    public static void CheckExternalFunctionDeclaration<TFunction>(IRuntimeInfoProvider runtime, TFunction definition, IExternalFunction externalFunction, DiagnosticsCollection diagnostics)
        where TFunction : ICompiledFunctionDefinition, ICompiledDefinition<FunctionThingDefinition>
    {
        CheckExternalFunctionDeclaration(runtime, definition.Definition, externalFunction, definition.Type, definition.Parameters.ToImmutableArray(v => v.Type), diagnostics);
    }

    public static void CheckExternalFunctionDeclaration(IRuntimeInfoProvider runtime, FunctionThingDefinition definition, IExternalFunction externalFunction, GeneralType returnType, IReadOnlyList<GeneralType> parameterTypes, DiagnosticsCollection diagnostics)
    {
        int passedParametersSize = 0;
        int passedReturnType;

        if (returnType.SameAs(BasicType.Void))
        {
            passedReturnType = 0;
        }
        else if (!FindSize(returnType, out passedReturnType, out PossibleDiagnostic? sizeError, runtime))
        {
            diagnostics.Add(sizeError.ToError(definition));
            return;
        }

        foreach (GeneralType parameter in parameterTypes)
        {
            if (!FindSize(parameter, out int parameterSize, out PossibleDiagnostic? sizeError, runtime))
            {
                diagnostics.Add(sizeError.ToError(definition));
                return;
            }
            passedParametersSize += parameterSize;
        }

        if (externalFunction.ParametersSize != passedParametersSize)
        {
            diagnostics?.Add(DiagnosticAt.Error($"Wrong size of parameters defined ({passedParametersSize}) for external function \"{externalFunction.ToReadable()}\" {definition.ToReadable()}", definition.Identifier, definition.File));
            return;
        }

        if (externalFunction.ReturnValueSize != passedReturnType)
        {
            diagnostics?.Add(DiagnosticAt.Error($"Wrong size of return type defined ({passedReturnType}) for external function \"{externalFunction.ToReadable()}\" {definition.ToReadable()}", definition.Identifier, definition.File));
            return;
        }
    }

    bool CompileFunctionDefinition(FunctionDefinition function, CompiledStruct? context, [NotNullWhen(true)] out CompiledFunctionDefinition? result)
    {
        result = null;

        if (function.Template is not null)
        {
            GenericParameters.Push(function.Template.Parameters);
            foreach (Token typeParameter in function.Template.Parameters)
            { typeParameter.AnalyzedType = TokenAnalyzedType.TypeParameter; }
        }

        if (!CompileType(function.Type, out GeneralType? type, Diagnostics)) return false;

        ImmutableArray<CompiledParameter>.Builder parameters = ImmutableArray.CreateBuilder<CompiledParameter>(function.Parameters.Length);
        foreach (ParameterDefinition item in function.Parameters.Parameters)
        {
            if (!CompileType(item.Type, out GeneralType? parameterType, Diagnostics)) return false;
            parameters.Add(new CompiledParameter(parameterType, item));
        }

        result = new(
            type,
            parameters.MoveToImmutable(),
            context,
            function
        );

        /*
        if (type.Is(out StructType? structType) &&
            structType.Struct == GeneratorStructDefinition?.Struct)
        {
            List<GeneralType> _parameterTypes = new(parameterTypes.Length + 1)
            {
                new PointerType(new StructType(GetGeneratorState(function).Struct, function.File))
            };
            _parameterTypes.AddRange(parameterTypes);
            parameterTypes = _parameterTypes.ToArray();

            result = new(
                type,
                parameterTypes,
                context,
                new FunctionDefinition(
                    result.Attributes,
                    result.Modifiers,
                    result.TypeToken,
                    result.Identifier,
                    new ParameterDefinitionCollection(
                        Enumerable.Empty<ParameterDefinition>()
                        .Append(new ParameterDefinition(
                            new Token[] { Token.CreateAnonymous(ModifierKeywords.This) },
                            new TypeInstancePointer(new TypeInstanceSimple(Token.CreateAnonymous(GetGeneratorState(function).Struct.Identifier.Content), function.File), Token.CreateAnonymous("*"), function.File),
                            Token.CreateAnonymous("this"),
                            null
                        ))
                        .Append(result.Parameters.Select(v => v))
                        .ToArray(),
                        result.Parameters.Brackets),
                    result.Template,
                    result.File)
            );
        }
        */

        if (function.Template is not null)
        { GenericParameters.Pop(); }

        CompileFunctionAttributes(result);

        return true;
    }

    bool CompileOperatorDefinition(FunctionDefinition function, CompiledStruct? context, [NotNullWhen(true)] out CompiledOperatorDefinition? result)
    {
        result = null;

        if (!CompileType(function.Type, out GeneralType? type, Diagnostics)) return false;

        ImmutableArray<CompiledParameter>.Builder parameters = ImmutableArray.CreateBuilder<CompiledParameter>(function.Parameters.Length);
        foreach (ParameterDefinition item in function.Parameters.Parameters)
        {
            if (!CompileType(item.Type, out GeneralType? parameterType, Diagnostics)) return false;
            parameters.Add(new CompiledParameter(parameterType, item));
        }

        result = new(
            type,
            parameters.MoveToImmutable(),
            context,
            function
        );

        CompileFunctionAttributes(result);

        return true;
    }

    bool CompileGeneralFunctionDefinition(GeneralFunctionDefinition function, GeneralType returnType, CompiledStruct context, [NotNullWhen(true)] out CompiledGeneralFunctionDefinition? result)
    {
        result = null;

        ImmutableArray<CompiledParameter>.Builder parameters = ImmutableArray.CreateBuilder<CompiledParameter>(function.Parameters.Length);
        foreach (ParameterDefinition item in function.Parameters.Parameters)
        {
            if (!CompileType(item.Type, out GeneralType? parameterType, Diagnostics)) return false;
            parameters.Add(new CompiledParameter(parameterType, item));
        }

        result = new(
            returnType,
            parameters.MoveToImmutable(),
            context,
            function
        );

        CompileFunctionAttributes(result);

        return true;
    }

    bool CompileConstructorDefinition(ConstructorDefinition function, CompiledStruct context, [NotNullWhen(true)] out CompiledConstructorDefinition? result)
    {
        result = null;

        if (function.Template is not null)
        {
            GenericParameters.Push(function.Template.Parameters);
            foreach (Token typeParameter in function.Template.Parameters)
            { typeParameter.AnalyzedType = TokenAnalyzedType.TypeParameter; }
        }

        if (!CompileType(function.Type, out GeneralType? type, Diagnostics)) return false;

        ImmutableArray<CompiledParameter>.Builder parameters = ImmutableArray.CreateBuilder<CompiledParameter>(function.Parameters.Length);
        foreach (ParameterDefinition item in function.Parameters.Parameters)
        {
            if (!CompileType(item.Type, out GeneralType? parameterType, Diagnostics)) return false;
            parameters.Add(new CompiledParameter(parameterType, item));
        }

        result = new(
            type,
            parameters.MoveToImmutable(),
            context,
            function);

        if (function.Template is not null)
        { GenericParameters.Pop(); }

        CompileFunctionAttributes(result);

        return true;
    }

    void AddAST(ParsedFile collectedAST, bool addTopLevelStatements = true)
    {
        if (addTopLevelStatements)
        { TopLevelStatements.Add((collectedAST.AST.TopLevelStatements, collectedAST.File)); }

        FunctionDefinitions.AddRange(collectedAST.AST.Functions);
        OperatorDefinitions.AddRange(collectedAST.AST.Operators);
        StructDefinitions.AddRange(collectedAST.AST.Structs);
        AliasDefinitions.AddRange(collectedAST.AST.AliasDefinitions);
        EnumDefinitions.AddRange(collectedAST.AST.EnumDefinitions);
    }

    static bool ThingEquality<TThing1, TThing2>(TThing1 a, TThing2 b)
        where TThing1 : IIdentifiable<Token>, IInFile
        where TThing2 : IIdentifiable<Token>, IInFile
    {
        if (a.Identifier.Content != b.Identifier.Content) return false;
        if (a.File != b.File) return false;
        return true;
    }

    static bool FunctionEquality<TFunction>(TFunction a, TFunction b)
        where TFunction : ICompiledFunctionDefinition, ICompiledDefinition<FunctionThingDefinition>
    {
        if (!a.Type.Equals(b.Type)) return false;
        if (!Utils.SequenceEquals(a.Parameters.Select(v => v.Type), b.Parameters.Select(v => v.Type))) return false;
        if (!ThingEquality(a.Definition, b.Definition)) return false;
        return true;
    }

    bool IsSymbolDefined<TThing>(TThing thing)
        where TThing : IIdentifiable<Token>, IInFile
    {
        if (CompiledStructs.Any(other => ThingEquality(other.Definition, thing)))
        { return true; }

        if (CompiledFunctions.Any(other => ThingEquality(other.Definition, thing)))
        { return true; }

        if (CompiledAliases.Any(other => ThingEquality(other.Definition, thing)))
        { return true; }

        if (CompiledEnums.Any(other => ThingEquality(other.Definition, thing)))
        { return true; }

        return false;
    }

    void CompileDefinitions(Uri file, ImmutableArray<ParsedFile> parsedFiles)
    {
        // First compile the structs without fields
        // so it can reference other structs that are
        // not compiled but will be.
        foreach (StructDefinition @struct in StructDefinitions)
        {
            if (IsSymbolDefined(@struct))
            {
                Diagnostics.Add(DiagnosticAt.Error($"Symbol \"{@struct.Identifier}\" already exists", @struct.Identifier, @struct.File));
                continue;
            }

            CompiledStructs.Add(CompileStructNoFields(@struct));
        }

        foreach (AliasDefinition aliasDefinition in AliasDefinitions)
        {
            if (IsSymbolDefined(aliasDefinition))
            {
                Diagnostics.Add(DiagnosticAt.Error($"Symbol \"{aliasDefinition.Identifier}\" already exists", aliasDefinition.Identifier, aliasDefinition.File));
                continue;
            }

            if (!CompileStatement(aliasDefinition.Value, out CompiledTypeExpression? aliasValue, Diagnostics)) continue;

            CompiledAliases.Add(new CompiledAlias(
                aliasValue,
                aliasDefinition
            ));
        }

        foreach (EnumDefinition enumDefinition in EnumDefinitions)
        {
            if (IsSymbolDefined(enumDefinition))
            {
                Diagnostics.Add(DiagnosticAt.Error($"Symbol \"{enumDefinition.Identifier}\" already exists", enumDefinition.Identifier, enumDefinition.File));
                continue;
            }

            GeneralType? enumType = null;
            if (enumDefinition.Type is not null && !CompileType(enumDefinition.Type, out enumType, Diagnostics))
            {
                enumType = null;
            }

            List<CompiledEnumMember> compiledMembers = new(enumDefinition.Members.Length);

            bool? IsEnumMemberUnique(CompiledValue constantValue, out PossibleDiagnostic? warning)
            {
                foreach (CompiledEnumMember otherMember in compiledMembers)
                {
                    if (otherMember.Value is not CompiledConstantValue otherConstantValue)
                    {
                        warning = new PossibleDiagnostic($"Cannot check if the enum member is unique, because not all members has a numeric value",
                            new PossibleDiagnostic($"Enum member \"{otherMember.Identifier}\" doesn't have a numeric value", otherMember));
                        return null;
                    }

                    if (otherConstantValue.Value == constantValue)
                    {
                        warning = new PossibleDiagnostic($"Enum member conflicts with \"{otherMember.Identifier}\"");
                        return false;
                    }
                }

                warning = null;
                return true;
            }

            CompiledExpression? lastValue = null;
            foreach (EnumMemberDefinition member in enumDefinition.Members)
            {
                CompiledExpression? value;
                if (member.Value is null)
                {
                    if (lastValue is null)
                    {
                        if (enumType is not null
                            && enumType.Is(out BuiltinType? builtinEnumType)
                            && builtinEnumType.RuntimeType != RuntimeType.Null)
                        {
                            lastValue = new CompiledConstantValue()
                            {
                                Value = builtinEnumType.RuntimeType switch
                                {
                                    RuntimeType.Null => throw new UnreachableException(),
                                    RuntimeType.U8 => CompiledValue.CreateUnsafe(0, RuntimeType.U8),
                                    RuntimeType.I8 => CompiledValue.CreateUnsafe(0, RuntimeType.I8),
                                    RuntimeType.U16 => CompiledValue.CreateUnsafe(0, RuntimeType.U16),
                                    RuntimeType.I16 => CompiledValue.CreateUnsafe(0, RuntimeType.I16),
                                    RuntimeType.U32 => CompiledValue.CreateUnsafe(0, RuntimeType.U32),
                                    RuntimeType.I32 => CompiledValue.CreateUnsafe(0, RuntimeType.I32),
                                    RuntimeType.F32 => CompiledValue.CreateUnsafe(0, RuntimeType.F32),
                                    _ => throw new UnreachableException(),
                                },
                                Type = enumType,
                                Location = member.Location,
                                SaveValue = true,
                            };
                        }
                        else
                        {
                            Diagnostics.Add(DiagnosticAt.Error($"Can't guess the enum member value, because there are no previous enum member values", member));
                            continue;
                        }
                    }

                    if (lastValue is not CompiledConstantValue lastConstValue)
                    {
                        Diagnostics.Add(DiagnosticAt.Error($"Can't guess the next enum member value, because the previous one was not a numeric value", member));
                        continue;
                    }

                    CompiledValue constLastValue = lastConstValue.Value;
                    RuntimeType constLastType = constLastValue.Type;

                    while (!IsEnumMemberUnique(constLastValue, out _) ?? false)
                    {
                        constLastValue += 1;
                    }

                    if (!constLastValue.TryCast(constLastType, out CompiledValue castedConstLastValue))
                    {
                        Diagnostics.Add(DiagnosticAt.Error($"Can't cast constant value {constLastValue} of type {constLastValue.Type} to {constLastType}", member));
                    }
                    else
                    {
                        constLastValue = castedConstLastValue;
                    }

                    value = new CompiledConstantValue()
                    {
                        Type = constLastValue.Type switch
                        {
                            RuntimeType.Null => BuiltinType.Void,
                            RuntimeType.U8 => BuiltinType.U8,
                            RuntimeType.I8 => BuiltinType.I8,
                            RuntimeType.U16 => BuiltinType.Char,
                            RuntimeType.I16 => BuiltinType.I16,
                            RuntimeType.U32 => BuiltinType.U32,
                            RuntimeType.I32 => BuiltinType.I32,
                            RuntimeType.F32 => BuiltinType.F32,
                            _ => throw new UnreachableException(),
                        },
                        Value = constLastValue,
                        Location = member.Location.After(),
                        SaveValue = true,
                    };
                }
                else if (!CompileExpression(member.Value, out value, enumType))
                {
                    continue;
                }

                if (enumType is null)
                {
                    enumType = value.Type;
                }
                else if (!CanCastImplicitly(value, enumType, out CompiledExpression? assignedValue, out PossibleDiagnostic? castError))
                {
                    Diagnostics.Add(castError.ToError(value));
                    value = assignedValue;
                }

                if (TryCompute(value, out CompiledValue constValue, out PossibleDiagnostic? evaluationError))
                {
                    if (!enumType.Is(out BuiltinType? enumBuiltinType))
                    {
                        Diagnostics.Add(DiagnosticAt.Error($"Const enum must have a built-in type", enumDefinition.Type?.Location ?? enumDefinition.Location));
                    }
                    else if (!constValue.TryCast(enumBuiltinType.RuntimeType, out CompiledValue castedConstantValue))
                    {
                        Diagnostics.Add(DiagnosticAt.Error($"Can't cast constant value {constValue} of type {constValue.Type} to {enumBuiltinType}", value));
                        value = new CompiledConstantValue()
                        {
                            Type = constValue.Type switch
                            {
                                RuntimeType.Null => BuiltinType.Void,
                                RuntimeType.U8 => BuiltinType.U8,
                                RuntimeType.I8 => BuiltinType.I8,
                                RuntimeType.U16 => BuiltinType.Char,
                                RuntimeType.I16 => BuiltinType.I16,
                                RuntimeType.U32 => BuiltinType.U32,
                                RuntimeType.I32 => BuiltinType.I32,
                                RuntimeType.F32 => BuiltinType.F32,
                                _ => throw new UnreachableException(),
                            },
                            Value = constValue,
                            Location = value.Location,
                            SaveValue = value.SaveValue,
                        };
                    }
                    else
                    {
                        constValue = castedConstantValue;
                        value = new CompiledConstantValue()
                        {
                            Type = enumBuiltinType,
                            Value = constValue,
                            Location = value.Location,
                            SaveValue = value.SaveValue,
                        };
                    }

                    IsEnumMemberUnique(constValue, out PossibleDiagnostic? uniqueWarning);
                    if (uniqueWarning is not null) Diagnostics.Add(uniqueWarning.ToWarning(member));
                }
                else
                {
                    Diagnostics.Add(DiagnosticAt.Error($"Enum member value must be a constant", value));
                    continue;
                }

                lastValue = value;

                compiledMembers.Add(new CompiledEnumMember(value, member));
            }

            if (enumType is null)
            {
                Diagnostics.Add(DiagnosticAt.Error($"Cannot guess the enum member type", enumDefinition));
                enumType = BuiltinType.Any;
            }

            CompiledEnums.Add(new CompiledEnum(
                enumType,
                compiledMembers.ToImmutableArray(),
                enumDefinition
            ));
        }

        // Now compile the fields. Now every struct is compiled
        // so it can reference other structs.
        foreach (CompiledStruct @struct in CompiledStructs)
        {
            CompileStructFields(@struct);
        }

        foreach (CompiledStruct @struct in CompiledStructs)
        {
            foreach (AttributeUsage attribute in @struct.Definition.Attributes)
            {
                CompileUserAttribute(@struct, attribute);
            }

            foreach (CompiledField field in @struct.Fields)
            {
                foreach (AttributeUsage attribute in field.Definition.Attributes)
                {
                    CompileUserAttribute(field, attribute);
                }
            }
        }

        foreach (FunctionDefinition @operator in OperatorDefinitions)
        {
            if (!CompileOperatorDefinition(@operator, null, out CompiledOperatorDefinition? compiled))
            {
                continue;
            }

            if (CompiledOperators.Any(other => FunctionEquality(compiled, other)))
            {
                Diagnostics.Add(DiagnosticAt.Error($"Operator \"{compiled.ToReadable()}\" already defined", @operator.Identifier, @operator.File));
                continue;
            }

            CompiledOperators.Add(compiled);
        }

        foreach (FunctionDefinition function in FunctionDefinitions)
        {
            if (!CompileFunctionDefinition(function, null, out CompiledFunctionDefinition? compiled))
            {
                continue;
            }

            if (CompiledFunctions.Any(other => FunctionEquality(compiled, other)))
            {
                Diagnostics.Add(DiagnosticAt.Error($"Function \"{compiled.ToReadable()}\" already defined", function.Identifier, function.File));
                continue;
            }

            CompiledFunctions.Add(compiled);
        }

        foreach (CompiledStruct compiledStruct in CompiledStructs)
        {
            if (compiledStruct.Definition.Template is not null)
            {
                GenericParameters.Push(compiledStruct.Definition.Template.Parameters);
                foreach (Token typeParameter in compiledStruct.Definition.Template.Parameters)
                { typeParameter.AnalyzedType = TokenAnalyzedType.TypeParameter; }
            }

            foreach (GeneralFunctionDefinition method in compiledStruct.Definition.GeneralFunctions)
            {
                foreach (ParameterDefinition parameter in method.Parameters.Parameters)
                {
                    if (parameter.Modifiers.Contains(ModifierKeywords.This))
                    {
                        Diagnostics.Add(DiagnosticAt.Error($"Keyword \"{ModifierKeywords.This}\" is not valid in the current context", parameter.Identifier, compiledStruct.Definition.File));
                        continue;
                    }
                }

                GeneralType returnType = new StructType(compiledStruct, method.File);

                if (method.Identifier.Content == BuiltinFunctionIdentifiers.Destructor)
                {
                    ImmutableArray<ParameterDefinition> parameters = method.Parameters.Parameters.Insert(0, new ParameterDefinition(
                        ImmutableArray.Create(Token.CreateAnonymous(ModifierKeywords.This)),
                        TypeInstanceSimple.CreateAnonymous(compiledStruct.Identifier, method.File, compiledStruct.Definition.Template?.Parameters),
                        Token.CreateAnonymous(StatementKeywords.This),
                        null,
                        method.File
                    ));

                    GeneralFunctionDefinition copy = new(
                        method.Identifier,
                        method.Modifiers,
                        new ParameterDefinitionCollection(parameters, method.Parameters.Brackets, method.File),
                        method.Block,
                        method.File)
                    {
                        Context = method.Context,
                    };

                    returnType = BuiltinType.Void;

                    if (!CompileGeneralFunctionDefinition(copy, returnType, compiledStruct, out CompiledGeneralFunctionDefinition? methodWithRef))
                    {
                        continue;
                    }

                    parameters = method.Parameters.Parameters.Insert(0, new ParameterDefinition(
                        ImmutableArray.Create(Token.CreateAnonymous(ModifierKeywords.This)),
                        TypeInstancePointer.CreateAnonymous(TypeInstanceSimple.CreateAnonymous(compiledStruct.Identifier, method.File, compiledStruct.Definition.Template?.Parameters), method.File),
                        Token.CreateAnonymous(StatementKeywords.This),
                        null,
                        method.File
                    ));

                    copy = new GeneralFunctionDefinition(
                        method.Identifier,
                        method.Modifiers,
                        new ParameterDefinitionCollection(parameters, method.Parameters.Brackets, method.File),
                        method.Block,
                        method.File)
                    {
                        Context = method.Context,
                    };

                    if (!CompileGeneralFunctionDefinition(copy, returnType, compiledStruct, out CompiledGeneralFunctionDefinition? methodWithPointer))
                    {
                        continue;
                    }

                    if (CompiledGeneralFunctions.Any(methodWithRef.IsSame))
                    {
                        Diagnostics.Add(DiagnosticAt.Error($"Function with name \"{methodWithRef.ToReadable()}\" already defined", method.Identifier, compiledStruct.Definition.File));
                        continue;
                    }

                    if (CompiledGeneralFunctions.Any(methodWithPointer.IsSame))
                    {
                        Diagnostics.Add(DiagnosticAt.Error($"Function with name \"{methodWithPointer.ToReadable()}\" already defined", method.Identifier, compiledStruct.Definition.File));
                        continue;
                    }

                    CompiledGeneralFunctions.Add(methodWithRef);
                    CompiledGeneralFunctions.Add(methodWithPointer);
                }
                else
                {
                    if (!CompileGeneralFunctionDefinition(method, returnType, compiledStruct, out CompiledGeneralFunctionDefinition? methodWithRef))
                    {
                        continue;
                    }

                    if (CompiledGeneralFunctions.Any(methodWithRef.IsSame))
                    {
                        Diagnostics.Add(DiagnosticAt.Error($"Function with name \"{methodWithRef.ToReadable()}\" already defined", method.Identifier, compiledStruct.Definition.File));
                        continue;
                    }

                    CompiledGeneralFunctions.Add(methodWithRef);
                }
            }

            foreach (FunctionDefinition method in compiledStruct.Definition.Functions)
            {
                foreach (ParameterDefinition parameter in method.Parameters.Parameters)
                {
                    if (parameter.Modifiers.Contains(ModifierKeywords.This))
                    { Diagnostics.Add(DiagnosticAt.Error($"Keyword \"{ModifierKeywords.This}\" is not valid in the current context", parameter.Identifier, compiledStruct.Definition.File)); }
                }

                ImmutableArray<ParameterDefinition> parameters = method.Parameters.Parameters.Insert(0, new ParameterDefinition(
                    ImmutableArray.Create(Token.CreateAnonymous(ModifierKeywords.This)),
                    TypeInstancePointer.CreateAnonymous(TypeInstanceSimple.CreateAnonymous(compiledStruct.Identifier, method.File, compiledStruct.Definition.Template?.Parameters), method.File),
                    Token.CreateAnonymous(StatementKeywords.This),
                    null,
                    method.File
                ));

                FunctionDefinition copy = new(
                    method.Attributes,
                    method.Modifiers,
                    method.Type,
                    method.Identifier,
                    new ParameterDefinitionCollection(parameters, method.Parameters.Brackets, method.File),
                    method.Template,
                    method.Block,
                    method.File)
                {
                    Context = method.Context,
                };

                if (!CompileFunctionDefinition(copy, compiledStruct, out CompiledFunctionDefinition? methodWithPointer))
                {
                    continue;
                }

                if (CompiledFunctions.Any(methodWithPointer.IsSame))
                {
                    Diagnostics.Add(DiagnosticAt.Error($"Function with name \"{methodWithPointer.ToReadable()}\" already defined", method.Identifier, compiledStruct.Definition.File));
                    continue;
                }

                CompiledFunctions.Add(methodWithPointer);
            }

            foreach (ConstructorDefinition constructor in compiledStruct.Definition.Constructors)
            {
                foreach (ParameterDefinition parameter in constructor.Parameters.Parameters)
                {
                    if (parameter.Modifiers.Contains(ModifierKeywords.This))
                    {
                        Diagnostics.Add(DiagnosticAt.Error($"Keyword \"{ModifierKeywords.This}\" is not valid in the current context", parameter.Identifier, compiledStruct.Definition.File));
                        continue;
                    }
                }

                ImmutableArray<ParameterDefinition> parameters = constructor.Parameters.Parameters.Insert(0, new ParameterDefinition(
                    ImmutableArray.Create(Token.CreateAnonymous(ModifierKeywords.This)),
                    TypeInstancePointer.CreateAnonymous(TypeInstanceSimple.CreateAnonymous(compiledStruct.Identifier, constructor.File, compiledStruct.Definition.Template?.Parameters), constructor.File),
                    Token.CreateAnonymous(StatementKeywords.This),
                    null,
                    constructor.File
                ));

                ConstructorDefinition constructorWithThisParameter = new(
                    constructor.Type,
                    constructor.Modifiers,
                    new ParameterDefinitionCollection(parameters, constructor.Parameters.Brackets, constructor.File),
                    constructor.Block,
                    constructor.File
                )
                {
                    Context = constructor.Context,
                };

                if (!CompileConstructorDefinition(constructorWithThisParameter, compiledStruct, out CompiledConstructorDefinition? compiledConstructor))
                {
                    continue;
                }

                if (CompiledConstructors.Any(compiledConstructor.IsSame))
                {
                    Diagnostics.Add(DiagnosticAt.Error($"Constructor \"{compiledConstructor.ToReadable()}\" already defined", constructor.Type, compiledStruct.Definition.File));
                    continue;
                }

                CompiledConstructors.Add(compiledConstructor);
            }

            foreach (FunctionDefinition @operator in compiledStruct.Definition.Operators)
            {
                if (!CompileOperatorDefinition(@operator, null, out CompiledOperatorDefinition? compiled))
                {
                    continue;
                }

                if (CompiledOperators.Any(other => FunctionEquality(compiled, other)))
                {
                    Diagnostics.Add(DiagnosticAt.Error($"Operator \"{compiled.ToReadable()}\" already defined", @operator.Identifier, @operator.File));
                    continue;
                }

                CompiledOperators.Add(compiled);
            }

            if (compiledStruct.Definition.Template is not null)
            { GenericParameters.Pop(); }
        }
    }

    CompilerResult CompileMainFile(string file)
    {
        SourceCodeManagerResult res = SourceCodeManager.Collect(file, Diagnostics, PreprocessorVariables, Settings.AdditionalImports, Settings.SourceProviders, Settings.TokenizerSettings, Settings.Cache, Logger);

        foreach (ParsedFile parsedFile in res.ParsedFiles)
        { AddAST(parsedFile, parsedFile.File != res.ResolvedEntry); }

        foreach (ParsedFile parsedFile in res.ParsedFiles)
        {
            if (parsedFile.File != res.ResolvedEntry) continue;
            TopLevelStatements.Add((parsedFile.AST.TopLevelStatements, parsedFile.File));
        }

        // This should not be null ...
        if (res.ResolvedEntry is null) throw new InternalExceptionWithoutContext($"I can't really explain this error ...");

        if (Diagnostics.HasErrors)
        { return CompilerResult.MakeEmpty(res.ResolvedEntry); }

        return CompileInternal(res.ResolvedEntry, res.ParsedFiles);
    }

    CompilerResult CompileFiles(ReadOnlySpan<string> files)
    {
        SourceCodeManagerResult res = SourceCodeManager.CollectMultiple(files, Diagnostics, PreprocessorVariables, Settings.AdditionalImports, Settings.SourceProviders, Settings.TokenizerSettings, Settings.Cache, Logger);

        foreach (ParsedFile parsedFile in res.ParsedFiles)
        { AddAST(parsedFile, parsedFile.File != res.ResolvedEntry); }

        foreach (ParsedFile parsedFile in res.ParsedFiles)
        {
            if (parsedFile.File != res.ResolvedEntry) continue;
            TopLevelStatements.Add((parsedFile.AST.TopLevelStatements, parsedFile.File));
        }

        // This should not be null ...
        if (res.ResolvedEntry is null) throw new InternalExceptionWithoutContext($"I can't really explain this error ...");
        return CompileInternal(res.ResolvedEntry, res.ParsedFiles);
    }

    CompilerResult CompileExpressionInternal(string expression, CompilerResult previous)
    {
        Uri entryFile = new("void:///");
        TokenizerResult expressionTokens = Tokenizer.Tokenize(
            expression,
            Diagnostics,
            entryFile,
            PreprocessorVariables,
            Settings.TokenizerSettings
        );

        ParserResult expressionAst = Parser.Parser.ParseExpression(expressionTokens.Tokens, entryFile, Diagnostics);

        ParsedFile parsedExpression = new(entryFile, null, expressionTokens, expressionAst, new ImportIndex(), expression);

        if (expressionAst.Usings.Any())
        {
            Diagnostics.Add(DiagnosticAt.Error($"Cannot import files from an interactive expression", expressionAst.Usings.First()));
            return CompilerResult.MakeEmpty(entryFile);
        }

        CompiledStructs.Set(previous.Structs);
        CompiledAliases.Set(previous.Aliases);
        CompiledEnums.Set(previous.Enums);
        CompiledOperators.Set(previous.OperatorDefinitions);
        CompiledFunctions.Set(previous.FunctionDefinitions);
        CompiledGeneralFunctions.Set(previous.GeneralFunctionDefinitions);
        CompiledConstructors.Set(previous.ConstructorDefinitions);

        //if (parsedExpression.AST.TopLevelStatements.Length > 1)
        //{
        //    Diagnostics.Add(DiagnosticAt.Error($"Expression should consists of one value only", parsedExpression.AST.TopLevelStatements[1]));
        //    return CompilerResult.MakeEmpty(entryFile);
        //}
        //else if (parsedExpression.AST.TopLevelStatements.Length == 0)
        //{
        //    Diagnostics.Add(DiagnosticAt.Error($"Expression doesn't have any values", new Location(Position.Zero, entryFile)));
        //    return CompilerResult.MakeEmpty(entryFile);
        //}

        if (parsedExpression.AST.Functions.Length > 0) { Diagnostics.Add(DiagnosticAt.Error($"No function definitions allowed", parsedExpression.AST.Functions[0])); }
        if (parsedExpression.AST.Operators.Length > 0) { Diagnostics.Add(DiagnosticAt.Error($"No operator definitions allowed", parsedExpression.AST.Operators[0])); }
        if (parsedExpression.AST.AliasDefinitions.Length > 0) { Diagnostics.Add(DiagnosticAt.Error($"No alias definitions allowed", new Location(parsedExpression.AST.AliasDefinitions[0].Position, parsedExpression.AST.AliasDefinitions[0].File))); }
        if (parsedExpression.AST.EnumDefinitions.Length > 0) { Diagnostics.Add(DiagnosticAt.Error($"No enum definitions allowed", new Location(parsedExpression.AST.EnumDefinitions[0].Position, parsedExpression.AST.EnumDefinitions[0].File))); }
        if (parsedExpression.AST.Structs.Length > 0) { Diagnostics.Add(DiagnosticAt.Error($"No struct definitions allowed", new Location(parsedExpression.AST.Structs[0].Position, parsedExpression.AST.Structs[0].File))); }

        TopLevelStatements.Add((parsedExpression.AST.TopLevelStatements, parsedExpression.File));

        return CompileInternal(entryFile, ImmutableArray.Create(parsedExpression), false);
    }

#if UNITY
    static readonly Unity.Profiling.ProfilerMarker _m3 = new("LanguageCore.Compiler");
#endif
    CompilerResult CompileInternal(Uri file, ImmutableArray<ParsedFile> parsedFiles, bool compileDefinitions = true)
    {
#if UNITY
        using var _1 = _m3.Auto();
#endif

        using (Frames.PushAuto(CompiledFrame.Empty))
        {
            if (compileDefinitions) CompileDefinitions(file, parsedFiles);

            GenerateCode(
                parsedFiles,
                file
            );
        }

        return new CompilerResult(
            parsedFiles,
            CompiledFunctions.ToImmutableArray(),
            CompiledGeneralFunctions.ToImmutableArray(),
            CompiledOperators.ToImmutableArray(),
            CompiledConstructors.ToImmutableArray(),
            CompiledAliases.ToImmutableArray(),
            CompiledEnums.ToImmutableArray(),
            ExternalFunctions,
            CompiledStructs.ToImmutableArray(),
            TopLevelStatements.ToImmutableArray(),
            file,
            Settings.IsExpression,
            CompiledTopLevelStatements.ToImmutable(),
            GeneratedFunctions.ToImmutableArray()
        );
    }

    public static CompilerResult CompileFiles(
        ReadOnlySpan<string> files,
        CompilerSettings settings,
        DiagnosticsCollection diagnostics,
        ILogger? logger = null)
    {
        StatementCompiler compiler = new(settings, diagnostics, logger);
        return compiler.CompileFiles(files);
    }

    public static CompilerResult CompileFile(
        string file,
        CompilerSettings settings,
        DiagnosticsCollection diagnostics,
        ILogger? logger = null)
    {
        StatementCompiler compiler = new(settings, diagnostics, logger);
        return compiler.CompileMainFile(file);
    }

    public static CompilerResult CompileExpression(
        string expression,
        CompilerSettings settings,
        DiagnosticsCollection diagnostics,
        CompilerResult previous,
        ILogger? logger = null)
    {
        StatementCompiler compiler = new(settings, diagnostics, logger);
        return compiler.CompileExpressionInternal(expression, previous);
    }
}
