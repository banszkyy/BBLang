using LanguageCore.Compiler;
using System.Collections.Immutable;

namespace LanguageCore.Tests;

static class Utils
{
    public static CompilerSettings GetCompilerSettings(CompilerSettings settigns) => new(settigns)
    {
        AdditionalImports = ImmutableArray.Create(
            $"{Project.Path}/StandardLibrary/Primitives.bbc"
        ),
        SourceProviders = ImmutableArray.Create<ISourceProvider>(
            new FileSourceProvider()
            {
                ExtraDirectories = new string[]
                {
                    $"{Project.Path}/StandardLibrary/"
                },
            }
        ),
    };
}
