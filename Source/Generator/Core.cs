using LanguageCore.Parser;

namespace LanguageCore.Compiler;

public interface IHaveInstructionOffset
{

}

public interface IHaveCompiledType
{
    GeneralType Type { get; }
}

public interface IInContext<TContext>
{
    TContext Context { get; }
}

public enum Protection
{
    Private,
    Public,
    Exported,
}

public interface IIdentifiable<TIdentifier>
{
    TIdentifier Identifier { get; }
}

public interface ITemplateable
{
    TemplateInfo? Template { get; }
}

public interface IHaveAttributes
{
    CanUseOn AttributeUsageKind { get; }
    ImmutableArray<AttributeUsage> Attributes { get; }
}

public interface IMsilCompatible
{
    bool IsMsilCompatible { get; set; }
}

public interface IExternalFunctionDefinition
{
    string? ExternalFunctionName { get; }
}

public interface IExposeable
{
    string? ExposedFunctionName { get; }
}

public interface ICallableDefinition :
    IInFile
{

}
