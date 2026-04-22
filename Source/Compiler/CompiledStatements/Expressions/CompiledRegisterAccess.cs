using LanguageCore.Runtime;

namespace LanguageCore.Compiler;

public class CompiledRegisterAccess : CompiledAccessExpression
{
    public required Register Register { get; init; }

    public override string ToString() => $"{Register}";
}
