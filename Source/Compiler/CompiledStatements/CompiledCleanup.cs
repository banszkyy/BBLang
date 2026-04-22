namespace LanguageCore.Compiler;

public class CompiledCleanup : CompiledStatement
{
    public TemplateInstance<CompiledGeneralFunctionDefinition>? Destructor { get; init; }
    public TemplateInstance<CompiledFunctionDefinition>? Deallocator { get; init; }
    public required GeneralType TrashType { get; init; }

    public override string ToString() => "::cleanup::";
}
