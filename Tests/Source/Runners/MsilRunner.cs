using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using LanguageCore.Compiler;
using LanguageCore.Runtime;

namespace LanguageCore.Tests;

static class MsilRunner
{
    class CollectibleAssemblyLoadContext : AssemblyLoadContext
    {
        public CollectibleAssemblyLoadContext() : base(isCollectible: true) { }

        protected override System.Reflection.Assembly? Load(AssemblyName assemblyName) => null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Run(string file, string input)
    {
        CollectibleAssemblyLoadContext context = new();
        int result = RunImpl(file, input);
        context.Unload();
        context = null!;
        GC.Collect();

        return result;
    }

    static int RunImpl(string file, string input)
    {
        DiagnosticsCollection diagnostics = new();

        CompilerResult compiled = StatementCompiler.CompileFile(file, new CompilerSettings(Utils.GetCompilerSettings(IL.Generator.CodeGeneratorForIL.DefaultCompilerSettings))
        {
            ExternalFunctions = BytecodeProcessor.GetExternalFunctions(new FixedIO(input)).ToImmutableArray(),
            PreprocessorVariables = PreprocessorVariables.IL,
        }, diagnostics);

        diagnostics.Throw();

        Func<int> generatedCode = IL.Generator.CodeGeneratorForIL.Generate(compiled, diagnostics, new()
        {
            AllowCrash = true,
            AllowHeap = true,
            AllowPointers = true,
            AllowUnsafePointers = true,
        }).EntryPointDelegate;

        diagnostics.Throw();

        return generatedCode.Invoke();
    }
}
