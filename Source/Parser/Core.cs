namespace LanguageCore.Parser;

public static class ExportableExtensions
{
    public static bool CanUse(this IExportable self, Uri? sourceFile)
    {
        if (self.IsExported) return true;
        if (sourceFile == null) return true;
        if (sourceFile == self.File) return true;
        return false;
    }
}

public interface IExportable : IInFile
{
    bool IsExported { get; }
}

public interface IHaveType
{
    TypeInstance Type { get; }
}

public interface IReferenceableTo<TReference> : IInFile, IReferenceableTo where TReference : class
{
    new TReference? Reference { get; internal set; }
    object? IReferenceableTo.Reference
    {
        get => Reference;
        set => Reference = (value as TReference) ?? throw new InvalidOperationException($"Cannot assign '{value?.GetType().ToString() ?? "null"}' to '{typeof(TReference)}'");
    }
}

public interface IReferenceableTo : IInFile
{
    object? Reference { get; internal set; }
}

public enum LiteralType
{
    Invalid,
    Integer,
    Float,
    String,
    Char,
}
