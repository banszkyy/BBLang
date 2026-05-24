using CommandLine;
using CommandLine.Text;

namespace LanguageCore;

public static class Program
{
    public static void DisplayHelp<T>(ParserResult<T> result, IEnumerable<Error> errs)
    {
        HelpText? helpText = null;
        if (errs.IsVersion())
        {
            helpText = HelpText.AutoBuild(result);
        }
        else
        {
            helpText = HelpText.AutoBuild(result, h =>
            {
                h.AdditionalNewLineAfterOption = false;
                h.Heading = "BBLang";
                h.Copyright = string.Empty;
                return HelpText.DefaultParsingErrorsHandler(result, h);
            }, e => e);
        }
        Console.WriteLine(helpText);
    }

    public static int Main(string[] args)
    {
#if DEBUG
        return DevelopmentEntry.Start(args);
#else
        return Entry.Run(args);
#endif
    }
}
