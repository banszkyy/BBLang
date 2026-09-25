namespace LanguageCore.Runtime;

public static class ExternalFunctionExtensions
{
    public static string ToReadable(this IExternalFunction externalFunction) => $"<{externalFunction.ReturnValueSize}bytes> {(externalFunction.Name is not null ? $"\"{externalFunction.Name}\"" : externalFunction.Id.ToString())}(<{externalFunction.ParametersSize}bytes>)";
}
