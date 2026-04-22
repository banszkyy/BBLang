using System.Reflection.Emit;

namespace LanguageCore.IL.Generator;

public struct ILGeneratorResult
{
    public DynamicMethod EntryPoint;
    public Func<int> EntryPointDelegate;
    public ImmutableArray<DynamicMethod> Methods;
    public ModuleBuilder Module;
}
