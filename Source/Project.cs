using System.Runtime.CompilerServices;

namespace LanguageCore;

public static class Project
{
    public static string Path => GetProjectPath();

    [SuppressMessage("Usage", "CA2201")]
    static string GetProjectPath([CallerFilePath] string? callerFilePath = null) => System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(callerFilePath)) ?? throw new Exception($"Failed to get the project path");
}
