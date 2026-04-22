namespace LanguageCore.Compiler;

public class CompiledDesctructorCall : CompiledExpression
{
    public required TemplateInstance<CompiledGeneralFunctionDefinition> Function { get; init; }
    public required CompiledExpression Value { get; init; }

    public override string ToString() => $"{Function.Template.Definition.Identifier}({Value})";
}
