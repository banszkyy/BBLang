using System.IO;
using LanguageCore;
using LanguageCore.Runtime;

namespace LanguageCore.Workspaces;

public static class ConfigurationManager
{
    public static IReadOnlyList<(Uri Uri, string Content)> Search(Uri currentDocument)
    {
        Uri currentUri = currentDocument;
        List<(Uri Uri, string Content)> result = new();
        EndlessCheck endlessCheck = new(50);
        while (currentUri.LocalPath != "/")
        {
            if (endlessCheck.Step()) break;
            Uri uri = new(currentUri, $"./{Configuration.FileName}");
            if (File.Exists(uri.LocalPath))
            {
                result.Add((uri, File.ReadAllText(uri.LocalPath)));
            }
            currentUri = new Uri(currentUri, "..");
        }
        return result;
    }
}

public sealed class Configuration
{
    public const string FileName = "bbl.conf";

    public required ImmutableArray<string> ExtraDirectories { get; init; }
    public required ImmutableArray<string> AdditionalImports { get; init; }
    public required ImmutableArray<ExternalFunctionStub> ExternalFunctions { get; init; }
    public required ImmutableArray<ExternalConstant> ExternalConstants { get; init; }

    public static readonly Configuration Empty = new()
    {
        ExtraDirectories = ImmutableArray<string>.Empty,
        AdditionalImports = ImmutableArray<string>.Empty,
        ExternalFunctions = ImmutableArray<ExternalFunctionStub>.Empty,
        ExternalConstants = ImmutableArray<ExternalConstant>.Empty,
    };

    class Parser
    {
        public readonly DiagnosticsCollection diagnostics;

        public readonly List<string> extraDirectories = new();
        public readonly List<string> additionalImports = new();
        public readonly List<string> includes = new();
        public readonly List<ExternalFunctionStub> externalFunctions = new();
        public readonly List<ExternalConstant> externalConstants = new();
        public readonly HashSet<Uri> alreadyParsed = new();

        public Parser(DiagnosticsCollection diagnostics)
        {
            this.diagnostics = diagnostics;
        }

        [SuppressMessage("Style", "IDE0071", Justification = "Unity")]
        public void Parse(ReadOnlySpan<char> key, ReadOnlySpan<char> value, Location location)
        {
            if (key.Equals("searchin", StringComparison.InvariantCultureIgnoreCase))
            {
                extraDirectories.Add(value.ToString());
            }
            else if (key.Equals("import", StringComparison.InvariantCultureIgnoreCase))
            {
                additionalImports.Add(value.ToString());
            }
            else if (key.Equals("include", StringComparison.InvariantCultureIgnoreCase))
            {
                includes.Add(value.ToString());
            }
            else if (key.Equals("externalfunc", StringComparison.InvariantCultureIgnoreCase))
            {
                string? name = null;
                int returnValueSize = 0;
                int parametersSize = 0;
                int argIndex = -1;
                int i = 0;

                while (value[i] == ' ') i++;

                while (i < value.Length)
                {
                    int j = value[i..].IndexOf(' ') + i;
                    ReadOnlySpan<char> arg = value[..j].Trim();
                    argIndex++;

                    if (argIndex == 0)
                    {
                        name = arg.ToString();
                    }
                    else if (argIndex == 1)
                    {
                        if (int.TryParse(arg, out int v))
                        {
                            returnValueSize = v;
                        }
                        else
                        {
                            diagnostics.Add(DiagnosticAt.Error($"Invalid integer `{arg.ToString()}`", location));
                        }
                    }
                    else
                    {
                        if (int.TryParse(arg, out int v))
                        {
                            parametersSize += v;
                        }
                        else
                        {
                            diagnostics.Add(DiagnosticAt.Error($"Invalid integer `{arg.ToString()}`", location));
                        }
                    }

                    i = j;
                    while (value[i] == ' ') i++;
                }

                if (name is not null)
                {
                    if (!externalFunctions.Any(v => v.Name == name))
                    {
                        externalFunctions.Add(new ExternalFunctionStub(
                            externalFunctions.GenerateId(name),
                            name,
                            parametersSize,
                            returnValueSize
                        ));
                    }
                    else
                    {
                        diagnostics.Add(DiagnosticAt.Error($"[Configuration]: External function {name} already exists", location));
                    }
                }
                else
                {
                    diagnostics.Add(DiagnosticAt.Error($"[Configuration]: Invalid config", location));
                }
            }
            else
            {
                diagnostics.Add(DiagnosticAt.Error($"Invalid configuration key `{key.ToString()}`", location));
            }
        }

        public Configuration Compile()
        {
            return new Configuration()
            {
                AdditionalImports = additionalImports.ToImmutableArray(),
                ExtraDirectories = extraDirectories.ToImmutableArray(),
                ExternalFunctions = externalFunctions.ToImmutableArray(),
                ExternalConstants = externalConstants.ToImmutableArray(),
            };
        }
    }

    static void Parse(Uri uri, string content, Parser parser, DiagnosticsCollection diagnostics)
    {
        if (!parser.alreadyParsed.Add(uri)) return;

        string[] values = content.Split('\n');
        for (int line = 0; line < values.Length; line++)
        {
            ReadOnlySpan<char> decl = values[line];
            int i = decl.IndexOf('#');
            if (i != -1) decl = decl[..i];
            decl = decl.Trim();
            if (decl.IsEmpty) continue;

            Location location = new(new Position((new SinglePosition(line, 0), new SinglePosition(line, decl.Length - 1)), (-1, -1)), uri);

            i = decl.IndexOf('=');
            if (i == -1)
            {
                diagnostics.Add(DiagnosticAt.Error($"Invalid configuration", location));
                continue;
            }

            ReadOnlySpan<char> key = decl[..i].Trim();
            ReadOnlySpan<char> value = decl[(i + 1)..].Trim();

            parser.Parse(key, value, location);
        }

        string[] includes = parser.includes.ToArray();
        parser.includes.Clear();
        foreach (string include in includes)
        {
            Uri newUri = new(uri, include);
            if (newUri.Scheme == "file" && File.Exists(newUri.LocalPath))
            {
                Parse(newUri, File.ReadAllText(newUri.LocalPath), parser, diagnostics);
            }
        }
    }

    static void Parse(IEnumerable<(Uri Uri, string Content)> configurations, Parser parser, DiagnosticsCollection diagnostics)
    {
        foreach ((Uri uri, string content) in configurations)
        {
            Parse(uri, content, parser, diagnostics);
        }
    }

    public static Configuration Parse(IEnumerable<(Uri Uri, string Content)> configurations, DiagnosticsCollection diagnostics)
    {
        Parser parser = new(diagnostics);
        Parse(configurations, parser, diagnostics);
        return parser.Compile();
    }

    public static Configuration Parse(Uri uri, string content, DiagnosticsCollection diagnostics)
    {
        Parser parser = new(diagnostics);
        Parse(uri, content, parser, diagnostics);
        return parser.Compile();
    }
}

