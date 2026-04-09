using LanguageCore.Parser.Statements;
using LanguageCore.Tokenizing;

namespace LanguageCore.Parser;

public sealed partial class Parser
{
    int CurrentTokenIndex;
    readonly List<Token> Tokens;
    readonly ImmutableArray<Token> OriginalTokens;
    readonly Uri File;

    Location CurrentLocation => new(CurrentPosition, File);
    Position CurrentPosition => CurrentToken?.Position ?? PreviousToken?.Position.After() ?? Position.UnknownPosition;
    Token? CurrentToken => (CurrentTokenIndex >= 0 && CurrentTokenIndex < Tokens.Count) ? Tokens[CurrentTokenIndex] : null;
    Token? PreviousToken => (CurrentTokenIndex >= 1 && CurrentTokenIndex <= Tokens.Count) ? Tokens[CurrentTokenIndex - 1] : null;

    static readonly ImmutableArray<string> AllModifiers = ImmutableArray.Create
    (
        ProtectionKeywords.Export,
        ProtectionKeywords.Private,

        ModifierKeywords.Inline,
        ModifierKeywords.Const,
        ModifierKeywords.Temp,
        ModifierKeywords.This
    );

    static readonly ImmutableArray<string> FunctionModifiers = ImmutableArray.Create
    (
        ProtectionKeywords.Export,
        ModifierKeywords.Inline
    );

    static readonly ImmutableArray<string> AliasModifiers = ImmutableArray.Create
    (
        ProtectionKeywords.Export
    );

    static readonly ImmutableArray<string> EnumModifiers = ImmutableArray.Create
    (
        ProtectionKeywords.Export
    );

    static readonly ImmutableArray<string> FieldModifiers = ImmutableArray.Create
    (
        ProtectionKeywords.Private
    );

    static readonly ImmutableArray<string> GeneralStatementModifiers = ImmutableArray.Create
    (
        ModifierKeywords.Temp
    );

    static readonly ImmutableArray<string> VariableModifiers = ImmutableArray.Create
    (
        ProtectionKeywords.Export,
        ModifierKeywords.Temp,
        ModifierKeywords.Const
    );

    static readonly ImmutableArray<string> ParameterModifiers = ImmutableArray.Create
    (
        ModifierKeywords.This,
        ModifierKeywords.Temp
    );

    static readonly ImmutableArray<string> ArgumentModifiers = ImmutableArray.Create
    (
        ModifierKeywords.Temp
    );

    static readonly ImmutableArray<string> StructModifiers = ImmutableArray.Create
    (
        ProtectionKeywords.Export
    );

    static readonly ImmutableArray<string> ConstructorModifiers = ImmutableArray.Create
    (
        ProtectionKeywords.Export
    );

    static readonly ImmutableArray<string> GeneralFunctionModifiers = ImmutableArray.Create
    (
        ProtectionKeywords.Export
    );

    static readonly ImmutableArray<string> OverloadableOperators = ImmutableArray.Create
    (
        "<<", ">>",
        "+", "-", "*", "/", "%",
        "&", "|", "^",
        "<", ">", ">=", "<=", "!=", "==",
        "&&", "||"
    );

    static readonly ImmutableArray<string> CompoundAssignmentOperators = ImmutableArray.Create
    (
        "+=", "-=", "*=", "/=", "%=",
        "&=", "|=", "^="
    );

    static readonly ImmutableArray<string> BinaryOperators = ImmutableArray.Create
    (
        "<<", ">>",
        "+", "-", "*", "/", "%",
        "&", "|", "^",
        "<", ">", ">=", "<=", "!=", "==", "&&", "||"
    );

    static readonly ImmutableArray<string> UnaryPrefixOperators = ImmutableArray.Create
    (
        "!", "~",
        "-", "+"
    );

    static readonly ImmutableArray<string> IncrementDecrementOperators = ImmutableArray.Create
    (
        "++", "--"
    );

#pragma warning disable RCS1213, IDE0052, CA1823 // Remove unread private members
    static readonly ImmutableArray<string> UnaryPostfixOperators = ImmutableArray<string>.Empty;
#pragma warning restore RCS1213, IDE0052, CA1823

    readonly bool IsExpression;
    readonly DiagnosticsCollection Diagnostics;
    readonly List<FunctionDefinition> Functions = new();
    readonly List<FunctionDefinition> Operators = new();
    readonly Dictionary<string, StructDefinition> Structs = new();
    readonly List<UsingDefinition> Usings = new();
    readonly List<AliasDefinition> AliasDefinitions = new();
    readonly List<EnumDefinition> EnumDefinitions = new();
    readonly List<Statement> TopLevelStatements = new();

    Parser(ImmutableArray<Token> tokens, Uri file, DiagnosticsCollection diagnostics, bool isExpression)
    {
        OriginalTokens = tokens;
        Tokens = tokens.ToList();
        File = file;
        IsExpression = isExpression;
        Diagnostics = diagnostics;
    }

    readonly struct ParseRestorePoint
    {
        readonly Parser Parser;
        readonly int TokenIndex;

        public ParseRestorePoint(Parser parser, int tokenIndex)
        {
            Parser = parser;
            TokenIndex = tokenIndex;
        }

        public void Restore()
        {
            Parser.CurrentTokenIndex = TokenIndex;
        }
    }

    ParseRestorePoint SavePoint() => new(this, CurrentTokenIndex);

    public static ParserResult Parse(ImmutableArray<Token> tokens, Uri file, DiagnosticsCollection diagnostics)
        => new Parser(tokens, file, diagnostics, false).ParseInternal();

    public static ParserResult ParseExpression(ImmutableArray<Token> tokens, Uri file, DiagnosticsCollection diagnostics)
        => new Parser(tokens, file, diagnostics, true).ParseInternal();

#if UNITY
    static readonly Unity.Profiling.ProfilerMarker _marker = new("LanguageCore.Parser");
#endif
    ParserResult ParseInternal()
    {
#if UNITY
        using Unity.Profiling.ProfilerMarker.AutoScope _1 = _marker.Auto();
#endif
        CurrentTokenIndex = 0;

        ParseCodeHeader();

        SkipCrapTokens();

        EndlessCheck endlessSafe = new();
        while (CurrentToken is not null && ParseCodeBlock())
        {
            SkipCrapTokens();
            endlessSafe.Step();
        }

        SkipCrapTokens();

        if (CurrentToken is not null)
        {
            Diagnostics.Add(DiagnosticAt.Error($"Unexpected token `{CurrentToken}`", CurrentToken, File, false));
        }

        return new ParserResult(
            Functions.ToImmutableArray(),
            Operators.ToImmutableArray(),
            Structs.Values.ToImmutableArray(),
            Usings.ToImmutableArray(),
            AliasDefinitions.ToImmutableArray(),
            EnumDefinitions.ToImmutableArray(),
            TopLevelStatements.ToImmutableArray(),
            OriginalTokens,
            Tokens.ToImmutableArray()
        );
    }

    bool ExpectUsing([NotNullWhen(true)] out UsingDefinition? usingDefinition)
    {
        usingDefinition = null;

        if (!ExpectIdentifier(DeclarationKeywords.Using, out Token? keyword))
        { return false; }

        keyword.AnalyzedType = TokenAnalyzedType.Keyword;

        SkipCrapTokens();

        List<Token> tokens = new();

        if (CurrentToken is not null && CurrentToken.TokenType == TokenType.LiteralString)
        {
            tokens.Add(CurrentToken);
            CurrentToken.AnalyzedType = TokenAnalyzedType.None;
            CurrentTokenIndex++;
        }
        else
        {
            EndlessCheck endlessSafe = new();
            while (ExpectIdentifier(out Token? pathIdentifier))
            {
                tokens.Add(pathIdentifier);

                if (!ExpectOperator(".")) break;

                endlessSafe.Step();
            }
        }

        if (tokens.Count == 0)
        {
            Diagnostics.Add(DiagnosticAt.Error($"Expected identifier or string literal after keyword `{DeclarationKeywords.Using}`", keyword.Position.After(), File, false));
            usingDefinition = new UsingDefinition(keyword, ImmutableArray.Create<Token>(new MissingToken(TokenType.Identifier, keyword.Position.After())), File);
            return true;
        }

        usingDefinition = new UsingDefinition(keyword, tokens.ToImmutableArray(), File);

        if (!ExpectOperator(";"))
        { Diagnostics.Add(DiagnosticAt.Warning($"You forgot the semicolon", usingDefinition.Position.After(), File)); }

        return true;
    }

    void ParseCodeHeader()
    {
        while (true)
        {
            if (!ExpectUsing(out UsingDefinition? usingDefinition)) break;

            Usings.Add(usingDefinition);
        }
    }

    bool ParseCodeBlock()
    {
        OrderedDiagnosticCollection diagnostics = new();
        if (ExpectStructDefinition(diagnostics)) { }
        else if (ExpectFunctionDefinition(out FunctionDefinition? functionDefinition, diagnostics))
        { Functions.Add(functionDefinition); }
        else if (ExpectOperatorDefinition(out FunctionDefinition? operatorDefinition, diagnostics))
        { Operators.Add(operatorDefinition); }
        else if (ExpectAliasDefinition(out AliasDefinition? aliasDefinition, diagnostics))
        { AliasDefinitions.Add(aliasDefinition); }
        else if (ExpectEnumDefinition(out EnumDefinition? enumDefinition, diagnostics))
        { EnumDefinitions.Add(enumDefinition); }
        else if (ExpectStatement(out Statement? statement, diagnostics))
        { TopLevelStatements.Add(statement); }
        else
        {
            Diagnostics.AddRange(diagnostics.Compile());
            return false;
        }

        return true;
    }

    ImmutableArray<Token> ExpectModifiers() => ExpectModifiers(AllModifiers);

    ImmutableArray<Token> ExpectModifiers(ImmutableArray<string> modifiers)
    {
        ImmutableArray<Token>.Builder? result = null;

        EndlessCheck endlessSafe = new();
        while (true)
        {
            if (ExpectIdentifier(out Token? modifier, modifiers))
            {
                result ??= ImmutableArray.CreateBuilder<Token>();
                modifier.AnalyzedType = TokenAnalyzedType.Keyword;
                result.Add(modifier);
            }
            else
            { break; }

            endlessSafe.Step();
        }

        return result?.DrainToImmutable() ?? ImmutableArray<Token>.Empty;
    }

    void CheckModifiers(IEnumerable<Token> modifiers, ImmutableArray<string> validModifiers)
    {
        foreach (Token modifier in modifiers)
        {
            if (!validModifiers.Contains(modifier.Content))
            { Diagnostics.Add(DiagnosticAt.Error($"Modifier `{modifier}` is not valid in the current context", modifier, File, false)); }
        }
    }

    bool ExpectIdentifier([NotNullWhen(true)] out Token? result) => ExpectIdentifier("", out result);
    bool ExpectIdentifier(string name, [NotNullWhen(true)] out Token? result)
    {
        result = null;
        SkipCrapTokens();
        if (CurrentToken is null) return false;
        if (CurrentToken.TokenType != TokenType.Identifier) return false;
        if (name.Length > 0 && CurrentToken.Content != name) return false;
        CurrentToken.AnalyzedType = TokenAnalyzedType.None;

        result = CurrentToken;
        CurrentTokenIndex++;

        return true;
    }
    bool ExpectIdentifier([NotNullWhen(true)] out Token? result, ImmutableArray<string> names)
    {
        foreach (string name in names)
        {
            if (ExpectIdentifier(name, out result))
            { return true; }
        }
        result = null;
        return false;
    }

    bool ExpectOperator(string name) => ExpectOperator(name, out _);
    bool ExpectOperator(ImmutableArray<string> name, [NotNullWhen(true)] out Token? result)
    {
        result = null;
        SkipCrapTokens();
        if (CurrentToken is null) return false;
        if (CurrentToken.TokenType != TokenType.Operator) return false;
        if (!name.Contains(CurrentToken.Content)) return false;
        CurrentToken.AnalyzedType = TokenAnalyzedType.None;

        result = CurrentToken;
        CurrentTokenIndex++;

        return true;
    }
    bool ExpectOperator(string name, [NotNullWhen(true)] out Token? result)
    {
        result = null;
        SkipCrapTokens();
        if (CurrentToken is null) return false;
        if (CurrentToken.TokenType != TokenType.Operator) return false;
        if (name.Length > 0 && CurrentToken.Content != name) return false;
        CurrentToken.AnalyzedType = TokenAnalyzedType.None;

        result = CurrentToken;
        CurrentTokenIndex++;

        return true;
    }

    void SkipCrapTokens()
    {
        while (CurrentToken is not null &&
               CurrentToken.TokenType is
               TokenType.Whitespace or
               TokenType.LineBreak or
               TokenType.Comment or
               TokenType.CommentMultiline or
               TokenType.PreprocessIdentifier or
               TokenType.PreprocessArgument or
               TokenType.PreprocessSkipped)
        { CurrentTokenIndex++; }
    }

    static bool NeedSemicolon(Statement statement) => statement is not (
        ForLoopStatement or
        WhileLoopStatement or
        Block or
        BranchStatementBase or
        InstructionLabelDeclaration or
        LambdaExpression
    );
}
