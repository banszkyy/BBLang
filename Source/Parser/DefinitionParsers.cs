using LanguageCore.Parser.Statements;
using LanguageCore.Tokenizing;

namespace LanguageCore.Parser;

public sealed partial class Parser
{
    bool ExpectOperatorDefinition([NotNullWhen(true)] out FunctionDefinition? function, OrderedDiagnosticCollection diagnostic)
    {
        ParseRestorePoint savepoint = SavePoint();
        function = null;

        ImmutableArray<AttributeUsage> attributes = ExpectAttributes();

        ImmutableArray<Token> modifiers = ExpectModifiers();

        if (!ExpectType(AllowedType.None, out TypeInstance? possibleType, out DiagnosticAt? typeError))
        {
            diagnostic.Add(0, DiagnosticAt.Error($"Expected type for operator definition", CurrentLocation, false).WithSuberrors(typeError));
            savepoint.Restore();
            return false;
        }

        if (!ExpectOperator(OverloadableOperators, out Token? possibleName))
        {
            if (OverloadableOperators.Contains("*") &&
                possibleType is TypeInstancePointer _possibleTypePointer)
            {
                possibleType = _possibleTypePointer.To;
                possibleName = _possibleTypePointer.Operator;
            }
            else if (OverloadableOperators.Contains("&") &&
                possibleType is TypeInstanceReference _possibleTypeReference)
            {
                possibleType = _possibleTypeReference.To;
                possibleName = _possibleTypeReference.Operator;
            }
            else
            {
                int callOperatorParseStart = CurrentTokenIndex;
                if (ExpectOperator("(", out Token? opening) && ExpectOperator(")", out Token? closing) && CurrentToken?.Content == "(")
                {
                    possibleName = opening + closing;
                }
                else
                {
                    CurrentTokenIndex = callOperatorParseStart;

                    diagnostic.Add(1, DiagnosticAt.Error($"Expected an operator for operator definition", possibleType.Location.After(), false));
                    savepoint.Restore();
                    return false;
                }
            }
        }

        OrderedDiagnosticCollection parameterDiagnostics = new();
        if (!ExpectParameters(ImmutableArray.Create(ModifierKeywords.Temp), false, out ParameterDefinitionCollection? parameters, parameterDiagnostics))
        {
            diagnostic.Add(2, DiagnosticAt.Error($"Expected parameter list for operator", possibleName.Position.After(), File, false), parameterDiagnostics.ToImmutableArray());
            savepoint.Restore();
            return false;
        }

        Block? block = null;

        if (!ExpectOperator(";") && !ExpectBlock(out block))
        {
            diagnostic.Add(3, DiagnosticAt.Error($"Expected `;` or block", parameters.Brackets.End.Position.After(), File, false));
            savepoint.Restore();
            return false;
        }

        CheckModifiers(modifiers, FunctionModifiers);

        possibleName.AnalyzedType = TokenAnalyzedType.FunctionName;

        function = new FunctionDefinition(
            attributes,
            modifiers,
            possibleType,
            possibleName,
            parameters,
            null,
            block,
            File
        );
        return true;
    }

    bool ExpectAliasDefinition([NotNullWhen(true)] out AliasDefinition? aliasDefinition, OrderedDiagnosticCollection diagnostics)
    {
        ParseRestorePoint savepoint = SavePoint();
        aliasDefinition = null;

        ImmutableArray<AttributeUsage> attributes = ExpectAttributes();

        ImmutableArray<Token> modifiers = ExpectModifiers();

        if (!ExpectIdentifier(DeclarationKeywords.Alias, out Token? keyword))
        {
            diagnostics.Add(0, DiagnosticAt.Error($"Expected keyword `{DeclarationKeywords.Alias}` for alias definition", CurrentLocation, false));
            savepoint.Restore();
            return false;
        }

        keyword.AnalyzedType = TokenAnalyzedType.Keyword;

        CheckModifiers(modifiers, AliasModifiers);

        if (!ExpectIdentifier(out Token? identifier))
        {
            identifier = new MissingToken(TokenType.Identifier, keyword.Position.After());
            Diagnostics.Add(DiagnosticAt.Error($"Expected identifier after keyword `{keyword}`", identifier, File, false));
        }

        identifier.AnalyzedType = TokenAnalyzedType.TypeAlias;

        if (!ExpectType(AllowedType.Any | AllowedType.FunctionPointer | AllowedType.StackArrayWithoutLength, out TypeInstance? type))
        {
            type = new MissingTypeInstance(identifier.Position.After(), File);
            Diagnostics.Add(DiagnosticAt.Error($"Expected type after alias identifier", type, false));
        }

        aliasDefinition = new AliasDefinition(
            attributes,
            modifiers,
            keyword,
            identifier,
            type,
            File
        );

        if (!ExpectOperator(";"))
        {
            Diagnostics.Add(DiagnosticAt.Warning($"You forgot the semicolon", aliasDefinition.Position.After(), File));
        }

        return true;
    }

    bool ExpectEnumDefinition([NotNullWhen(true)] out EnumDefinition? enumDefinition, OrderedDiagnosticCollection diagnostics)
    {
        ParseRestorePoint savepoint = SavePoint();
        enumDefinition = null;

        ImmutableArray<AttributeUsage> attributes = ExpectAttributes();

        ImmutableArray<Token> modifiers = ExpectModifiers();

        if (!ExpectIdentifier(DeclarationKeywords.Enum, out Token? keyword))
        {
            diagnostics.Add(0, DiagnosticAt.Error($"Expected keyword `{DeclarationKeywords.Enum}` for enum definition", CurrentLocation, false));
            savepoint.Restore();
            return false;
        }

        keyword.AnalyzedType = TokenAnalyzedType.Keyword;

        CheckModifiers(modifiers, EnumModifiers);

        if (!ExpectIdentifier(out Token? identifier))
        {
            identifier = new MissingToken(TokenType.Identifier, keyword.Position.After());
            Diagnostics.Add(DiagnosticAt.Error($"Expected identifier after enum type", identifier, File, false));
        }

        identifier.AnalyzedType = TokenAnalyzedType.Enum;

        TypeInstance? type = null;

        if (ExpectOperator(":", out Token? inheritOperator))
        {
            if (!ExpectType(AllowedType.FunctionPointer | AllowedType.StackArrayWithoutLength, out type))
            {
                type = new MissingTypeInstance(inheritOperator.Position.After(), File);
                Diagnostics.Add(DiagnosticAt.Error($"Expected type after ':'", type, File, false));
            }
        }

        if (!ExpectOperator("{", out Token? bracketStart))
        {
            Diagnostics.Add(DiagnosticAt.Error($"Expected '{{' after enum identifier", identifier.Position.After(), File, false));
            return false;
        }

        ImmutableArray<EnumMemberDefinition>.Builder members = ImmutableArray.CreateBuilder<EnumMemberDefinition>();
        Token? bracketEnd;
        Position lastPosition = bracketStart.Position;
        Token? previousComma = null;
        while (true)
        {
            if (ExpectOperator("}", out bracketEnd))
            {
                break;
            }

            if (previousComma is MissingToken)
            {
                Diagnostics.Add(DiagnosticAt.Error($"Expected ',' after this enum member", previousComma, File, false));
            }

            if (!ExpectIdentifier(out Token? memberIdentifier))
            {
                memberIdentifier = new MissingToken(TokenType.Identifier, lastPosition.After());
                Diagnostics.Add(DiagnosticAt.Error($"Expected identifier for enum member", memberIdentifier, File, false));
            }
            lastPosition = memberIdentifier.Position;
            memberIdentifier.AnalyzedType = TokenAnalyzedType.EnumMember;

            Expression? memberValue;

            if (!ExpectOperator("=", out Token? equalsToken))
            {
                memberValue = null;
            }
            else
            {
                lastPosition = equalsToken.Position;

                if (!ExpectAnyExpression(out memberValue))
                {
                    memberValue = new MissingExpression(lastPosition.After(), File);
                    Diagnostics.Add(DiagnosticAt.Error($"Expected enum member value after '='", memberValue, false));
                }
                lastPosition = memberValue.Position;
            }

            members.Add(new EnumMemberDefinition(memberIdentifier, memberValue, File));

            if (!ExpectOperator(",", out previousComma))
            {
                previousComma = new MissingToken(TokenType.Operator, lastPosition.After(), ",");
            }
        }

        enumDefinition = new EnumDefinition(
            attributes,
            modifiers,
            keyword,
            type,
            identifier,
            members.ToImmutable(),
            File,
            new TokenPair(bracketStart, bracketEnd)
        );

        if (ExpectOperator(";", out Token? semicolon))
        {
            Diagnostics.Add(DiagnosticAt.Warning($"Unnecessary semicolon", semicolon, File));
        }

        return true;
    }

    bool ExpectTemplateInfo([NotNullWhen(true)] out TemplateInfo? templateInfo)
    {
        if (!ExpectOperator("<", out Token? startBracket))
        {
            templateInfo = null;
            return false;
        }

        if (ExpectOperator(">", out Token? endBracket))
        {
            templateInfo = new TemplateInfo(new TokenPair(startBracket, endBracket), ImmutableArray<Token>.Empty);
            Diagnostics.Add(DiagnosticAt.Warning($"Empty template", templateInfo, File));
            return true;
        }

        List<Token> parameters = new();
        Position lastPosition = startBracket.Position;

        while (true)
        {
            if (!ExpectIdentifier(out Token? parameter))
            {
                Diagnostics.Add(DiagnosticAt.Error("Expected identifier or `>`", lastPosition.After(), File, false));
                parameter = new MissingToken(TokenType.Identifier, lastPosition.After());
            }

            parameter.AnalyzedType = TokenAnalyzedType.TypeParameter;
            parameters.Add(parameter);

            if (ExpectOperator(">", out endBracket))
            { break; }

            if (!ExpectOperator(",", out Token? comma))
            {
                Diagnostics.Add(DiagnosticAt.Error("Expected `,` or `>`", parameter.Position.After(), File));

                if (CurrentToken?.TokenType == TokenType.Identifier)
                {
                    comma = new MissingToken(TokenType.Operator, parameter.Position.After(), ",");
                }
                else
                {
                    endBracket = new MissingToken(TokenType.Operator, parameter.Position.After(), ">");
                    break;
                }
            }

            lastPosition = comma.Position;
        }

        templateInfo = new TemplateInfo(new TokenPair(startBracket, endBracket), parameters.ToImmutableArray());
        return true;
    }

    bool ExpectFunctionDefinition([NotNullWhen(true)] out FunctionDefinition? function, OrderedDiagnosticCollection diagnostics)
    {
        ParseRestorePoint savepoint = SavePoint();
        function = null;

        ImmutableArray<AttributeUsage> attributes = ExpectAttributes();

        ImmutableArray<Token> modifiers = ExpectModifiers();

        if (!ExpectType(AllowedType.None, out TypeInstance? possibleType, out DiagnosticAt? typeError))
        {
            diagnostics.Add(0, DiagnosticAt.Error($"Expected type for function definition", CurrentLocation, false).WithSuberrors(typeError));
            savepoint.Restore();
            return false;
        }

        if (!ExpectIdentifier(out Token? possibleNameT))
        {
            diagnostics.Add(1, DiagnosticAt.Error($"Expected identifier for function definition", possibleType.Position.After(), File, false));
            savepoint.Restore();
            return false;
        }

        ExpectTemplateInfo(out TemplateInfo? templateInfo);

        OrderedDiagnosticCollection parameterDiagnostics = new();
        if (!ExpectParameters(ParameterModifiers, true, out ParameterDefinitionCollection? parameters, parameterDiagnostics))
        {
            diagnostics.Add(2, DiagnosticAt.Error($"Expected parameter list for function definition", CurrentLocation, false), parameterDiagnostics.ToImmutableArray());
            savepoint.Restore();
            return false;
        }

        Block? block = null;

        if (!ExpectOperator(";") && !ExpectBlock(out block))
        {
            diagnostics.Add(3, DiagnosticAt.Error($"Expected `;` or block", parameters.Brackets.End.Position.After(), File, false));
            savepoint.Restore();
            return false;
        }

        possibleNameT.AnalyzedType = TokenAnalyzedType.FunctionName;

        CheckModifiers(modifiers, FunctionModifiers);

        function = new FunctionDefinition(
            attributes,
            modifiers,
            possibleType,
            possibleNameT,
            parameters,
            templateInfo,
            block,
            File
        );
        return true;
    }

    bool ExpectGeneralFunctionDefinition([NotNullWhen(true)] out GeneralFunctionDefinition? function, OrderedDiagnosticCollection diagnostic)
    {
        ParseRestorePoint savepoint = SavePoint();
        function = null;

        ImmutableArray<Token> modifiers = ExpectModifiers();

        if (!ExpectIdentifier(out Token? possibleNameT))
        {
            diagnostic.Add(0, DiagnosticAt.Error($"Expected identifier for general function definition", CurrentLocation, false));
            savepoint.Restore();
            return false;
        }

        if (possibleNameT.Content is
            not BuiltinFunctionIdentifiers.IndexerGet and
            not BuiltinFunctionIdentifiers.IndexerSet and
            not BuiltinFunctionIdentifiers.Destructor)
        {
            diagnostic.Add(0, DiagnosticAt.Error($"Invalid identifier `{possibleNameT.Content}` for general function definition", possibleNameT, File, false));
            savepoint.Restore();
            return false;
        }

        OrderedDiagnosticCollection parameterDiagnostics = new();
        if (!ExpectParameters(ImmutableArray.Create(ModifierKeywords.Temp), false, out ParameterDefinitionCollection? parameters, parameterDiagnostics))
        {
            diagnostic.Add(1, DiagnosticAt.Error($"Expected parameter list for general function definition", possibleNameT.Position.After(), File, false), parameterDiagnostics.ToImmutableArray());
            savepoint.Restore();
            return false;
        }

        if (!ExpectBlock(out Block? block))
        {
            diagnostic.Add(2, DiagnosticAt.Error($"Body is required for general function definition", CurrentPosition, File, false));
            savepoint.Restore();
            return false;
        }

        possibleNameT.AnalyzedType = TokenAnalyzedType.FunctionName;

        CheckModifiers(modifiers, GeneralFunctionModifiers);

        function = new GeneralFunctionDefinition(
            possibleNameT,
            modifiers,
            parameters,
            block,
            File
        );
        return true;
    }

    bool ExpectConstructorDefinition([NotNullWhen(true)] out ConstructorDefinition? function, OrderedDiagnosticCollection diagnostic)
    {
        ParseRestorePoint savepoint = SavePoint();
        function = null;

        ImmutableArray<Token> modifiers = ExpectModifiers();

        if (!ExpectType(AllowedType.None, out TypeInstance? type))
        {
            diagnostic.Add(0, DiagnosticAt.Error($"Expected a type for constructor definition", CurrentPosition, File, false));
            savepoint.Restore();
            return false;
        }

        OrderedDiagnosticCollection parameterDiagnostics = new();
        if (!ExpectParameters(ImmutableArray.Create(ModifierKeywords.Temp), true, out ParameterDefinitionCollection? parameters, parameterDiagnostics))
        {
            diagnostic.Add(0, DiagnosticAt.Error($"Expected a parameter list for constructor definition", CurrentPosition, File, false), parameterDiagnostics.ToImmutableArray());
            savepoint.Restore();
            return false;
        }

        CheckModifiers(modifiers, ConstructorModifiers);

        if (!ExpectBlock(out Block? block))
        {
            diagnostic.Add(0, DiagnosticAt.Error($"Body is required for constructor definition", CurrentPosition, File, false));
            savepoint.Restore();
            return false;
        }

        function = new ConstructorDefinition(
            type,
            modifiers,
            parameters,
            block,
            File
        );
        return true;
    }

    bool ExpectStructDefinition(OrderedDiagnosticCollection diagnostic)
    {
        ParseRestorePoint savepoint = SavePoint();

        ImmutableArray<AttributeUsage> attributes = ExpectAttributes();

        ImmutableArray<Token> modifiers = ExpectModifiers();

        if (!ExpectIdentifier(DeclarationKeywords.Struct, out Token? keyword))
        {
            diagnostic.Add(0, DiagnosticAt.Error($"Expected keyword `{DeclarationKeywords.Struct}` for struct definition", CurrentLocation, false));
            savepoint.Restore();
            return false;
        }

        keyword.AnalyzedType = TokenAnalyzedType.Keyword;

        CheckModifiers(modifiers, StructModifiers);

        if (!ExpectIdentifier(out Token? possibleStructName))
        {
            possibleStructName = new MissingToken(TokenType.Identifier, keyword.Position.After());
            Diagnostics.Add(DiagnosticAt.Error($"Expected identifier after keyword `{keyword}`", possibleStructName, File, false));
        }

        possibleStructName.AnalyzedType = TokenAnalyzedType.Struct;

        ExpectTemplateInfo(out TemplateInfo? templateInfo);

        if (!ExpectOperator("{", out Token? bracketStart))
        {
            bracketStart = new MissingToken(TokenType.Operator, possibleStructName.Position.After(), "{");
            Diagnostics.Add(DiagnosticAt.Error($"Expected `{{` after struct identifier `{keyword}`", bracketStart, File, false));
        }

        List<FieldDefinition> fields = new();
        List<FunctionDefinition> methods = new();
        List<FunctionDefinition> operators = new();
        List<GeneralFunctionDefinition> generalMethods = new();
        List<ConstructorDefinition> constructors = new();

        Token? bracketEnd;
        EndlessCheck endlessSafe = new();

        while (!ExpectOperator("}", out bracketEnd))
        {
            OrderedDiagnosticCollection diagnostics = new();
            if (ExpectField(out FieldDefinition? field, diagnostics))
            {
                fields.Add(field);
                if (ExpectOperator(";", out Token? semicolon))
                {
                    field.Semicolon = semicolon;
                }
                else
                {
                    Diagnostics.Add(DiagnosticAt.Warning($"You forgot the `;`", field.Position.After(), File));
                }
            }
            else if (ExpectFunctionDefinition(out FunctionDefinition? methodDefinition, diagnostics))
            {
                methods.Add(methodDefinition);
            }
            else if (ExpectGeneralFunctionDefinition(out GeneralFunctionDefinition? generalMethodDefinition, diagnostics))
            {
                generalMethods.Add(generalMethodDefinition);
            }
            else if (ExpectConstructorDefinition(out ConstructorDefinition? constructorDefinition, diagnostics))
            {
                constructors.Add(constructorDefinition);
            }
            else if (ExpectOperatorDefinition(out FunctionDefinition? operatorDefinition, diagnostics))
            {
                operators.Add(operatorDefinition);
            }
            else
            {
                Diagnostics.Add(DiagnosticAt.Error($"Unexpected {(CurrentToken is null ? "end of file" : $"token `{CurrentToken}`")}", CurrentToken?.Position ?? PreviousToken!.Position.After(), File, false).WithSuberrors(diagnostics.Compile()));
                bracketEnd = new MissingToken(TokenType.Operator, PreviousToken!.Position.After(), "}");
                break;
            }

            endlessSafe.Step();
        }

        StructDefinition structDefinition = new(
            possibleStructName,
            bracketStart,
            bracketEnd,
            attributes,
            modifiers,
            templateInfo,
            fields.ToImmutableArray(),
            methods.ToImmutableArray(),
            generalMethods.ToImmutableArray(),
            operators.ToImmutableArray(),
            constructors.ToImmutableArray(),
            File
        );

        Structs.Add(structDefinition.Identifier.Content, structDefinition);

        return true;
    }

    bool ExpectParameters(ImmutableArray<string> allowedParameterModifiers, bool allowDefaultValues, [NotNullWhen(true)] out ParameterDefinitionCollection? parameterDefinitions, OrderedDiagnosticCollection diagnostic)
    {
        ParseRestorePoint savepoint = SavePoint();
        parameterDefinitions = null;

        if (!ExpectOperator("(", out Token? bracketStart))
        {
            diagnostic.Add(0, DiagnosticAt.Error("Expected a `(` for parameter list", CurrentLocation, false));
            savepoint.Restore();
            return false;
        }

        if (ExpectOperator(")", out Token? bracketEnd))
        {
            parameterDefinitions = new ParameterDefinitionCollection(ImmutableArray<ParameterDefinition>.Empty, new TokenPair(bracketStart, bracketEnd), File);
            return true;
        }

        List<ParameterDefinition> parameters = new();
        Position lastPosition = bracketStart.Position;
        bool expectOptionalParameters = false;

        while (true)
        {
            ImmutableArray<Token> parameterModifiers = ExpectModifiers();

            foreach (Token modifier in parameterModifiers)
            {
                if (!allowedParameterModifiers.Contains(modifier.Content))
                { Diagnostics.Add(DiagnosticAt.Error($"Modifier `{modifier}` not valid in the current context", modifier, File, false)); }
                else if (modifier.Content == ModifierKeywords.This && parameters.Count > 0)
                { Diagnostics.Add(DiagnosticAt.Error($"Modifier `{modifier}` only valid on the first parameter", modifier, File, false)); }
            }

            if (!ExpectType(AllowedType.FunctionPointer, out TypeInstance? parameterType))
            {
                parameterType = new MissingTypeInstance(lastPosition.After(), File);
                diagnostic.Add(1, DiagnosticAt.Error("Expected parameter type", parameterType, false));
                savepoint.Restore();
                return false;
            }

            if (!ExpectIdentifier(out Token? parameterIdentifier))
            {
                parameterIdentifier = new MissingToken(TokenType.Identifier, parameterType.Position.After());
                diagnostic.Add(1, DiagnosticAt.Error("Expected a parameter name", parameterIdentifier, File, false));
                savepoint.Restore();
                return false;
            }

            parameterIdentifier.AnalyzedType = TokenAnalyzedType.ParameterName;

            Expression? defaultValue = null;
            if (ExpectOperator("=", out Token? assignmentOperator))
            {
                if (!ExpectAnyExpression(out defaultValue))
                {
                    defaultValue = new MissingExpression(assignmentOperator.Position.After(), File);
                    Diagnostics.Add(DiagnosticAt.Error("Expected expression", defaultValue, false));
                }

                if (!allowDefaultValues)
                {
                    Diagnostics.Add(DiagnosticAt.Error("Default parameter values are not valid in the current context", defaultValue, false));
                }

                expectOptionalParameters = true;
            }
            else if (expectOptionalParameters)
            {
                Diagnostics.Add(DiagnosticAt.Error("Parameters without default value after a parameter that has one is not supported", parameterIdentifier.Position.After(), File, false));
            }

            ParameterDefinition parameter = new(parameterModifiers, parameterType, parameterIdentifier, defaultValue, File);
            parameters.Add(parameter);

            if (ExpectOperator(")", out bracketEnd))
            { break; }

            if (!ExpectOperator(","))
            {
                diagnostic.Add(2, DiagnosticAt.Error("Expected `,` or `)`", parameter.Position.After(), File, false));
                savepoint.Restore();
                return false;
            }

            lastPosition = parameter.Position;
        }

        parameterDefinitions = new ParameterDefinitionCollection(parameters.ToImmutableArray(), new TokenPair(bracketStart, bracketEnd), File);
        return true;
    }

    bool ExpectAttribute([NotNullWhen(true)] out AttributeUsage? attribute)
    {
        ParseRestorePoint savepoint = SavePoint();
        attribute = null;

        if (!ExpectOperator("[", out Token? bracketStart))
        {
            savepoint.Restore();
            return false;
        }

        if (!ExpectIdentifier(out Token? attributeT))
        {
            savepoint.Restore();
            return false;
        }

        attributeT.AnalyzedType = TokenAnalyzedType.Attribute;

        List<LiteralExpression> parameters = new();
        Position lastPosition = attributeT.Position;
        if (ExpectOperator("(", out Token? bracketParametersStart))
        {
            lastPosition = bracketParametersStart.Position;

            if (!ExpectOperator(")", out Token? bracketParametersEnd))
            {
                EndlessCheck endlessSafe = new();

                while (true)
                {
                    if (!ExpectLiteral(out LiteralExpression? argument))
                    {
                        argument = new MissingLiteral(lastPosition.After(), File);
                        Diagnostics.Add(DiagnosticAt.Error($"Expected literal as an argument", argument, false));
                    }

                    parameters.Add(argument);

                    if (ExpectOperator(")", out bracketParametersEnd))
                    { break; }

                    if (!ExpectOperator(",", out Token? comma))
                    {
                        Diagnostics.Add(DiagnosticAt.Error($"Expected `,` or `)`", argument.Location.After()));

                        int v = CurrentTokenIndex;
                        if (ExpectLiteral(out _))
                        {
                            CurrentTokenIndex = v;
                            comma = new MissingToken(TokenType.Operator, argument.Position.After(), ",");
                        }
                        else
                        {
                            bracketParametersEnd = new MissingToken(TokenType.Operator, argument.Position.After(), ")");
                            break;
                        }
                    }

                    lastPosition = comma.Position;

                    endlessSafe.Step();
                }
            }

            lastPosition = bracketParametersEnd.Position;
        }

        if (!ExpectOperator("]", out Token? bracketEnd))
        {
            bracketEnd = new MissingToken(TokenType.Operator, lastPosition.After(), "]");
            Diagnostics.Add(DiagnosticAt.Error("Expected `]`", bracketEnd, File));
        }

        attribute = new AttributeUsage(attributeT, parameters.ToImmutableArray(), new TokenPair(bracketStart, bracketEnd), File);
        return true;
    }
    ImmutableArray<AttributeUsage> ExpectAttributes()
    {
        ImmutableArray<AttributeUsage>.Builder? attributes = null;
        while (ExpectAttribute(out AttributeUsage? attr))
        {
            attributes ??= ImmutableArray.CreateBuilder<AttributeUsage>();
            attributes.Add(attr);
        }
        return attributes?.DrainToImmutable() ?? ImmutableArray<AttributeUsage>.Empty;
    }

    bool ExpectField([NotNullWhen(true)] out FieldDefinition? field, OrderedDiagnosticCollection diagnostic)
    {
        ParseRestorePoint savepoint = SavePoint();
        field = null;

        ImmutableArray<AttributeUsage> attributes = ExpectAttributes();

        ImmutableArray<Token> modifiers = ExpectModifiers();

        if (!ExpectType(AllowedType.FunctionPointer, out TypeInstance? possibleType, out DiagnosticAt? typeError))
        {
            diagnostic.Add(0, DiagnosticAt.Error($"Expected type for field definition", CurrentLocation, false).WithSuberrors(typeError));
            savepoint.Restore();
            return false;
        }

        if (!ExpectIdentifier(out Token? fieldName))
        {
            diagnostic.Add(1, DiagnosticAt.Error($"Expected identifier for field definition", possibleType.Location.After(), false));
            savepoint.Restore();
            return false;
        }

        if (ExpectOperator("(", out _))
        {
            savepoint.Restore();
            return false;
        }

        fieldName.AnalyzedType = TokenAnalyzedType.FieldName;

        CheckModifiers(modifiers, FieldModifiers);

        field = new(fieldName, possibleType, modifiers, attributes);
        return true;
    }
}
