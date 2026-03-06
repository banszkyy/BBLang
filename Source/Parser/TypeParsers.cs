using LanguageCore.Parser.Statements;
using LanguageCore.Tokenizing;

namespace LanguageCore.Parser;

public sealed partial class Parser
{
    [Flags]
    enum AllowedType
    {
        None = 0x0,
        Any = 0x1,
        FunctionPointer = 0x2,
        StackArrayWithoutLength = 0x4,
    }

    static readonly ImmutableArray<string> TheseCharactersIndicateThatTheIdentifierWillBeFollowedByAComplexType = ImmutableArray.Create("<", "(", "[");

    bool ExpectType(AllowedType flags, [NotNullWhen(true)] out TypeInstance? type)
    {
        if (ExpectType(flags, out type, out DiagnosticAt? error))
        { return true; }
        if (error is not null)
        { Diagnostics.Add(error.Break()); }
        return false;
    }

    bool ExpectType(AllowedType flags, [NotNullWhen(true)] out TypeInstance? type, [MaybeNullWhen(true)] out DiagnosticAt? error)
    {
        type = default;
        error = null;

        if (!ExpectIdentifier(out Token? possibleType)) return false;

        if (possibleType.Equals(StatementKeywords.Return))
        { return false; }

        Token? closureModifier = null;

        if (CurrentToken is not null
            && CurrentToken.TokenType == TokenType.Identifier
            && CurrentToken.Position.AbsoluteRange.Start == possibleType.Position.AbsoluteRange.End)
        {
            closureModifier = possibleType;
            possibleType = CurrentToken;
            CurrentTokenIndex++;
        }
        else if (possibleType.Content.StartsWith('@'))
        {
            int slicedIndex = CurrentTokenIndex - 1;
            closureModifier = possibleType[..1];
            closureModifier.AnalyzedType = TokenAnalyzedType.TypeModifier;
            possibleType = possibleType[1..];
            Tokens.RemoveAt(slicedIndex);
            Tokens.Insert(slicedIndex, possibleType);
            Tokens.Insert(slicedIndex, closureModifier);
            CurrentTokenIndex++;
        }

        type = new TypeInstanceSimple(possibleType, File);

        if (possibleType.Content.Equals(TypeKeywords.Any))
        {
            possibleType.AnalyzedType = TokenAnalyzedType.Keyword;

            if (ExpectOperator(TheseCharactersIndicateThatTheIdentifierWillBeFollowedByAComplexType, out Token? illegalT))
            { Diagnostics.Add(DiagnosticAt.Error($"This is not allowed", illegalT, File, false)); }

            if (ExpectOperator("*", out Token? pointerOperator))
            {
                pointerOperator.AnalyzedType = TokenAnalyzedType.TypeModifier;
                type = new TypeInstancePointer(type, pointerOperator, File);
            }
            else if (ExpectOperator("&", out Token? referenceOperator))
            {
                referenceOperator.AnalyzedType = TokenAnalyzedType.TypeModifier;
                type = new TypeInstanceReference(type, referenceOperator, File);
            }
            else
            {
                if ((flags & AllowedType.Any) == 0)
                {
                    error = DiagnosticAt.Error($"Type `{TypeKeywords.Any}` is not valid in the current context", possibleType, File, false);
                    return false;
                }
            }

            goto end;
        }

        if (TypeKeywords.List.Contains(possibleType.Content))
        {
            possibleType.AnalyzedType = TokenAnalyzedType.BuiltinType;
        }
        else
        {
            possibleType.AnalyzedType = TokenAnalyzedType.Type;
        }

        int afterIdentifier = CurrentTokenIndex;
        bool withGenerics = false;

        while (true)
        {
            if (ExpectOperator("*", out Token? pointerOperator))
            {
                pointerOperator.AnalyzedType = TokenAnalyzedType.TypeModifier;
                type = new TypeInstancePointer(type, pointerOperator, File);
            }
            else if (ExpectOperator("&", out Token? referenceOperator))
            {
                referenceOperator.AnalyzedType = TokenAnalyzedType.TypeModifier;
                type = new TypeInstanceReference(type, referenceOperator, File);
            }
            else if (ExpectOperator("<", out Token? angleBracketStart))
            {
                if (type is not TypeInstanceSimple)
                { throw new NotImplementedException(); }

                List<TypeInstance> genericTypes = new();
                Token? angleBracketEnd;

                while (true)
                {
                    if (!ExpectType(AllowedType.FunctionPointer, out TypeInstance? typeParameter))
                    {
                        CurrentTokenIndex = afterIdentifier;
                        goto end;
                    }

                    genericTypes.Add(typeParameter);

                    if (ExpectOperator(">", out angleBracketEnd))
                    { break; }

                    if (ExpectOperator(">>", out Token? doubleEnd))
                    {
                        (Token? newA, Token? newB) = doubleEnd.Slice(1);
                        if (newA == null || newB == null)
                        { throw new UnreachableException($"I failed at token splitting :("); }
                        CurrentTokenIndex--;
                        angleBracketEnd = newA;
                        Tokens[CurrentTokenIndex] = newB;
                        break;
                    }

                    if (ExpectOperator(","))
                    { continue; }
                }

                type = new TypeInstanceSimple(possibleType, File, genericTypes.ToImmutableArray(), new TokenPair(angleBracketStart, angleBracketEnd));
                withGenerics = true;
            }
            else if (!withGenerics && ExpectOperator("(", out Token? bracketStart))
            {
                if (!flags.HasFlag(AllowedType.FunctionPointer))
                {
                    CurrentTokenIndex--;
                    goto end;
                }

                List<TypeInstance> parameterTypes = new();
                Token? bracketEnd;
                while (!ExpectOperator(")", out bracketEnd))
                {
                    if (!ExpectType(AllowedType.FunctionPointer, out TypeInstance? subtype))
                    {
                        CurrentTokenIndex = afterIdentifier;
                        goto end;
                    }

                    parameterTypes.Add(subtype);

                    if (ExpectOperator(")", out bracketEnd))
                    { break; }

                    if (ExpectOperator(","))
                    { continue; }
                }

                type = new TypeInstanceFunction(type, parameterTypes.ToImmutableArray(), closureModifier, File, new(bracketStart, bracketEnd));
            }
            else if (ExpectOperator("[", out Token? arraySquareBracketStart))
            {
                if (ExpectOperator("]", out Token? arraySquareBracketEnd))
                {
                    type = new TypeInstanceStackArray(type, null, new(arraySquareBracketStart, arraySquareBracketEnd), File);
                }
                else if (ExpectAnyExpression(out Expression? sizeValue))
                {
                    if (!ExpectOperator("]", out arraySquareBracketEnd))
                    { return false; }

                    type = new TypeInstanceStackArray(type, sizeValue, new(arraySquareBracketStart, arraySquareBracketEnd), File);
                }
                else
                {
                    return false;
                }
            }
            else
            { break; }
        }

    end:
        if (type is not TypeInstanceFunction && closureModifier is not null)
        {
            error = DiagnosticAt.Error($"This type modifier is bruh", closureModifier, File, false);
            return false;
        }

        return true;
    }
}
