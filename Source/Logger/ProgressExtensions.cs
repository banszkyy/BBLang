namespace LanguageCore;

static class ProgressExtensions
{
    public static void Report(this IProgress<float> progress, int index, int count) => progress.Report((float)index / count);
}
