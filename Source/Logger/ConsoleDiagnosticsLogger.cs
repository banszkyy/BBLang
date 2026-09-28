using System.IO;

namespace LanguageCore;

[ExcludeFromCodeCoverage]
public class ConsoleDiagnosticsLogger : IDiagnosticsLogger
{
    public bool LogInfos { get; init; }
    public bool LogWarnings { get; init; }

    public static readonly ConsoleDiagnosticsLogger Default = new()
    {
        LogInfos = true,
        LogWarnings = true,
    };

    public void LogDiagnostic(DiagnosticAt diagnostic, IEnumerable<ISourceProvider>? sourceProviders = null)
        => LogDiagnostic(diagnostic, 0, sourceProviders);

    public void LogDiagnostic(Diagnostic diagnostic, IEnumerable<ISourceProvider>? sourceProviders = null)
        => LogDiagnostic(diagnostic, 0, sourceProviders);

    void LogDiagnostic(Diagnostic diagnostic, int depth, IEnumerable<ISourceProvider>? sourceProviders = null, Diagnostic? parent = null)
    {
        DiagnosticsLevel level = diagnostic.Level;

        if (parent is not null && parent.Level > level)
        {
            level = parent.Level;
        }

        if (!(level switch
        {
            DiagnosticsLevel.Error => true,
            DiagnosticsLevel.Warning => LogWarnings,
            DiagnosticsLevel.Information => LogInfos,
            DiagnosticsLevel.Hint => LogInfos,
            DiagnosticsLevel.OptimizationNotice => LogInfos,
            DiagnosticsLevel.FailedOptimization => LogInfos,
            _ => false,
        }))
        { return; }

        LogDiagnosticImplementation(diagnostic, depth, sourceProviders, parent);
    }

    public static void LogDiagnosticImplementation(Diagnostic diagnostic, int depth, IEnumerable<ISourceProvider>? sourceProviders = null, Diagnostic? parent = null)
    {
        DiagnosticsLevel level = diagnostic.Level;

        if (parent is not null && parent.Level > level)
        {
            level = parent.Level;
        }

        Console.Write(new string(' ', depth * 2));

        static void WriteLocation(Location l)
        {
            if (l.File.IsFile)
            {
                string v = Path.GetRelativePath(Environment.CurrentDirectory, l.File.LocalPath);
                if (v.Contains(".."))
                {
                    v = l.File.LocalPath;
                }
                else if (v.StartsWith($".{Path.DirectorySeparatorChar}"))
                {
                    v = v[2..];
                }
                string? d = Path.GetDirectoryName(v);
                if (d is not null)
                {
                    Console.Write(d);
                    Console.Write(Path.DirectorySeparatorChar);
                }
                if (!Console.IsOutputRedirected) Console.Write("\x1b[1m");
                Console.Write(Path.GetFileName(v));
                if (!Console.IsOutputRedirected) Console.Write("\x1b[0m");
                Console.ResetColor();
            }
            else
            {
                Console.Write(l.File);
            }
            Console.Write(':');
            if (!Console.IsOutputRedirected) Console.Write("\x1b[1m");
            Console.Write(l.Position.Range.Start.Line + 1);
            if (!Console.IsOutputRedirected) Console.Write("\x1b[0m");
            Console.Write(':');
            if (!Console.IsOutputRedirected) Console.Write("\x1b[1m");
            if (l.Position.Range.Start.Line == l.Position.Range.End.Line && l.Position.Range.Start.Character < l.Position.Range.End.Character)
            {
                Console.Write($"{l.Position.Range.Start.Character + 1}-{l.Position.Range.End.Character + 1}");
            }
            else
            {
                Console.Write($"{l.Position.Range.Start.Character + 1}");
            }
            if (!Console.IsOutputRedirected) Console.Write("\x1b[0m");
        }

        if (diagnostic is DiagnosticAt diagnosticAt)
        {
            WriteLocation(diagnosticAt.Location);
            Console.Write(": ");
        }

        if (!Console.IsOutputRedirected)
        {
            Console.ForegroundColor = level switch
            {
                DiagnosticsLevel.Error => ConsoleColor.Red,
                DiagnosticsLevel.Warning => ConsoleColor.Yellow,
                DiagnosticsLevel.Information => ConsoleColor.Blue,
                DiagnosticsLevel.Hint => ConsoleColor.Cyan,
                DiagnosticsLevel.OptimizationNotice => ConsoleColor.DarkGray,
                DiagnosticsLevel.FailedOptimization => ConsoleColor.DarkYellow,
                _ => throw new UnreachableException(),
            };
            Console.Write("\x1b[1m");
        }
        Console.Write(level switch
        {
            DiagnosticsLevel.Error => "ERROR",
            DiagnosticsLevel.Warning => "WARNING",
            DiagnosticsLevel.Information => "INFO",
            DiagnosticsLevel.Hint => "HINT",
            DiagnosticsLevel.OptimizationNotice => "OPTNOTE",
            DiagnosticsLevel.FailedOptimization => "OPTFAIL",
            _ => throw new UnreachableException(),
        });
        if (!Console.IsOutputRedirected)
        {
            Console.Write("\x1b[0m");
            Console.ResetColor();
        }
        Console.Write(": ");

        Console.WriteLine(diagnostic.Message.ToString());

        if (diagnostic is DiagnosticAt diagnosticAt1)
        {
            (string SourceCode, string Arrows)? arrows = diagnosticAt1.GetArrows(sourceProviders);
            if (arrows.HasValue)
            {
                Console.Write(new string(' ', depth * 2));
                Console.WriteLine(arrows.Value.SourceCode);
                Console.Write(new string(' ', depth * 2));
                Console.WriteLine(arrows.Value.Arrows);
            }
        }

        if (diagnostic.RelatedInformation.Length > 0)
        {
            Console.Write(new string(' ', depth * 2));
            Console.WriteLine("Related Info:");
        }

        foreach (DiagnosticRelatedInformation relatedInfo in diagnostic.RelatedInformation)
        {
            Console.Write(new string(' ', (depth + 1) * 2));
            if (relatedInfo is DiagnosticRelatedInformationAt relatedInfoAt)
            {
                WriteLocation(relatedInfoAt.Location);
                Console.Write(": ");
            }
            Console.WriteLine(relatedInfo.Message);
        }

        if (diagnostic.SubErrors.Length > 0)
        {
            Console.Write(new string(' ', depth * 2));
            Console.WriteLine("Caused by:");
        }

        foreach (Diagnostic subdiagnostic in diagnostic.SubErrors)
        { LogDiagnosticImplementation(subdiagnostic, depth + 1, sourceProviders, diagnostic); }
    }
}
