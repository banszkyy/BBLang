namespace LanguageCore.Compiler;

public class TemplateInstance<T> where T : notnull
{
    public readonly T Template;
    public readonly ImmutableDictionary<string, GeneralType>? TypeArguments;

    public TemplateInstance(T template, ImmutableDictionary<string, GeneralType>? typeArguments)
    {
        Template = template;
        TypeArguments = typeArguments;
    }

    public override string ToString() => Template?.ToString() ?? "null";

    [SuppressMessage("Quality", "MY003")]
    public TemplateInstance<V> UnsafeTo<V>() where V : notnull => new((V)(object)Template, TypeArguments);
}

public static class TemplateInstance
{
    [return: NotNullIfNotNull(nameof(template))]
    public static TemplateInstance<T>? New<T>(T? template, ImmutableDictionary<string, GeneralType>? typeArguments)
        where T : notnull
        => template is null ? null : new TemplateInstance<T>(template, typeArguments);

    [return: NotNullIfNotNull(nameof(template))]
    public static TemplateInstance<T>? New<T>(StatementCompiler.FunctionQueryResult<T>? template)
        where T : notnull
        => template is null ? null : new TemplateInstance<T>(template.Function, template.TypeArguments);
}
