using LanguageCore.Parser;

namespace LanguageCore.Compiler;

public class CompiledStruct :
    IReferenceable<TypeInstance>,
    IIdentifiable<string>,
    IInFile,
    ILocated,
    IHaveAttributes
{
    public StructDefinition Definition { get; }
    public ImmutableArray<CompiledField> Fields { get; private set; }
    public List<Reference<TypeInstance>> References { get; }

    public string Identifier => Definition.Identifier.Content;
    public Uri File => Definition.File;
    public Location Location => Definition.Location;
    CanUseOn IHaveAttributes.AttributeUsageKind => (Definition as IHaveAttributes).AttributeUsageKind;
    public ImmutableArray<AttributeUsage> Attributes => Definition.Attributes;

    public CompiledStruct(ImmutableArray<CompiledField> fields, StructDefinition definition)
    {
        Definition = definition;
        Fields = fields;
        foreach (CompiledField field in fields) field.Context = this;

        References = new List<Reference<TypeInstance>>();
    }

    public CompiledStruct(ImmutableArray<CompiledField> fields, CompiledStruct other)
    {
        Definition = other.Definition;
        Fields = fields;
        foreach (CompiledField field in fields) field.Context = this;

        References = new List<Reference<TypeInstance>>(other.References);
    }

    public void SetFields(ImmutableArray<CompiledField> fields)
    {
        Fields = fields;
        foreach (CompiledField field in fields) field.Context = this;
    }

    public override string ToString()
    {
        StringBuilder result = new();
        result.Append("struct ");

        result.Append(Identifier);

        if (Definition.Template is null)
        { return result.ToString(); }

        result.Append('<');
        result.AppendJoin(", ", Definition.Template.Parameters);
        result.Append('>');
        return result.ToString();
    }
}
