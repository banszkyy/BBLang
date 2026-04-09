using LanguageCore.Parser;
using LanguageCore.Parser.Statements;

namespace LanguageCore.Compiler;

public class CompiledEnumMember :
    IReferenceable<FieldExpression>,
    IIdentifiable<string>,
    IInFile,
    ILocated,
    ICompiledDefinition<EnumMemberDefinition>
{
    /// <summary>
    /// Set by the compiler
    /// </summary>
    public CompiledEnum Enum { get; internal set; }
    public EnumMemberDefinition Definition { get; }
    public CompiledExpression Value { get; }
    public List<Reference<FieldExpression>> References { get; }

    public string Identifier => Definition.Identifier.Content;
    public Uri File => Definition.File;
    public Location Location => new(Definition.Position, Definition.File);

    public CompiledEnumMember(CompiledExpression value, EnumMemberDefinition definition)
    {
        Enum = null!;
        Definition = definition;
        Value = value;
        References = new List<Reference<FieldExpression>>();
    }
}
