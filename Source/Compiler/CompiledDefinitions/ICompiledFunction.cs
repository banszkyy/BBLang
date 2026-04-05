using LanguageCore.Parser;

namespace LanguageCore.Compiler;

public interface ICompiledFunctionDefinition :
    IHaveCompiledType,
    IInFile,
    IHaveInstructionOffset,
    IReadable,
    IMsilCompatible,
    ILocated,
    ICompiledDefinition<FunctionThingDefinition>,
    IHaveAttributes
{
    bool ReturnSomething { get; }
    ImmutableArray<CompiledParameter> Parameters { get; }
}
