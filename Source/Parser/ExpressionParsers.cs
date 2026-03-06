using LanguageCore.Parser.Statements;
using LanguageCore.Tokenizing;

namespace LanguageCore.Parser;

public sealed partial class Parser
{
    bool ExpectLambda([NotNullWhen(true)] out LambdaExpression? lambdaStatement)
    {
        ParseRestorePoint savepoint = SavePoint();
        lambdaStatement = null;

        OrderedDiagnosticCollection parametersDiagnostics = new();
        if (!ExpectParameters(ParameterModifiers, false, out ParameterDefinitionCollection? parameters, parametersDiagnostics))
        {
            savepoint.Restore();
            return false;
        }

        if (!ExpectOperator("=>", out Token? arrow))
        {
            savepoint.Restore();
            return false;
        }

        Statement body;

        if (ExpectBlock(out Block? block, false))
        {
            body = block;
        }
        else if (ExpectAnyExpression(out Expression? expression))
        {
            body = expression;
        }
        else
        {
            savepoint.Restore();
            return false;
        }

        arrow.AnalyzedType = TokenAnalyzedType.OtherOperator;

        lambdaStatement = new LambdaExpression(
            parameters,
            arrow,
            body,
            File
        );
        return true;
    }

    bool ExpectListValue([NotNullWhen(true)] out ListExpression? listValue)
    {
        ParseRestorePoint savepoint = SavePoint();
        listValue = null;

        if (!ExpectOperator("[", out Token? bracketStart))
        {
            savepoint.Restore();
            return false;
        }

        if (ExpectOperator("]", out Token? bracketEnd))
        {
            listValue = new ListExpression(ImmutableArray<Expression>.Empty, new TokenPair(bracketStart, bracketEnd), File);
            return true;
        }

        ImmutableArray<Expression>.Builder values = ImmutableArray.CreateBuilder<Expression>();
        EndlessCheck endlessSafe = new();
        Position lastPosition = bracketStart.Position;

        while (true)
        {
            if (ExpectOperator("]", out bracketEnd))
            { break; }

            if (!ExpectAnyExpression(out Expression? value))
            {
                value = new MissingExpression(lastPosition.After(), File);
                Diagnostics.Add(DiagnosticAt.Error("Expected expression or `]`", value, false));
            }

            values.Add(value);
            lastPosition = value.Position;

            if (!ExpectOperator(",", out Token? comma))
            {
                if (!ExpectOperator("]", out bracketEnd))
                {
                    bracketEnd = new MissingToken(TokenType.Operator, lastPosition.After(), "]");
                    Diagnostics.Add(DiagnosticAt.Error("Expected `,` or `]`", bracketEnd, File, false));
                }
                break;
            }

            lastPosition = comma.Position;

            endlessSafe.Step();
        }

        listValue = new ListExpression(values.DrainToImmutable(), new TokenPair(bracketStart, bracketEnd), File);
        return true;
    }

    bool ExpectLiteral([NotNullWhen(true)] out LiteralExpression? statement)
    {
        ParseRestorePoint savepoint = SavePoint();

        SkipCrapTokens();

        if (CurrentToken != null && CurrentToken.TokenType == TokenType.LiteralFloat)
        {
            string v = CurrentToken.Content;
            v = v.Replace("_", null, StringComparison.Ordinal);
            if (v.EndsWith('f')) v = v[..^1];

            if (!float.TryParse(v, out float value))
            {
                value = default;
                Diagnostics.Add(DiagnosticAt.Error($"Invalid float literal `{CurrentToken.Content}`", CurrentToken, File));
            }

            LiteralExpression literal = new FloatLiteralExpression(value, CurrentToken, File);
            CurrentToken.AnalyzedType = TokenAnalyzedType.None;

            CurrentTokenIndex++;

            statement = literal;
            return true;
        }
        else if (CurrentToken != null && CurrentToken.TokenType == TokenType.LiteralNumber)
        {
            string v = CurrentToken.Content;
            v = v.Replace("_", null, StringComparison.Ordinal);

            if (!int.TryParse(v, out int value))
            {
                value = default;
                Diagnostics.Add(DiagnosticAt.Error($"Invalid integer literal `{CurrentToken.Content}`", CurrentToken, File));
            }

            LiteralExpression literal = new IntLiteralExpression(value, CurrentToken, File);
            CurrentToken.AnalyzedType = TokenAnalyzedType.None;

            CurrentTokenIndex++;

            statement = literal;
            return true;
        }
        else if (CurrentToken != null && CurrentToken.TokenType == TokenType.LiteralHex)
        {
            string v = CurrentToken.Content;

            if (v.Length < 3)
            {
                Diagnostics.Add(DiagnosticAt.Error($"Invalid hex literal `{CurrentToken}`", CurrentToken, File, false));
                v = "0";
            }
            else
            {
                v = v[2..];
                v = v.Replace("_", string.Empty, StringComparison.Ordinal);
            }

            LiteralExpression literal = new IntLiteralExpression(Convert.ToInt32(v, 16), CurrentToken, File);
            CurrentToken.AnalyzedType = TokenAnalyzedType.None;

            CurrentTokenIndex++;

            statement = literal;
            return true;
        }
        else if (CurrentToken != null && CurrentToken.TokenType == TokenType.LiteralBinary)
        {
            string v = CurrentToken.Content;

            if (v.Length < 3)
            {
                Diagnostics.Add(DiagnosticAt.Error($"Invalid binary literal `{CurrentToken}`", CurrentToken, File, false));
                v = "0";
            }
            else
            {
                v = v[2..];
                v = v.Replace("_", string.Empty, StringComparison.Ordinal);
            }

            LiteralExpression literal = new IntLiteralExpression(Convert.ToInt32(v, 2), CurrentToken, File);
            CurrentToken.AnalyzedType = TokenAnalyzedType.None;

            CurrentTokenIndex++;

            statement = literal;
            return true;
        }
        else if (CurrentToken != null && CurrentToken.TokenType == TokenType.LiteralString)
        {
            LiteralExpression literal = new StringLiteralExpression(CurrentToken.Content, CurrentToken, File);
            CurrentToken.AnalyzedType = TokenAnalyzedType.None;

            CurrentTokenIndex++;

            statement = literal;
            return true;
        }
        else if (CurrentToken != null && CurrentToken.TokenType == TokenType.LiteralCharacter)
        {
            char value;
            if (CurrentToken.Content.Length != 1)
            {
                value = default;
                Diagnostics.Add(DiagnosticAt.Error($"Invalid character literal `{CurrentToken.Content}`", CurrentToken, File));
            }
            else
            {
                value = CurrentToken.Content[0];
            }

            LiteralExpression literal = new CharLiteralExpression(value, CurrentToken, File);
            CurrentToken.AnalyzedType = TokenAnalyzedType.None;

            CurrentTokenIndex++;

            statement = literal;
            return true;
        }

        savepoint.Restore();

        statement = null;
        return false;
    }

    bool ExpectIndex(Expression prevStatement, [NotNullWhen(true)] out IndexCallExpression? statement)
    {
        ParseRestorePoint savepoint = SavePoint();
        statement = null;

        if (!ExpectOperator("[", out Token? bracketStart))
        {
            savepoint.Restore();
            return false;
        }

        if (!ExpectArgument(out ArgumentExpression? indexArgument, ArgumentModifiers))
        {
            savepoint.Restore();
            return false;
        }

        if (!ExpectOperator("]", out Token? bracketEnd))
        {
            bracketEnd = new MissingToken(TokenType.Operator, indexArgument.Position.After(), "]");
            Diagnostics.Add(DiagnosticAt.Error("Expected `]`", bracketEnd, File, false));
        }

        statement = new IndexCallExpression(prevStatement, indexArgument, new TokenPair(bracketStart, bracketEnd), File);
        return true;
    }

    bool ExpectExpressionInBrackets([NotNullWhen(true)] out Expression? expressionInBrackets)
    {
        ParseRestorePoint savepoint = SavePoint();
        expressionInBrackets = null;

        if (!ExpectOperator("(", out Token? bracketStart))
        {
            savepoint.Restore();
            return false;
        }

        if (!ExpectAnyExpression(out Expression? expression))
        {
            expression = new MissingExpression(bracketStart.Position.After(), File);
            Diagnostics.Add(DiagnosticAt.Error("Expected expression", bracketStart.Position.After(), File));
        }

        if (!ExpectOperator(")", out Token? bracketEnd))
        {
            bracketEnd = new MissingToken(TokenType.Operator, expression.Position.After(), ")");
            Diagnostics.Add(DiagnosticAt.Error("Expected `)`", bracketEnd, File, false));
        }

        expression.SurroundingBrackets = new TokenPair(bracketStart, bracketEnd);

        expressionInBrackets = expression;
        return true;
    }

    bool ExpectNewExpression([NotNullWhen(true)] out Expression? newExpression)
    {
        ParseRestorePoint savepoint = SavePoint();
        newExpression = null;

        if (!ExpectIdentifier(StatementKeywords.New, out Token? keywordNew))
        {
            savepoint.Restore();
            return false;
        }

        keywordNew.AnalyzedType = TokenAnalyzedType.Keyword;

        if (!ExpectType(AllowedType.None, out TypeInstance? instanceTypeName))
        {
            instanceTypeName = new MissingTypeInstance(keywordNew.Position.After(), File);
            Diagnostics.Add(DiagnosticAt.Error($"Expected type after keyword `{StatementKeywords.New}`", instanceTypeName, false));
        }

        if (ExpectArguments(out ArgumentListExpression? argumentList))
        {
            newExpression = new ConstructorCallExpression(keywordNew, instanceTypeName, argumentList, File);
            return true;
        }
        else
        {
            newExpression = new NewInstanceExpression(keywordNew, instanceTypeName, File);
            return true;
        }
    }

    bool ExpectFieldAccessor(Expression prevStatement, [NotNullWhen(true)] out FieldExpression? fieldAccessor)
    {
        ParseRestorePoint savepoint = SavePoint();
        fieldAccessor = null;

        if (!ExpectOperator(".", out Token? tokenDot))
        {
            savepoint.Restore();
            return false;
        }

        if (!ExpectIdentifier(out Token? fieldName))
        {
            fieldName = new MissingToken(TokenType.Identifier, tokenDot.Position.After());
            fieldAccessor = new FieldExpression(
                prevStatement,
                fieldName,
                File
            );
            Diagnostics.Add(DiagnosticAt.Error("Expected a symbol after `.`", fieldName, File, false));
            return true;
        }

        fieldAccessor = new FieldExpression(
            prevStatement,
            fieldName,
            File
        );
        return true;
    }

    bool ExpectAsStatement(Expression prevStatement, [NotNullWhen(true)] out ReinterpretExpression? basicTypeCast)
    {
        ParseRestorePoint savepoint = SavePoint();
        basicTypeCast = null;

        if (!ExpectIdentifier(StatementKeywords.As, out Token? keyword))
        {
            savepoint.Restore();
            return false;
        }

        if (!ExpectType(AllowedType.StackArrayWithoutLength, out TypeInstance? type))
        {
            type = new MissingTypeInstance(keyword.Position.After(), File);
            Diagnostics.Add(DiagnosticAt.Error($"Expected type after keyword `{keyword}`", type, false));
        }

        basicTypeCast = new ReinterpretExpression(prevStatement, keyword, type, File);
        return true;
    }

    bool ExpectIdentifierExpression([NotNullWhen(true)] out IdentifierExpression? expression)
    {
        ParseRestorePoint savepoint = SavePoint();
        expression = null;

        if (!ExpectIdentifier(out Token? simpleIdentifier))
        {
            savepoint.Restore();
            return false;
        }

        if (simpleIdentifier.Content
            is StatementKeywords.This
            or StatementKeywords.Sizeof)
        {
            simpleIdentifier.AnalyzedType = TokenAnalyzedType.Keyword;
            expression = new IdentifierExpression(simpleIdentifier, File);
            return true;
        }

        if (StatementKeywords.List.Contains(simpleIdentifier.Content))
        {
            savepoint.Restore();
            return false;
        }

        if (ProtectionKeywords.List.Contains(simpleIdentifier.Content))
        {
            savepoint.Restore();
            return false;
        }

        if (ModifierKeywords.List.Contains(simpleIdentifier.Content))
        {
            savepoint.Restore();
            return false;
        }

        if (DeclarationKeywords.List.Contains(simpleIdentifier.Content))
        {
            savepoint.Restore();
            return false;
        }

        expression = new IdentifierExpression(simpleIdentifier, File);
        return true;
    }

    bool ExpectOneValue([NotNullWhen(true)] out Expression? statementWithValue, bool allowAsStatement = true)
    {
        statementWithValue = null;

        if (ExpectLambda(out LambdaExpression? lambdaStatement))
        {
            statementWithValue = lambdaStatement;
        }
        else if (ExpectListValue(out ListExpression? listValue))
        {
            statementWithValue = listValue;
        }
        else if (ExpectLiteral(out LiteralExpression? literal))
        {
            statementWithValue = literal;
        }
        else if (ExpectTypeCast(out ManagedTypeCastExpression? typeCast))
        {
            statementWithValue = typeCast;
        }
        else if (ExpectExpressionInBrackets(out Expression? expressionInBrackets))
        {
            statementWithValue = expressionInBrackets;
        }
        else if (ExpectNewExpression(out Expression? newExpression))
        {
            statementWithValue = newExpression;
        }
        else if (ExpectVariableAddressGetter(out GetReferenceExpression? memoryAddressGetter))
        {
            statementWithValue = memoryAddressGetter;
        }
        else if (ExpectVariableAddressFinder(out DereferenceExpression? pointer))
        {
            statementWithValue = pointer;
        }
        else if (ExpectIdentifierExpression(out IdentifierExpression? identifierExpression))
        {
            statementWithValue = identifierExpression;
        }

        if (statementWithValue == null)
        { return false; }

        while (true)
        {
            if (ExpectFieldAccessor(statementWithValue, out FieldExpression? fieldAccessor))
            {
                statementWithValue = fieldAccessor;
            }
            else if (ExpectIndex(statementWithValue, out IndexCallExpression? statementIndex))
            {
                statementWithValue = statementIndex;
            }
            else if (ExpectAnyCall(statementWithValue, out AnyCallExpression? anyCall))
            {
                statementWithValue = anyCall;
            }
            else
            {
                break;
            }
        }

        if (allowAsStatement && ExpectAsStatement(statementWithValue, out ReinterpretExpression? basicTypeCast))
        {
            statementWithValue = basicTypeCast;
        }

        return statementWithValue != null;
    }

    bool ExpectTypeCast([NotNullWhen(true)] out ManagedTypeCastExpression? typeCast)
    {
        ParseRestorePoint savepoint = SavePoint();
        typeCast = default;

        if (!ExpectOperator("(", out Token? leftBracket))
        {
            savepoint.Restore();
            return false;
        }

        if (!ExpectType(AllowedType.Any | AllowedType.FunctionPointer | AllowedType.StackArrayWithoutLength, out TypeInstance? type))
        {
            savepoint.Restore();
            return false;
        }

        if (!ExpectOperator(")", out Token? rightBracket))
        {
            savepoint.Restore();
            return false;
        }

        if (!ExpectOneValue(out Expression? value, false))
        {
            savepoint.Restore();
            return false;
        }

        typeCast = new ManagedTypeCastExpression(value, type, new TokenPair(leftBracket, rightBracket), File);
        return true;
    }

    bool ExpectVariableAddressGetter([NotNullWhen(true)] out GetReferenceExpression? statement)
    {
        ParseRestorePoint savepoint = SavePoint();
        statement = null;

        if (!ExpectOperator("&", out Token? refToken))
        {
            savepoint.Restore();
            return false;
        }

        if (!ExpectOneValue(out Expression? prevStatement, false))
        {
            savepoint.Restore();
            return false;
        }

        refToken.AnalyzedType = TokenAnalyzedType.OtherOperator;

        statement = new GetReferenceExpression(refToken, prevStatement, File);
        return true;
    }

    bool ExpectVariableAddressFinder([NotNullWhen(true)] out DereferenceExpression? statement)
    {
        ParseRestorePoint savepoint = SavePoint();
        statement = null;

        if (!ExpectOperator("*", out Token? refToken))
        {
            savepoint.Restore();
            return false;
        }

        if (!ExpectOneValue(out Expression? prevStatement, false))
        {
            savepoint.Restore();
            return false;
        }

        refToken.AnalyzedType = TokenAnalyzedType.OtherOperator;

        statement = new DereferenceExpression(refToken, prevStatement, File);
        return true;
    }

    bool ExpectUnaryOperatorCall([NotNullWhen(true)] out UnaryOperatorCallExpression? result)
    {
        ParseRestorePoint savepoint = SavePoint();
        result = null;

        if (!ExpectOperator(UnaryPrefixOperators, out Token? unaryPrefixOperator))
        {
            savepoint.Restore();
            return false;
        }

        if (!ExpectOneValue(out Expression? statement))
        {
            statement = new MissingExpression(unaryPrefixOperator.Position.After(), File);
            Diagnostics.Add(DiagnosticAt.Error($"Expected value after unary prefix operator `{unaryPrefixOperator}`", unaryPrefixOperator.Position.After(), File));
        }

        unaryPrefixOperator.AnalyzedType = TokenAnalyzedType.MathOperator;

        result = new UnaryOperatorCallExpression(unaryPrefixOperator, statement, File);
        return true;
    }

    bool ExpectOperatorArgument([NotNullWhen(true)] out Expression? argument, ImmutableArray<string> validModifiers)
    {
        if (!ExpectIdentifier(out Token? modifier, validModifiers))
        {
            return ExpectOneValue(out argument);
        }

        modifier.AnalyzedType = TokenAnalyzedType.Keyword;

        if (!ExpectOneValue(out Expression? expression))
        {
            argument = new IdentifierExpression(modifier, File);
            Diagnostics.Add(DiagnosticAt.Warning($"is this ok?", argument));
            return true;
        }

        argument = new ArgumentExpression(modifier, expression, File);
        return true;
    }

    bool ExpectStatementExpression([NotNullWhen(true)] out Expression? result)
    {
        ParseRestorePoint savepoint = SavePoint();

        if (!ExpectOneValue(out result))
        {
            return false;
        }

        //if (!IsExpression && !IsStatementExpression(result))
        //{
        //    savepoint.Restore();
        //    return false;
        //}

        return true;
    }

    bool ExpectAnyExpression([NotNullWhen(true)] out Expression? result)
    {
        result = null;

        if (ExpectUnaryOperatorCall(out UnaryOperatorCallExpression? unaryOperatorCall))
        {
            result = unaryOperatorCall;
            return true;
        }

        if (!ExpectOperatorArgument(out Expression? leftStatement, GeneralStatementModifiers)) return false;

        while (true)
        {
            if (!ExpectOperator(BinaryOperators, out Token? binaryOperator)) break;

            if (!ExpectOperatorArgument(out Expression? rightStatement, GeneralStatementModifiers))
            {
                if (!ExpectUnaryOperatorCall(out UnaryOperatorCallExpression? rightUnaryOperatorCall))
                {
                    rightStatement = new MissingArgumentExpression(binaryOperator.Position.After(), File);
                    Diagnostics.Add(DiagnosticAt.Error($"Expected value after binary operator `{binaryOperator}`", rightStatement, false));
                }
                else
                {
                    rightStatement = rightUnaryOperatorCall;
                }
            }

            binaryOperator.AnalyzedType = TokenAnalyzedType.MathOperator;

            int rightSidePrecedence = OperatorPrecedence(binaryOperator.Content);

            BinaryOperatorCallExpression? rightmostStatement = FindRightmostStatement(leftStatement, rightSidePrecedence);
            if (rightmostStatement != null)
            {
                rightmostStatement.Right = new BinaryOperatorCallExpression(binaryOperator, rightmostStatement.Right, rightStatement, File);
            }
            else
            {
                leftStatement = new BinaryOperatorCallExpression(binaryOperator, leftStatement, rightStatement, File);
            }
        }

        result = leftStatement;
        return true;
    }

    bool ExpectArgument([NotNullWhen(true)] out ArgumentExpression? argumentExpression, ImmutableArray<string> validModifiers)
    {
        if (ExpectIdentifier(out Token? modifier, validModifiers))
        {
            modifier.AnalyzedType = TokenAnalyzedType.Keyword;

            if (!ExpectOneValue(out Expression? value))
            {
                value = new MissingExpression(modifier.Position.After(), File);
                Diagnostics.Add(DiagnosticAt.Error($"Expected value after modifier `{modifier}`", value, false));
            }

            argumentExpression = new ArgumentExpression(modifier, value, File);
            return true;
        }

        if (ExpectAnyExpression(out Expression? simpleParameter))
        {
            argumentExpression = ArgumentExpression.Wrap(simpleParameter);
            return true;
        }

        argumentExpression = null;
        return false;
    }

    static BinaryOperatorCallExpression? FindRightmostStatement(Statement? statement, int rightSidePrecedence)
    {
        if (statement is not BinaryOperatorCallExpression leftSide) return null;
        if (OperatorPrecedence(leftSide.Operator.Content) >= rightSidePrecedence) return null;
        if (leftSide.SurroundingBrackets.HasValue) return null;

        BinaryOperatorCallExpression? right = FindRightmostStatement(leftSide.Right, rightSidePrecedence);

        if (right == null) return leftSide;
        return right;
    }

    static int OperatorPrecedence(string @operator) =>
        LanguageOperators.Precedencies.TryGetValue(@operator, out int precedence)
        ? precedence
        : throw new InternalExceptionWithoutContext($"Precedence for operator `{@operator}` not found");

    bool ExpectAnyCall(Expression prevStatement, [NotNullWhen(true)] out AnyCallExpression? anyCall)
    {
        ParseRestorePoint savepoint = SavePoint();
        anyCall = null;

        if (!ExpectArguments(out ArgumentListExpression? argumentList))
        {
            savepoint.Restore();
            return false;
        }

        anyCall = new AnyCallExpression(prevStatement, argumentList, File);
        return true;
    }

    bool ExpectArguments([NotNullWhen(true)] out ArgumentListExpression? argumentList)
    {
        ParseRestorePoint savepoint = SavePoint();
        argumentList = null;

        if (!ExpectOperator("(", out Token? bracketStart))
        {
            savepoint.Restore();
            return false;
        }

        if (ExpectOperator(")", out Token? bracketEnd))
        {
            argumentList = new ArgumentListExpression(ImmutableArray<ArgumentExpression>.Empty, ImmutableArray<Token>.Empty, new TokenPair(bracketStart, bracketEnd), File);
            return true;
        }

        ImmutableArray<ArgumentExpression>.Builder arguments = ImmutableArray.CreateBuilder<ArgumentExpression>();
        ImmutableArray<Token>.Builder commas = ImmutableArray.CreateBuilder<Token>();

        EndlessCheck endlessSafe = new();
        Position lastPosition = bracketStart.Position;

        while (true)
        {
            if (!ExpectArgument(out ArgumentExpression? argument, ArgumentModifiers))
            {
                argument = new MissingArgumentExpression(lastPosition.After(), File);
                Diagnostics.Add(DiagnosticAt.Error($"Expected expression as an argument", argument, false));
            }

            arguments.Add(argument);

            if (ExpectOperator(")", out bracketEnd))
            { break; }

            if (!ExpectOperator(",", out Token? comma))
            {
                Diagnostics.Add(DiagnosticAt.Error($"Expected `,` or `)`", argument.Location.After()));

                int v = CurrentTokenIndex;
                if (ExpectArgument(out _, ArgumentModifiers))
                {
                    CurrentTokenIndex = v;
                    comma = new MissingToken(TokenType.Operator, argument.Position.After(), ",");
                }
                else
                {
                    bracketEnd = new MissingToken(TokenType.Operator, argument.Position.After(), ")");
                    break;
                }
            }

            commas.Add(comma);

            lastPosition = comma.Position;

            endlessSafe.Step();
        }

        argumentList = new ArgumentListExpression(arguments.DrainToImmutable(), commas.DrainToImmutable(), new TokenPair(bracketStart, bracketEnd), File);
        return true;
    }
}
