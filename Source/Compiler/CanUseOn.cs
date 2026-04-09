using LanguageCore.Runtime;

namespace LanguageCore.Compiler;

[Flags]
public enum CanUseOn
{
    Function = 0x1,
    Struct = 0x2,
    Field = 0x4,
    TypeAlias = 0x8,
    Variable = 0x10,
    Enum = 0x20,

    TypeDefinition = Struct | TypeAlias | Enum,
}
