using LanguageCore.Parser;

namespace LanguageCore.Compiler;

public partial class StatementCompiler
{
    public static class FunctionQuery
    {
        [DebuggerStepThrough]
        public static FunctionQuery<TFunction, TIdentifier, TDefinedIdentifier, GeneralType> Create<TFunction, TIdentifier, TDefinedIdentifier>(
            TIdentifier? identifier,
            ImmutableArray<GeneralType>? arguments = null,
            Uri? relevantFile = null,
            GeneralType? returnType = null,
            Action<TemplateInstance<TFunction>>? addCompilable = null)
            where TFunction : notnull
            => new()
            {
                Identifier = identifier,
                Arguments = arguments,
                ArgumentCount = arguments?.Length,
                Converter = v => v,
                RelevantFile = relevantFile,
                ReturnType = returnType,
                AddCompilable = addCompilable,
            };

        [DebuggerStepThrough]
        public static FunctionQuery<TFunction, TIdentifier, TDefinedIdentifier, CompiledExpression> Create<TFunction, TIdentifier, TDefinedIdentifier>(
            TIdentifier? identifier,
            ImmutableArray<CompiledExpression> arguments,
            Uri? relevantFile = null,
            GeneralType? returnType = null,
            Action<TemplateInstance<TFunction>>? addCompilable = null)
            where TFunction : notnull
            => new()
            {
                Identifier = identifier,
                Arguments = arguments,
                ArgumentCount = arguments.Length,
                Converter = v => v.Type,
                RelevantFile = relevantFile,
                ReturnType = returnType,
                AddCompilable = addCompilable,
            };

        [DebuggerStepThrough]
        public static FunctionQuery<TFunction, TIdentifier, TDefinedIdentifier, GeneralType> Create<TFunction, TIdentifier, TDefinedIdentifier>(
            TIdentifier? identifier,
            ImmutableArray<GeneralType> arguments,
            Uri? relevantFile = null,
            GeneralType? returnType = null,
            Action<TemplateInstance<TFunction>>? addCompilable = null,
            FunctionQueryIdentifierMatcher<TIdentifier, TDefinedIdentifier>? identifierMatcher = null)
            where TFunction : notnull
            => new()
            {
                Identifier = identifier,
                Arguments = arguments,
                ArgumentCount = arguments.Length,
                Converter = v => v,
                RelevantFile = relevantFile,
                ReturnType = returnType,
                AddCompilable = addCompilable,
                IdentifierMatcher = identifierMatcher,
            };

        [DebuggerStepThrough]
        public static FunctionQuery<TFunction, TIdentifier, TDefinedIdentifier, CompiledExpression> Create<TFunction, TIdentifier, TDefinedIdentifier>(
            TIdentifier? identifier,
            ImmutableArray<CompiledExpression> arguments,
            Uri? relevantFile = null,
            GeneralType? returnType = null,
            Action<TemplateInstance<TFunction>>? addCompilable = null,
            FunctionQueryIdentifierMatcher<TIdentifier, TDefinedIdentifier>? identifierMatcher = null)
            where TFunction : notnull
            => new()
            {
                Identifier = identifier,
                Arguments = arguments,
                ArgumentCount = arguments.Length,
                Converter = v => v.Type,
                RelevantFile = relevantFile,
                ReturnType = returnType,
                AddCompilable = addCompilable,
                IdentifierMatcher = identifierMatcher,
            };
    }

    public readonly struct FunctionQuery<TFunction, TIdentifier, TDefinedIdentifier, TArgument>
        where TFunction : notnull
    {
        public TIdentifier? Identifier { get; init; }
        public Uri? RelevantFile { get; init; }
        public ImmutableArray<TArgument>? Arguments { get; init; }
        public int? ArgumentCount { get; init; }
        public GeneralType? ReturnType { get; init; }
        public Action<TemplateInstance<TFunction>>? AddCompilable { get; init; }
        public Func<TArgument, GeneralType> Converter { get; init; }
        public FunctionQueryIdentifierMatcher<TIdentifier, TDefinedIdentifier>? IdentifierMatcher { get; init; }
    }

    public class FunctionQueryResult<TFunction> where TFunction : notnull
    {
        public required TFunction Function { get; init; }
        public required ImmutableArray<CompiledExpression?> Arguments { get; init; }
        public ImmutableDictionary<string, GeneralType>? TypeArguments { get; init; }
        public bool Success { get; init; }
        public bool DidReplaceArguments => !Arguments.IsDefault && Arguments.Any(v => v is not null);

        public void ReplaceArgumentsIfNeeded(ref ImmutableArray<CompiledExpression> arguments)
        {
            if (Arguments.IsDefault) return;
            ImmutableArray<CompiledExpression>.Builder newArguments = ImmutableArray.CreateBuilder<CompiledExpression>(arguments.Length);
            for (int i = 0; i < arguments.Length; i++)
            {
                newArguments.Add(Arguments[i] ?? arguments[i]);
            }
            arguments = newArguments.MoveToImmutable();
        }

        public void Deconstruct(
            out TFunction function,
            out ImmutableDictionary<string, GeneralType>? typeArguments)
        {
            function = Function;
            typeArguments = TypeArguments;
        }

        public override string? ToString() => Function.ToString();
    }

    public enum TypeMatch
    {
        None,
        Promotion,
        ImplicitCastRef,
        ImplicitCast,
        SameRef,
        Same,
        EqualsRef,
        Equals,
    }

    class MatchList<TMatch, TDefinition> : IEnumerable<TMatch>
        where TMatch : notnull, DefinitionMatch<TDefinition>
        where TDefinition : notnull
    {
        readonly List<TMatch> _items = new();

        public int Count => _items.Count;

        public TMatch this[int i] => _items[i];
        public TMatch this[Index i] => _items[i];

        public void Add(TMatch match)
        {
            _items.AddSorted(match);
            for (int i = 1; i < _items.Count; i++)
            {
                if (_items[0].CompareTo(_items[i]) == -1)
                {
                    _items.RemoveAt(i--);
                }
            }
        }

        public IEnumerator<TMatch> GetEnumerator() => _items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
    }

    class DefinitionMatch<TDefinition> :
        IComparable<DefinitionMatch<TDefinition>>,
        IEquatable<DefinitionMatch<TDefinition>>
        where TDefinition : notnull
    {
        public required TDefinition Definition { get; init; }
        public required List<PossibleDiagnostic> Errors { get; init; }

        public bool IsIdentifierMatched { get; set; }
        public int IdentifierBadness { get; set; }
        public bool IsFileMatches { get; set; }
        public bool IsGenericParameterCountMatches { get; set; }
        public bool IsProtectionRespected { get; set; }

        protected const int Better = -1;
        protected const int Same = 0;
        protected const int Worse = 1;

        public virtual int CompareTo(DefinitionMatch<TDefinition>? other)
        {
            if (Equals(other)) return Same;
            if (other is null) return Better;

            if (IsIdentifierMatched && !other.IsIdentifierMatched) return Better;
            if (!IsIdentifierMatched && other.IsIdentifierMatched) return Worse;

            if (IdentifierBadness < other.IdentifierBadness) return Better;
            if (IdentifierBadness > other.IdentifierBadness) return Worse;
            if (!IsIdentifierMatched || !other.IsIdentifierMatched) return Same;

            if (IsGenericParameterCountMatches && !other.IsGenericParameterCountMatches) return Better;
            if (!IsGenericParameterCountMatches && other.IsGenericParameterCountMatches) return Worse;
            if (!IsGenericParameterCountMatches || !other.IsGenericParameterCountMatches) return Same;

            if (IsProtectionRespected && !other.IsProtectionRespected) return Better;
            if (!IsProtectionRespected && other.IsProtectionRespected) return Worse;

            if (IsFileMatches && !other.IsFileMatches) return Better;
            if (!IsFileMatches && other.IsFileMatches) return Worse;

            return Same;
        }

        public virtual bool Equals(DefinitionMatch<TDefinition>? other)
        {
            if (other is null) return false;
            if (IdentifierBadness != other.IdentifierBadness) return false;
            if (IsIdentifierMatched != other.IsIdentifierMatched) return false;
            if (IsFileMatches != other.IsFileMatches) return false;
            if (IsGenericParameterCountMatches != other.IsGenericParameterCountMatches) return false;
            return true;
        }

        public override string? ToString() => Definition.ToString();
    }

    class FunctionMatch<TDefinition> : DefinitionMatch<TDefinition>,
        IComparable<FunctionMatch<TDefinition>>,
        IEquatable<FunctionMatch<TDefinition>>
        where TDefinition : notnull
    {
        public bool IsParameterCountMatches { get; set; }

        public TypeMatch ReturnTypeMatch { get; set; }
        public int UsedUpDefaultParameterValues { get; set; }
        public TypeMatch? ParameterTypeMatch { get; set; }

        public ImmutableDictionary<string, GeneralType>? TypeArguments { get; set; }
        public ImmutableArray<CompiledExpression?> Arguments { get; set; }

        public override int CompareTo(DefinitionMatch<TDefinition>? other) => CompareTo(other as FunctionMatch<TDefinition>);
        public override bool Equals(DefinitionMatch<TDefinition>? other) => Equals(other as FunctionMatch<TDefinition>);

        public int CompareTo(FunctionMatch<TDefinition>? other)
        {
            if (other is null) return Better;
            if (Equals(other)) return Same;

            if (IsIdentifierMatched && !other.IsIdentifierMatched) return Better;
            if (!IsIdentifierMatched && other.IsIdentifierMatched) return Worse;

            if (IdentifierBadness < other.IdentifierBadness) return Better;
            if (IdentifierBadness > other.IdentifierBadness) return Worse;
            if (!IsIdentifierMatched || !other.IsIdentifierMatched) return Same;

            if (IsParameterCountMatches && !other.IsParameterCountMatches) return Better;
            if (!IsParameterCountMatches && other.IsParameterCountMatches) return Worse;
            if (!IsParameterCountMatches || !other.IsParameterCountMatches) return Same;

            if (ParameterTypeMatch is not null && other.ParameterTypeMatch is not null)
            {
                TypeMatch a = ParameterTypeMatch.Value;
                TypeMatch b = other.ParameterTypeMatch.Value;
                if (a > b) return Better;
                if (a < b) return Worse;
                if (a == TypeMatch.None || b == TypeMatch.None) return Same;
            }

            if (ReturnTypeMatch > other.ReturnTypeMatch) return Better;
            if (ReturnTypeMatch < other.ReturnTypeMatch) return Worse;
            if (ReturnTypeMatch == TypeMatch.None || other.ReturnTypeMatch == TypeMatch.None) return Same;

            if (TypeArguments is null && other.TypeArguments is not null) return Better;
            if (TypeArguments is not null && other.TypeArguments is null) return Worse;

            if (UsedUpDefaultParameterValues < other.UsedUpDefaultParameterValues) return Better;
            if (UsedUpDefaultParameterValues > other.UsedUpDefaultParameterValues) return Worse;

            if (IsProtectionRespected && !other.IsProtectionRespected) return Better;
            if (!IsProtectionRespected && other.IsProtectionRespected) return Worse;

            if (IsFileMatches && !other.IsFileMatches) return Better;
            if (!IsFileMatches && other.IsFileMatches) return Worse;

            return Same;
        }

        public bool Equals(FunctionMatch<TDefinition>? other)
        {
            if (other is null) return false;
            if (!base.Equals(other)) return false;
            if (IsParameterCountMatches != other.IsParameterCountMatches) return false;
            if (ReturnTypeMatch != other.ReturnTypeMatch) return false;
            if (UsedUpDefaultParameterValues != other.UsedUpDefaultParameterValues) return false;
            if ((ParameterTypeMatch is null) != (other.ParameterTypeMatch is null)) return false;
            if (ParameterTypeMatch is null || other.ParameterTypeMatch is null) return false;
            if (ParameterTypeMatch.Value != other.ParameterTypeMatch.Value) return false;
            if ((TypeArguments is null) != (other.TypeArguments is null)) return false;
            if (TypeArguments is null || other.TypeArguments is null) return false;
            if (!Utils.SequenceEquals(TypeArguments, other.TypeArguments, (a, b) => a.Key == b.Key && a.Value.Equals(b.Value))) return false;
            return true;
        }
    }

    public delegate bool FunctionQueryIdentifierMatcher<TIdentifier, TDefinedIdentifier>(
        TIdentifier passed,
        TDefinedIdentifier defined,
        out int badness);

    public bool GetFunction<TFunction, TPassedIdentifier, TDefinedIdentifier, TArgument>(
        Functions<TFunction> functions,
        string kindName,
        string readableName,

        FunctionQuery<TFunction, TPassedIdentifier, TDefinedIdentifier, TArgument> query,

        [NotNullWhen(true)] out FunctionQueryResult<TFunction>? result,
        [NotNullWhen(false)] out PossibleDiagnostic? error)
        where TFunction : class, ICompiledFunctionDefinition, IIdentifiable<TDefinedIdentifier>, ICompiledDefinition<FunctionThingDefinition>
        where TDefinedIdentifier : notnull, IEquatable<TPassedIdentifier>
        where TArgument : notnull
    {
        string kindNameLower = kindName.ToLowerInvariant();
        string kindNameCapital = char.ToUpperInvariant(kindName[0]) + kindName[1..];

        MatchList<FunctionMatch<TFunction>, TFunction> functionMatches = new();

        foreach (TFunction function in functions.Compiled)
        {
            functionMatches.Add(GetFunctionMatch(function, query));
        }

        if (functionMatches.Count > 0)
        {
            FunctionMatch<TFunction> best = functionMatches[0];
            result = new FunctionQueryResult<TFunction>()
            {
                Function = best.Definition,
                Success = true,
                TypeArguments = best.TypeArguments,
                Arguments = best.Arguments,
            };

            if (best.Errors.Count > 0)
            {
                error = new PossibleDiagnostic($"{kindNameCapital} \"{readableName}\" not found", best.Errors.ToImmutableArray());
                return false;
            }

            if (!best.IsIdentifierMatched)
            {
                if (best.IdentifierBadness == 1)
                {
                    error = new PossibleDiagnostic($"No {kindName} found with name \"{query.Identifier}\" (did you mean \"{best.Definition.Identifier}\"?)");
                }
                else
                {
                    error = new PossibleDiagnostic($"No {kindName} found with name \"{query.Identifier}\"");
                }
                return false;
            }

            if (!best.IsParameterCountMatches)
            {
                PossibleDiagnostic suberror = new($"Wrong number of arguments passed: expected {best.Definition.Parameters.Length} but got {query.ArgumentCount}");
                if (best.Definition is FunctionThingDefinition ftd)
                { suberror = suberror.WithRelatedInfo(new DiagnosticRelatedInformationAt($"{kindNameCapital} \"{best.Definition.ToReadable()}\" defined here", new Location(ftd.Identifier.Position, ftd.File))); }
                error = new PossibleDiagnostic($"{kindNameCapital} \"{readableName}\" not found", suberror);
                return false;
            }

            if (best.ParameterTypeMatch is not null &&
                best.ParameterTypeMatch.Value == TypeMatch.None)
            {
                PossibleDiagnostic suberror = new($"Wrong types of arguments passed (sorry I can't tell any more info)");
                GetFunctionMatch<TFunction, TDefinedIdentifier, TPassedIdentifier, TArgument>(best.Definition, query);
                if (best.Definition is FunctionThingDefinition ftd)
                { suberror = suberror.WithRelatedInfo(new DiagnosticRelatedInformationAt($"{kindNameCapital} \"{best.Definition.ToReadable()}\" defined here", new Location(ftd.Identifier.Position, ftd.File))); }
                error = new PossibleDiagnostic($"{kindNameCapital} \"{readableName}\" not found", suberror);
                return false;
            }

            if (best.ReturnTypeMatch == TypeMatch.None)
            {
                PossibleDiagnostic suberror = new($"Wrong return type (sorry I can't tell any more info)");
                if (best.Definition is FunctionThingDefinition ftd)
                { suberror = suberror.WithRelatedInfo(new DiagnosticRelatedInformationAt($"{kindNameCapital} \"{best.Definition.ToReadable()}\" defined here", new Location(ftd.Identifier.Position, ftd.File))); }
                error = new PossibleDiagnostic($"{kindNameCapital} \"{readableName}\" not found", suberror);
                return false;
            }

            if (functionMatches.Count > 1)
            {
                error = new PossibleDiagnostic($"Multiple functions matched ({string.Join(", ", functionMatches.Select(v => v.Definition.ToReadable()))})");
                foreach (FunctionMatch<TFunction> functionMatch in functionMatches)
                {
                    if (functionMatch.Definition is FunctionThingDefinition f)
                    {
                        error = error.WithRelatedInfo(new DiagnosticRelatedInformationAt($"{kindNameCapital} \"{functionMatch.Definition.ToReadable()}\" defined here", new Location(f.Identifier.Position, f.File)));
                    }
                    else
                    {
                        error = error.WithRelatedInfo(new DiagnosticRelatedInformationAt($"{kindNameCapital} \"{functionMatch.Definition.ToReadable()}\" defined here", functionMatch.Definition.Location));
                    }
                }
                return false;
            }

            if (best.Definition.Definition.IsTemplate)
            {
                if (best.TypeArguments is null)
                {
                    PossibleDiagnostic suberror = new($"Failed to resolve the template types");
                    if (best.Definition is FunctionThingDefinition ftd)
                    { suberror = suberror.WithRelatedInfo(new DiagnosticRelatedInformationAt($"{kindNameCapital} \"{best.Definition.ToReadable()}\" defined here", new Location(ftd.Identifier.Position, ftd.File))); }
                    error = new PossibleDiagnostic($"{kindNameCapital} \"{readableName}\" not found", suberror);
                    return false;
                }

                bool templateAlreadyAdded = false;
                foreach (TemplateInstance<TFunction> item in functions.Compilable)
                {
                    if (!FunctionEquals(item, best)) continue;
                    result = new FunctionQueryResult<TFunction>()
                    {
                        Function = item.Template,
                        Success = true,
                        TypeArguments = best.TypeArguments,
                        Arguments = best.Arguments,
                    };
                    templateAlreadyAdded = true;
                    break;
                }

                if (!templateAlreadyAdded)
                {
                    TemplateInstance<TFunction> template = new(best.Definition, best.TypeArguments);
                    query.AddCompilable?.Invoke(template);
                    result = new FunctionQueryResult<TFunction>()
                    {
                        Function = template.Template,
                        Success = true,
                        TypeArguments = best.TypeArguments,
                        Arguments = best.Arguments,
                    };
                }
            }

            error = null;
            return true;
        }
        else
        {
            result = default;
            error = new PossibleDiagnostic($"There are no functions bruh");
            return false;
        }
    }

    FunctionMatch<TFunction> GetFunctionMatch<TFunction, TDefinedIdentifier, TPassedIdentifier, TArgument>(
        TFunction function,
        FunctionQuery<TFunction, TPassedIdentifier, TDefinedIdentifier, TArgument> query)
        where TFunction : ICompiledFunctionDefinition, IIdentifiable<TDefinedIdentifier>, ICompiledDefinition<FunctionThingDefinition>
        where TDefinedIdentifier : notnull, IEquatable<TPassedIdentifier>
        where TArgument : notnull
    {
        FunctionMatch<TFunction> result = new()
        {
            Definition = function,
            Errors = new(),
        };

        int partial = 0;
        for (int i = 0; i < function.Parameters.Length; i++)
        {
            if (function.Parameters[i].Definition.DefaultValue is null) partial = i + 1;
            else break;
        }

        if (query.Identifier is null)
        {
            result.IsIdentifierMatched = true;
            result.IdentifierBadness = 0;
        }
        else if (query.IdentifierMatcher is not null)
        {
            if (query.IdentifierMatcher.Invoke(query.Identifier, function.Identifier, out int identifierBadness))
            {
                result.IsIdentifierMatched = true;
                result.IdentifierBadness = identifierBadness;
            }
            else
            {
                result.IsIdentifierMatched = false;
            }
        }
        else if (function.Identifier.Equals(query.Identifier))
        {
            result.IsIdentifierMatched = true;
            result.IdentifierBadness = 0;
        }
        else
        {
            result.IsIdentifierMatched = false;
            result.IdentifierBadness = 2;

            if (query.Identifier is string _a1 &&
                function.Identifier is Tokenizing.Token _b1)
            {
                if (_a1.ToLowerInvariant() == _b1.Content.ToLowerInvariant())
                {
                    result.IdentifierBadness = 1;
                }
            }

            if (result.IdentifierBadness == 1)
            {
                PossibleDiagnostic item = new($"Function \"{query.Identifier}\" does not match with \"{function.Identifier}\"");
                if (function is FunctionThingDefinition ftd)
                { item = item.WithRelatedInfo(new DiagnosticRelatedInformationAt($"Function \"{function.ToReadable()}\" defined here", new Location(ftd.Identifier.Position, ftd.File))); }
                result.Errors.Add(item);
            }
            else
            {
                result.Errors.Add(new($"No function found with name \"{query.Identifier}\""));
            }
            return result;
        }

        if (query.ArgumentCount.HasValue)
        {
            if (query.ArgumentCount.Value < partial)
            {
                PossibleDiagnostic item = new($"Wrong number of arguments passed: expected {function.Parameters.Length} but passed {query.ArgumentCount.Value}");
                if (function is FunctionThingDefinition ftd)
                { item = item.WithRelatedInfo(new DiagnosticRelatedInformationAt($"Function \"{function.ToReadable()}\" defined here", new Location(ftd.Identifier.Position, ftd.File))); }
                result.Errors.Add(item);
                return result;
            }

            if (query.ArgumentCount.Value > function.Parameters.Length)
            {
                PossibleDiagnostic item = new($"Wrong number of arguments passed: expected {function.Parameters.Length} but passed {query.ArgumentCount.Value}");
                if (function is FunctionThingDefinition ftd)
                { item = item.WithRelatedInfo(new DiagnosticRelatedInformationAt($"Function \"{function.ToReadable()}\" defined here", new Location(ftd.Identifier.Position, ftd.File))); }
                result.Errors.Add(item);
                return result;
            }

            result.UsedUpDefaultParameterValues = function.Parameters.Length - query.ArgumentCount.Value;
        }

        result.IsParameterCountMatches = true;

        result.IsGenericParameterCountMatches = true;

        if (query.RelevantFile is null ||
            function.File == query.RelevantFile)
        {
            result.IsFileMatches = true;
        }

        if (query.RelevantFile is null ||
            function.Definition.CanUse(query.RelevantFile))
        {
            result.IsProtectionRespected = true;
        }
        else
        {
            result.Errors.Add(new($"Cannot use function \"{query.Identifier}\" due it's protection level"));
        }

        bool TryReplaceArgument2(ref CompiledExpression? argument, GeneralType passedType, GeneralType definedType, ParameterDefinition definition, TArgument passed, Dictionary<string, GeneralType> typeArguments, int argumentIndex)
        {
            if (passed is not CompiledExpression passedExpression) return false;
            if (!definition.Modifiers.Contains(ModifierKeywords.This)) return false;

            if (!definedType.Is(out PointerType? definedPointerType)) return false;
            if (!GeneralType.TryGetTypeParameters(definedPointerType.To, passedType, typeArguments)) return false;

            argument = new CompiledGetReference()
            {
                Of = passedExpression,
                Location = passedExpression.Location,
                SaveValue = passedExpression.SaveValue,
                Type = new PointerType(passedExpression.Type),
            };
            return true;
        }

        void GetArgumentMatch(ref TypeMatch typeMatch, ref CompiledExpression? compiledPassedArgument, GeneralType definedType, ParameterDefinition definition, TArgument passed, List<PossibleDiagnostic> errors, int argumentIndex)
        {
            if (typeMatch == TypeMatch.None) return;

            PossibleDiagnostic? error = null;

            /*
            if (passed is CompiledExpression expression)
            {
                if (typeMatch >= TypeMatch.ImplicitCast
                    && CanCastImplicitly(expression, definedType, out CompiledExpression? assignedValue, out PossibleDiagnostic? castError, out CastLevel castLevel))
                {
                    switch (castLevel)
                    {
                        case CastLevel.Cast:
                        case CastLevel.ReferenceDereference:
                            if (typeMatch > TypeMatch.ImplicitCast) typeMatch = TypeMatch.ImplicitCast;
                            compiledPassedArgument = assignedValue;
                            break;
                        case CastLevel.SameCast:
                        case CastLevel.Same:
                            if (typeMatch > TypeMatch.Same) typeMatch = TypeMatch.Same;
                            compiledPassedArgument = assignedValue;
                            break;
                        case CastLevel.Equals:
                            if (typeMatch > TypeMatch.Equals) typeMatch = TypeMatch.Equals;
                            compiledPassedArgument = assignedValue;
                            break;
                        case CastLevel.None:
                        default:
                            typeMatch = TypeMatch.None;
                            return;
                    }
                }
            }
            */

            if (typeMatch >= TypeMatch.ImplicitCast)
            {
                GeneralType passedType = query.Converter.Invoke(passed);

                if (typeMatch >= TypeMatch.Equals && passedType.Equals(definedType))
                {
                    typeMatch = TypeMatch.Equals;
                    return;
                }

                if (typeMatch >= TypeMatch.Same && passedType.SameAs(definedType))
                {
                    typeMatch = TypeMatch.Same;
                    return;
                }

                if (typeMatch >= TypeMatch.ImplicitCast && CanCastImplicitly(passedType, definedType, out error, out _))
                {
                    typeMatch = TypeMatch.ImplicitCast;
                    return;
                }

                if (passedType.Is(out ReferenceType? ar))
                {
                    if (typeMatch >= TypeMatch.EqualsRef && ar.To.Equals(definedType))
                    {
                        typeMatch = TypeMatch.EqualsRef;
                        return;
                    }

                    if (typeMatch >= TypeMatch.SameRef && ar.To.SameAs(definedType))
                    {
                        typeMatch = TypeMatch.SameRef;
                        return;
                    }
                }

                if (typeMatch >= TypeMatch.ImplicitCast
                    && passed is CompiledExpression passedExpression
                    && definition.Modifiers.Contains(ModifierKeywords.This)
                    && CanCastImplicitly(new PointerType(passedType), definedType, out _, out _))
                {
                    compiledPassedArgument = new CompiledGetReference()
                    {
                        Of = passedExpression,
                        Location = passedExpression.Location,
                        SaveValue = passedExpression.SaveValue,
                        Type = new PointerType(passedExpression.Type),
                    };
                    typeMatch = TypeMatch.ImplicitCastRef;
                    return;
                }
            }

            if (typeMatch >= TypeMatch.Promotion)
            {
                if (query.Converter.Invoke(passed).SameAs(definedType))
                {
                    typeMatch = TypeMatch.Promotion;
                    return;
                }
            }

            if (error is not null) errors.Add(new PossibleDiagnostic($"Invalid argument passed at {argumentIndex}", passed as ILocated, error));
            typeMatch = TypeMatch.None;
        }

        TypeMatch GetReturnTypeMatch(GeneralType target, GeneralType current, List<PossibleDiagnostic> errors)
        {
            if (current.Equals(target))
            {
                return TypeMatch.Equals;
            }
            else if (current.SameAs(target))
            {
                return TypeMatch.Same;
            }
            else if (CanCastImplicitly(current, target, out PossibleDiagnostic? error, out _))
            {
                return TypeMatch.ImplicitCast;
            }
            else
            {
                errors.Add(new PossibleDiagnostic($"Return type mismatch", error));
                return TypeMatch.None;
            }
        }

        if (function.Definition.IsTemplate)
        {
            Dictionary<string, GeneralType> _typeArguments = new();

            if (!query.Arguments.HasValue)
            {
                result.ParameterTypeMatch = null;
            }
            else
            {
                int checkCount = Math.Min(function.Parameters.Length, query.Arguments.Value.Length);

                CompiledExpression?[] argumentValues = new CompiledExpression?[checkCount];

                for (int i = 0; i < checkCount; i++)
                {
                    GeneralType defined = function.Parameters[i].Type;
                    GeneralType passed = query.Converter.Invoke(query.Arguments.Value[i]);

                    if (TryReplaceArgument2(ref argumentValues[i], passed, defined, function.Parameters[i].Definition, query.Arguments.Value[i], _typeArguments, i))
                    {
                        // yay
                    }
                    else if (!GeneralType.TryGetTypeParameters(defined, passed, _typeArguments))
                    {
                        PossibleDiagnostic suberror = new($"Argument {i + 1}: Invalid type passed: expected {GeneralType.TryInsertTypeParameters(defined, _typeArguments)} but passed {passed}");
                        if (function is FunctionThingDefinition ftd)
                        { suberror = suberror.WithRelatedInfo(new DiagnosticRelatedInformationAt($"Function \"{function.ToReadable()}\" defined here", new Location(ftd.Identifier.Position, ftd.File))); }
                        result.Errors.Add(new PossibleDiagnostic($"Argument {i + 1}: Could not resolve the template types", suberror));
                        return result;
                    }
                }

                result.ParameterTypeMatch = TypeMatch.Equals;
                result.TypeArguments = _typeArguments.ToImmutableDictionary();

                for (int i = 0; i < checkCount; i++)
                {
                    GeneralType defined = GeneralType.TryInsertTypeParameters(function.Parameters[i].Type, _typeArguments);
                    TArgument passed = query.Arguments.Value[i];
                    TypeMatch v = result.ParameterTypeMatch.Value;
                    GetArgumentMatch(ref v, ref argumentValues[i], defined, function.Parameters[i].Definition, passed, result.Errors, i);
                    if (v < result.ParameterTypeMatch) result.ParameterTypeMatch = v;
                }

                result.Arguments = argumentValues.AsImmutableUnsafe();
            }

            if (query.ReturnType is not null)
            {
                result.ReturnTypeMatch = GetReturnTypeMatch(GeneralType.TryInsertTypeParameters(function.Type, _typeArguments), query.ReturnType, result.Errors);
            }
            else
            {
                result.ReturnTypeMatch = TypeMatch.Equals;
            }
        }
        else
        {
            if (!query.Arguments.HasValue)
            {
                result.ParameterTypeMatch = null;
            }
            else
            {
                result.ParameterTypeMatch = TypeMatch.Equals;

                int checkCount = Math.Min(function.Parameters.Length, query.Arguments.Value.Length);
                CompiledExpression?[] arguments = new CompiledExpression?[checkCount];
                for (int i = 0; i < checkCount; i++)
                {
                    GeneralType defined = function.Parameters[i].Type;
                    TArgument passed = query.Arguments.Value[i];
                    TypeMatch v = result.ParameterTypeMatch.Value;
                    GetArgumentMatch(ref v, ref arguments[i], defined, function.Parameters[i].Definition, passed, result.Errors, i);
                    if (v < result.ParameterTypeMatch) result.ParameterTypeMatch = v;
                }
                result.Arguments = arguments.AsImmutableUnsafe();
            }

            if (query.ReturnType is not null)
            {
                result.ReturnTypeMatch = GetReturnTypeMatch(function.Type, query.ReturnType, result.Errors);
            }
            else
            {
                result.ReturnTypeMatch = TypeMatch.Equals;
            }
        }

        return result;
    }
}
