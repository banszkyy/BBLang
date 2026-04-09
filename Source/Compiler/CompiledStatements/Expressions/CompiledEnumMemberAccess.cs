namespace LanguageCore.Compiler;

public class CompiledEnumMemberAccess : CompiledExpression
{
    public required CompiledEnumMember EnumMember { get; init; }

    public override string Stringify(int depth = 0) => $"{EnumMember.Enum.Identifier}.{EnumMember.Identifier}";
    public override string ToString() => $"{EnumMember.Enum.Identifier}.{EnumMember.Identifier}";
}
