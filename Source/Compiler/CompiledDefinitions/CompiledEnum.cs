using LanguageCore.Parser;

namespace LanguageCore.Compiler;

public class CompiledEnum :
    IReferenceable<TypeInstance>,
    IIdentifiable<string>,
    IInFile,
    ILocated,
    ICompiledDefinition<EnumDefinition>
{
    public EnumDefinition Definition { get; }
    public GeneralType Type { get; }
    public ImmutableArray<CompiledEnumMember> Members { get; }
    public List<Reference<TypeInstance>> References { get; }

    public string Identifier => Definition.Identifier.Content;
    public Uri File => Definition.File;
    public Location Location => new(Definition.Position, Definition.File);

    public CompiledEnum(GeneralType type, ImmutableArray<CompiledEnumMember> members, EnumDefinition definition)
    {
        Definition = definition;
        Type = type;
        Members = members;
        References = new List<Reference<TypeInstance>>();

        for (int i = 0; i < Members.Length; i++)
        {
            Members[i].Enum = this;
        }
    }
}
