using LanguageCore.Parser.Statements;
using LanguageCore.Tokenizing;

namespace LanguageCore.Parser;

public sealed partial class Parser
{
    bool ExpectStatement([NotNullWhen(true)] out Statement? statement, OrderedDiagnosticCollection diagnostics)
    {
        if (ExpectOperator(";", out Token? semicolon))
        {
            statement = new EmptyStatement(semicolon.Position.Before(), File);
            Diagnostics.Add(DiagnosticAt.Warning($"Empty statement?", semicolon, File));
            return true;
        }

        if (!ExpectStatementUnchecked(out statement, diagnostics))
        {
            return false;
        }

        if (!IsExpression) SetStatementThings(statement);

        if (NeedSemicolon(statement))
        {
            if (!ExpectOperator(";", out semicolon) && !IsExpression)
            { Diagnostics.Add(DiagnosticAt.Warning($"You forgot the semicolon", statement.Position.After(), File)); }
        }
        else
        {
            if (ExpectOperator(";", out semicolon))
            { Diagnostics.Add(DiagnosticAt.Warning($"Unecessary semicolon", semicolon, File)); }
        }

        statement.Semicolon = semicolon;

        return true;
    }

    bool ExpectStatementUnchecked([NotNullWhen(true)] out Statement? statement, OrderedDiagnosticCollection diagnostics)
    {
        if (ExpectInstructionLabel(out InstructionLabelDeclaration? instructionLabel, diagnostics))
        {
            statement = instructionLabel;
            return true;
        }

        if (ExpectWhileStatement(out WhileLoopStatement? whileLoop, diagnostics))
        {
            statement = whileLoop;
            return true;
        }

        if (ExpectForStatement(out ForLoopStatement? forLoop, diagnostics))
        {
            statement = forLoop;
            return true;
        }

        if (ExpectKeywordCall(StatementKeywords.Return, 0, 1, out KeywordCallStatement? keywordCallReturn, diagnostics))
        {
            statement = keywordCallReturn;
            return true;
        }

        if (ExpectKeywordCall(StatementKeywords.Goto, 1, out KeywordCallStatement? keywordCallGoto, diagnostics))
        {
            statement = keywordCallGoto;
            return true;
        }

        if (ExpectKeywordCall(StatementKeywords.Crash, 1, out KeywordCallStatement? keywordCallThrow, diagnostics))
        {
            statement = keywordCallThrow;
            return true;
        }

        if (ExpectKeywordCall(StatementKeywords.Break, 0, out KeywordCallStatement? keywordCallBreak, diagnostics))
        {
            statement = keywordCallBreak;
            return true;
        }

        if (ExpectKeywordCall(StatementKeywords.Delete, 1, out KeywordCallStatement? keywordCallDelete, diagnostics))
        {
            statement = keywordCallDelete;
            return true;
        }

        if (ExpectIfStatement(out IfBranchStatement? ifStatement, diagnostics))
        {
            statement = ifStatement;
            return true;
        }

        if (ExpectVariableDeclaration(out VariableDefinition? variableDeclaration, diagnostics))
        {
            statement = variableDeclaration;
            return true;
        }

        if (ExpectAnySetter(out AssignmentStatement? assignment, diagnostics))
        {
            statement = assignment;
            return true;
        }

        if (ExpectStatementExpression(out Expression? expression))
        {
            statement = expression;
            return true;
        }

        if (ExpectBlock(out Block? block, diagnostics: diagnostics))
        {
            statement = block;
            return true;
        }

        statement = null;
        return false;
    }

    bool IsStatementExpression(Expression expression) => expression is not (
        LiteralExpression or
        IdentifierExpression or
        NewInstanceExpression or
        ConstructorCallExpression or
        BinaryOperatorCallExpression or
        UnaryOperatorCallExpression or
        IndexCallExpression or
        FieldExpression or
        DereferenceExpression or
        GetReferenceExpression or
        LambdaExpression or
        ListExpression or
        ManagedTypeCastExpression or
        ReinterpretExpression
    );

    void SetStatementThings(Statement statement)
    {
        if (statement is Expression expression)
        {
            if (!IsStatementExpression(expression))
            { Diagnostics.Add(DiagnosticAt.Warning("Unexpected expression", statement)); }

            expression.SaveValue = false;
        }
    }

    bool ExpectBlock([NotNullWhen(true)] out Block? block, bool consumeSemicolon = true, OrderedDiagnosticCollection? diagnostics = null)
    {
        block = null;
        ParseRestorePoint savepoint = SavePoint();

        if (!ExpectOperator("{", out Token? bracketStart))
        {
            diagnostics?.Add(0, DiagnosticAt.Error($"Expected `{{` for block", CurrentLocation, false));
            savepoint.Restore();
            return false;
        }

        if (ExpectOperator("}", out Token? bracketEnd))
        {
            block = new Block(ImmutableArray<Statement>.Empty, new TokenPair(bracketStart, bracketEnd), File);
        }
        else
        {
            ImmutableArray<Statement>.Builder statements = ImmutableArray.CreateBuilder<Statement>();
            EndlessCheck endlessSafe = new();
            Position lastPosition = bracketStart.Position;

            while (!ExpectOperator("}", out bracketEnd))
            {
                OrderedDiagnosticCollection statementDiagnostics = new();
                if (!ExpectStatement(out Statement? statement, statementDiagnostics))
                {
                    statement = new MissingStatement(lastPosition.After(), File);
                    Diagnostics.Add(DiagnosticAt.Error($"Expected `}}` or a statement", statement, false).WithSuberrors(statementDiagnostics.Compile()));
                    CurrentTokenIndex++;
                }

                statements.Add(statement);
                lastPosition = statement.Position;

                endlessSafe.Step();
            }
            block = new Block(statements.DrainToImmutable(), new TokenPair(bracketStart, bracketEnd), File);
        }

        if (consumeSemicolon && ExpectOperator(";", out Token? semicolon))
        {
            block.Semicolon = semicolon;
            Diagnostics.Add(DiagnosticAt.Warning("Unnecessary semicolon", semicolon, File).WithTag(DiagnosticTag.Unnecessary));
        }

        return true;
    }

    bool ExpectVariableDeclaration([NotNullWhen(true)] out VariableDefinition? variableDeclaration, OrderedDiagnosticCollection diagnostics)
    {
        variableDeclaration = null;
        ParseRestorePoint savepoint = SavePoint();

        ImmutableArray<AttributeUsage> attributes = ExpectAttributes();

        ImmutableArray<Token> modifiers = ExpectModifiers(VariableModifiers);

        TypeInstance? possibleType;
        if (ExpectIdentifier(StatementKeywords.Var, out Token? implicitTypeKeyword))
        {
            implicitTypeKeyword.AnalyzedType = TokenAnalyzedType.Keyword;
            possibleType = new TypeInstanceSimple(implicitTypeKeyword, File);
        }
        else if (!ExpectType(AllowedType.StackArrayWithoutLength | AllowedType.FunctionPointer, out possibleType, out DiagnosticAt? typeError))
        {
            savepoint.Restore();
            diagnostics.Add(0, DiagnosticAt.Error($"Expected `{StatementKeywords.Var}` or a type for variable definition", CurrentLocation, false).WithSuberrors(typeError));
            return false;
        }

        if (!ExpectIdentifier(out Token? possibleVariableName))
        {
            diagnostics.Add(1, DiagnosticAt.Error($"Expected identifier for variable definition", possibleType.Location.After(), false));
            savepoint.Restore();
            return false;
        }

        possibleVariableName.AnalyzedType = TokenAnalyzedType.VariableName;

        Expression? initialValue = null;

        if (ExpectOperator("=", out Token? eqOperatorToken))
        {
            if (!ExpectAnyExpression(out initialValue))
            {
                initialValue = new MissingExpression(eqOperatorToken.Position.After(), File);
                Diagnostics.Add(DiagnosticAt.Error("Expected initial value after `=` in variable declaration", initialValue, false));
            }
        }
        else
        {
            if (possibleType == StatementKeywords.Var)
            {
                Diagnostics.Add(DiagnosticAt.Error("Initial value for variable declaration with implicit type is required", possibleType, File, false));
            }
        }

        variableDeclaration = new VariableDefinition(
            attributes,
            modifiers,
            possibleType,
            possibleVariableName,
            initialValue,
            File
        );
        return true;
    }

    bool ExpectForStatement([NotNullWhen(true)] out ForLoopStatement? forLoop, OrderedDiagnosticCollection diagnostics)
    {
        forLoop = null;
        ParseRestorePoint savepoint = SavePoint();

        if (!ExpectIdentifier(StatementKeywords.For, out Token? keyword))
        {
            diagnostics.Add(0, DiagnosticAt.Error($"Expected `{StatementKeywords.For}`", CurrentLocation, false));
            savepoint.Restore();
            return false;
        }

        DiagnosticAt? error = null;

        keyword.AnalyzedType = TokenAnalyzedType.Statement;

        if (!ExpectOperator("(", out Token? bracketStart))
        {
            bracketStart = new MissingToken(TokenType.Operator, keyword.Position.After(), "(");
            error ??= DiagnosticAt.Error($"Expected `(` after `{keyword}`", bracketStart, File, false);
        }

        Statement? initialization;
        Expression? condition;
        Statement? step;
        Position lastPosition = bracketStart.Position;

        if (ExpectOperator(";", out Token? semicolon1))
        {
            initialization = null;
            lastPosition = semicolon1.Position;
        }
        else
        {
            OrderedDiagnosticCollection statementDiagnostics = new();
            if (!ExpectStatementUnchecked(out initialization, statementDiagnostics))
            {
                initialization = new MissingStatement(lastPosition.After(), File);
                error ??= DiagnosticAt.Error($"Expected a statement or `;`", initialization, false).WithSuberrors(statementDiagnostics.Compile());
            }

            SetStatementThings(initialization);
            lastPosition = initialization.Position;

            if (!ExpectOperator(";", out semicolon1))
            {
                if (error is null) Diagnostics.Add(DiagnosticAt.Warning($"Expected `;`", lastPosition.After(), File));
            }
            else
            {
                initialization.Semicolon = semicolon1;
                lastPosition = semicolon1.Position;
            }
        }

        if (ExpectOperator(";", out Token? semicolon2))
        {
            condition = null;
            lastPosition = semicolon2.Position;
        }
        else
        {
            if (!ExpectAnyExpression(out condition))
            {
                condition = new MissingExpression(lastPosition.After(), File);
                error ??= DiagnosticAt.Error($"Expected a condition or `;`", condition, false);
            }

            lastPosition = condition.Position;

            if (!ExpectOperator(";", out semicolon2))
            {
                if (error is null) Diagnostics.Add(DiagnosticAt.Warning($"Expected `;`", lastPosition.After(), File));
            }
            else
            {
                condition.Semicolon = semicolon2;
                lastPosition = semicolon2.Position;
            }
        }

        if (ExpectOperator(")", out Token? bracketEnd))
        {
            step = null;
            lastPosition = bracketEnd.Position;
        }
        else
        {
            OrderedDiagnosticCollection statementDiagnostics = new();
            if (!ExpectStatementUnchecked(out step, statementDiagnostics))
            {
                step = new MissingStatement(lastPosition.After(), File);
                error ??= DiagnosticAt.Error($"Expected a statement or `)`", step, false).WithSuberrors(statementDiagnostics.Compile());
            }

            SetStatementThings(step);
            lastPosition = step.Position;

            if (!ExpectOperator(")", out bracketEnd))
            {
                error ??= DiagnosticAt.Error($"Expected `)`", step.Position.After(), File, false);
            }
            else
            {
                lastPosition = bracketEnd.Position;
            }
        }

        if (!ExpectBlock(out Block? block))
        {
            block = new MissingBlock(lastPosition.After(), File);
            error ??= DiagnosticAt.Error($"Expected block", block, false);
        }

        Diagnostics.Add(error);
        forLoop = new ForLoopStatement(keyword, initialization, condition, step, block, File);
        return true;
    }

    bool ExpectWhileStatement([NotNullWhen(true)] out WhileLoopStatement? whileLoop, OrderedDiagnosticCollection diagnostics)
    {
        whileLoop = null;
        ParseRestorePoint savepoint = SavePoint();

        if (!ExpectIdentifier(StatementKeywords.While, out Token? keyword))
        {
            diagnostics.Add(0, DiagnosticAt.Error($"Expected `{StatementKeywords.While}`", CurrentLocation, false));
            savepoint.Restore();
            return false;
        }

        keyword.AnalyzedType = TokenAnalyzedType.Statement;
        DiagnosticAt? error = null;

        if (!ExpectOperator("(", out Token? bracketStart))
        {
            bracketStart = new MissingToken(TokenType.Operator, keyword.Position.After(), "(");
            error ??= DiagnosticAt.Error($"Expected `(`", bracketStart, File, false);
        }

        if (!ExpectAnyExpression(out Expression? condition))
        {
            condition = new MissingExpression(bracketStart.Position.After(), File);
            error ??= DiagnosticAt.Error($"Expected condition after `{bracketStart}`", condition, false);
        }

        if (!ExpectOperator(")", out Token? bracketEnd))
        {
            bracketEnd = new MissingToken(TokenType.Operator, condition.Position.After(), ")");
            error ??= DiagnosticAt.Error($"Expected `)`", bracketEnd, File, false);
        }

        OrderedDiagnosticCollection statementDiagnostics = new();
        if (!ExpectStatement(out Statement? block, statementDiagnostics))
        {
            block = new MissingStatement(bracketEnd.Position.After(), File);
            error ??= DiagnosticAt.Error($"Expected a statement", block, false).WithSuberrors(statementDiagnostics.Compile());
        }

        Diagnostics.Add(error);
        whileLoop = new WhileLoopStatement(keyword, condition, block, File);
        return true;
    }

    bool ExpectIfStatement([NotNullWhen(true)] out IfBranchStatement? ifStatement, OrderedDiagnosticCollection diagnostics)
    {
        ifStatement = null;
        ParseRestorePoint savepoint = SavePoint();

        if (!ExpectIdentifier(StatementKeywords.If, out Token? ifKeyword))
        {
            diagnostics.Add(0, DiagnosticAt.Error($"Expected `{StatementKeywords.If}`", CurrentLocation, false));
            savepoint.Restore();
            return false;
        }

        ifKeyword.AnalyzedType = TokenAnalyzedType.Statement;

        DiagnosticAt? error = null;

        if (!ExpectOperator("(", out Token? bracketStart))
        {
            bracketStart = new MissingToken(TokenType.Operator, ifKeyword.Position.After(), "(");
            error ??= DiagnosticAt.Error($"Expected a `(`", bracketStart, File, false);
        }

        if (!ExpectAnyExpression(out Expression? condition))
        {
            condition = new MissingExpression(bracketStart.Position.After(), File);
            error ??= DiagnosticAt.Error($"Expected a condition", condition, false);
        }

        if (!ExpectOperator(")", out Token? bracketEnd))
        {
            bracketEnd = new MissingToken(TokenType.Operator, condition.Position.After(), ")");
            error ??= DiagnosticAt.Error($"Expected a `)`", bracketEnd, File, false);
        }

        OrderedDiagnosticCollection statementDiagnostics1 = new();
        if (!ExpectStatement(out Statement? ifBlock, statementDiagnostics1))
        {
            ifBlock = new MissingBlock(bracketEnd.Position.After(), File);
            error ??= DiagnosticAt.Error($"Expected a statement", ifBlock, false).WithSuberrors(statementDiagnostics1.Compile());
        }

        ElseBranchStatement? elseBranch = null;

        if (ExpectIdentifier(StatementKeywords.Else, out Token? elseKeyword))
        {
            elseKeyword.AnalyzedType = TokenAnalyzedType.Statement;

            OrderedDiagnosticCollection statementDiagnostics2 = new();
            if (!ExpectStatement(out Statement? elseBlock, statementDiagnostics2))
            {
                elseBlock = new MissingBlock(elseKeyword.Position.After(), File);
                error ??= DiagnosticAt.Error($"Expected a statement", elseBlock, false).WithSuberrors(statementDiagnostics2.Compile());
            }

            elseBranch = new ElseBranchStatement(elseKeyword, elseBlock, File);
        }

        Diagnostics.Add(error);
        ifStatement = new IfBranchStatement(ifKeyword, condition, ifBlock, elseBranch, File);
        return true;
    }

    bool ExpectAnySetter([NotNullWhen(true)] out AssignmentStatement? assignment, OrderedDiagnosticCollection diagnostics)
    {
        if (ExpectShortOperator(out ShortOperatorCall? shortOperatorCall, diagnostics))
        {
            assignment = shortOperatorCall;
            return true;
        }

        if (ExpectCompoundSetter(out CompoundAssignmentStatement? compoundAssignment, diagnostics))
        {
            assignment = compoundAssignment;
            return true;
        }

        if (ExpectSetter(out SimpleAssignmentStatement? simpleSetter, diagnostics))
        {
            assignment = simpleSetter;
            return true;
        }

        assignment = null;
        return false;
    }

    bool ExpectSetter([NotNullWhen(true)] out SimpleAssignmentStatement? assignment, OrderedDiagnosticCollection diagnostics)
    {
        assignment = null;
        ParseRestorePoint savepoint = SavePoint();

        if (!ExpectAnyExpression(out Expression? leftStatement))
        {
            diagnostics.Add(0, DiagnosticAt.Error($"Expected expression for assignment statement", CurrentLocation, false));
            savepoint.Restore();
            return false;
        }

        if (!ExpectOperator("=", out Token? @operator))
        {
            diagnostics.Add(1, DiagnosticAt.Error($"Expected `=` for assignment statement", leftStatement.Location.After(), false));
            savepoint.Restore();
            return false;
        }

        @operator.AnalyzedType = TokenAnalyzedType.OtherOperator;

        if (!ExpectAnyExpression(out Expression? valueToAssign))
        {
            valueToAssign = new MissingExpression(@operator.Position.After(), File);
            Diagnostics.Add(DiagnosticAt.Error("Expected an expression after assignment operator", valueToAssign, false));
        }

        assignment = new SimpleAssignmentStatement(@operator, leftStatement, valueToAssign, File);
        return true;
    }

    bool ExpectCompoundSetter([NotNullWhen(true)] out CompoundAssignmentStatement? compoundAssignment, OrderedDiagnosticCollection diagnostics)
    {
        compoundAssignment = null;
        ParseRestorePoint savepoint = SavePoint();

        if (!ExpectAnyExpression(out Expression? leftStatement))
        {
            diagnostics.Add(0, DiagnosticAt.Error($"Expected expression for compound assignment statement", CurrentLocation, false));
            savepoint.Restore();
            return false;
        }

        if (!ExpectOperator(CompoundAssignmentOperators, out Token? @operator))
        {
            diagnostics.Add(1, DiagnosticAt.Error($"Expected operator ({string.Join(" | ", CompoundAssignmentOperators)}) for compound assignment statement", leftStatement.Location.After(), false));
            savepoint.Restore();
            return false;
        }

        @operator.AnalyzedType = TokenAnalyzedType.MathOperator;

        if (!ExpectAnyExpression(out Expression? valueToAssign))
        {
            valueToAssign = new MissingExpression(@operator.Position.After(), File);
            Diagnostics.Add(DiagnosticAt.Error("Expected an expression after compound assignment operator", valueToAssign, false));
        }

        compoundAssignment = new CompoundAssignmentStatement(@operator, leftStatement, valueToAssign, File);
        return true;
    }

    bool ExpectShortOperator([NotNullWhen(true)] out ShortOperatorCall? shortOperatorCall, OrderedDiagnosticCollection diagnostics)
    {
        ParseRestorePoint savepoint = SavePoint();
        shortOperatorCall = null;

        if (!ExpectAnyExpression(out Expression? expression))
        {
            diagnostics.Add(0, DiagnosticAt.Error($"Expected expression for increment/decrement expression", CurrentLocation, false));
            savepoint.Restore();
            return false;
        }

        if (!ExpectOperator(IncrementDecrementOperators, out Token? @operator))
        {
            diagnostics.Add(1, DiagnosticAt.Error($"Expected operator ({string.Join(" | ", IncrementDecrementOperators)}) for increment/decrement expression", expression.Location.After(), false));
            savepoint.Restore();
            return false;
        }

        @operator.AnalyzedType = TokenAnalyzedType.MathOperator;

        shortOperatorCall = new ShortOperatorCall(@operator, expression, File);
        return true;
    }

    bool ExpectKeywordCall(string name, int parameterCount, [NotNullWhen(true)] out KeywordCallStatement? keywordCall, OrderedDiagnosticCollection diagnostics)
        => ExpectKeywordCall(name, parameterCount, parameterCount, out keywordCall, diagnostics);
    bool ExpectKeywordCall(string name, int minArgumentCount, int maxArgumentCount, [NotNullWhen(true)] out KeywordCallStatement? keywordCall, OrderedDiagnosticCollection diagnostics)
    {
        ParseRestorePoint savepoint = SavePoint();
        keywordCall = null;

        if (!ExpectIdentifier(name, out Token? keyword))
        {
            diagnostics.Add(0, DiagnosticAt.Error($"Expected `{name}`", CurrentLocation, false));
            savepoint.Restore();
            return false;
        }

        keyword.AnalyzedType = TokenAnalyzedType.Statement;

        ImmutableArray<Expression>.Builder arguments = ImmutableArray.CreateBuilder<Expression>();

        EndlessCheck endlessSafe = new();
        while (arguments.Count < maxArgumentCount)
        {
            endlessSafe.Step();

            if (!ExpectAnyExpression(out Expression? argument)) break;

            arguments.Add(argument);
        }

        keywordCall = new(keyword, arguments.DrainToImmutable(), File);

        if (minArgumentCount == maxArgumentCount)
        {
            if (keywordCall.Arguments.Length != minArgumentCount)
            {
                Diagnostics.Add(DiagnosticAt.Error($"Keyword-call `{keyword}` requires {minArgumentCount} arguments but you passed {keywordCall.Arguments.Length}", keywordCall, File, false));
            }
        }
        else
        {
            if (keywordCall.Arguments.Length < minArgumentCount)
            { Diagnostics.Add(DiagnosticAt.Error($"Keyword-call `{keyword}` requires minimum {minArgumentCount} arguments but you passed {keywordCall.Arguments.Length}", keywordCall, File, false)); }

            if (keywordCall.Arguments.Length > maxArgumentCount)
            { Diagnostics.Add(DiagnosticAt.Error($"Keyword-call `{keyword}` requires maximum {maxArgumentCount} arguments but you passed {keywordCall.Arguments.Length}", keywordCall, File, false)); }
        }

        return true;
    }

    bool ExpectInstructionLabel([NotNullWhen(true)] out InstructionLabelDeclaration? instructionLabel, OrderedDiagnosticCollection diagnostics)
    {
        instructionLabel = null;
        ParseRestorePoint savepoint = SavePoint();

        if (!ExpectIdentifier(out Token? identifier))
        {
            diagnostics.Add(0, DiagnosticAt.Error($"Expected identifier for instruction label definition", CurrentLocation, false));
            savepoint.Restore();
            return false;
        }

        if (!ExpectOperator(":", out Token? colon))
        {
            diagnostics.Add(1, DiagnosticAt.Error($"Expected `:` after instruction label definition identifier", identifier.Position.After(), File, false));
            savepoint.Restore();
            return false;
        }

        identifier.AnalyzedType = TokenAnalyzedType.InstructionLabel;

        instructionLabel = new InstructionLabelDeclaration(
            identifier,
            colon,
            File
        );
        return true;
    }
}
