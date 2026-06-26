using System.Collections.Immutable;
using System.Text;
using LanguageCore.Runtime;

namespace LanguageCore.Tests;

readonly struct ExpectedResult
{
    public readonly string StdOutput;
    public readonly int ExitCode;
    public readonly ImmutableArray<InterpreterRunner.ExposedFunctionTest> ExposedFunctionTests;

    enum ExpectedResultParserState
    {
        Normal,
        Escape,
        Tag,
        TagEnd,
    }

    public ExpectedResult(string resultFile)
    {
        string resultText = File.ReadAllText(resultFile);
        List<InterpreterRunner.ExposedFunctionTest> exposedFunctionTests = [];

        StringBuilder builder = new(resultFile.Length);
        StringBuilder? tagBuilder = null;
        List<string> tags = new();

        ExpectedResultParserState state = ExpectedResultParserState.Normal;

        for (int i = 0; i < resultText.Length; i++)
        {
            char c = resultText[i];
            switch (state)
            {
                case ExpectedResultParserState.Normal:
                    switch (c)
                    {
                        case '\\':
                            state = ExpectedResultParserState.Escape;
                            break;
                        case '#':
                            state = ExpectedResultParserState.Tag;
                            break;
                        default:
                            builder.Append(c);
                            break;
                    }
                    break;
                case ExpectedResultParserState.Escape:
                    builder.Append(c);
                    state = ExpectedResultParserState.Normal;
                    break;
                case ExpectedResultParserState.Tag:
                    switch (c)
                    {
                        case '\r':
                        case '\n':
                            state = ExpectedResultParserState.TagEnd;
                            break;
                        default:
                            tagBuilder ??= new StringBuilder();
                            tagBuilder.Append(c);
                            break;
                    }
                    break;
                case ExpectedResultParserState.TagEnd:
                    switch (c)
                    {
                        case '\r':
                        case '\n':
                            break;
                        default:
                            if (tagBuilder != null)
                            {
                                tags.Add(tagBuilder.ToString());
                                tagBuilder = null;
                            }
                            state = ExpectedResultParserState.Normal;
                            i--;
                            break;
                    }
                    break;
                default:
                    break;
            }
        }

        if (tagBuilder != null)
        { tags.Add(tagBuilder.ToString()); }

        StdOutput = builder.ToString();
        ExitCode = 0;

        for (int i = 0; i < tags.Count; i++)
        {
            string tag = tags[i].Trim().ToLowerInvariant();
            ReadOnlySpan<string> parts = tag.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            string identifier = parts[0];
            ReadOnlySpan<string> arguments = parts.Length > 1 ? parts[1..] : [];

            switch (identifier)
            {
                case "exitcode":
                    if (arguments.Length != 1) throw new FormatException($"Invalid result syntax in {resultFile}");
                    if (!int.TryParse(arguments[0], System.Globalization.CultureInfo.InvariantCulture, out ExitCode)) throw new FormatException($"Invalid result syntax in {resultFile}");
                    break;
                case "exposed":
                    if (arguments.Length == 0) throw new FormatException($"Invalid result syntax in {resultFile}");
                    string name = arguments[0];
                    byte[] passingArguments = Array.Empty<byte>();
                    byte[] expectedReturn = Array.Empty<byte>();

                    List<byte> args = [];
                    List<byte> ret = [];

                    for (int j = 1; j < arguments.Length; j++)
                    {
                        if (arguments[j] == "=>")
                        {
                            passingArguments = args.ToArray();
                            args.Clear();
                            ret = args;
                            continue;
                        }

                        int k = arguments[j].IndexOf(':');
                        if (k == -1) throw new FormatException($"Invalid result syntax in {resultFile}");
                        string type = arguments[j][..k];
                        string value = arguments[j][(k + 1)..];
                        switch (type)
                        {
                            case "i8":
                                args.Add((byte)int.Parse(value));
                                break;
                            case "i16":
                                args.AddRange(((short)int.Parse(value)).ToBytes());
                                break;
                            case "i32":
                                args.AddRange(int.Parse(value).ToBytes());
                                break;
                            case "f32":
                                args.AddRange(float.Parse(value).ToBytes());
                                break;
                            default:
                                throw new FormatException($"Invalid result syntax in {resultFile}");
                        }
                    }

                    expectedReturn = ret.ToArray();

                    exposedFunctionTests.Add(new InterpreterRunner.ExposedFunctionTest(name, passingArguments, v =>
                    {
                        if (!v.SequenceEqual(expectedReturn)) throw new AssertFailedException($"Exposed function \"{name}\" returned invalid value");
                    }));
                    break;
                default:
                    throw new FormatException($"Invalid result syntax in {resultFile}");
            }
        }

        ExposedFunctionTests = exposedFunctionTests.ToImmutableArray();
    }

    public ExpectedResult Assert(IResult other)
    {
        if (!string.Equals(StdOutput.Replace("\r", ""), other.StdOutput.Replace("\r", ""), StringComparison.Ordinal))
        { throw new AssertFailedException($"Standard output isn't what is expected:{Environment.NewLine}Expected: \"{StdOutput.Replace("\r", "").Escape()}\"{Environment.NewLine}Actual:   \"{other.StdOutput.Replace("\r", "").Escape()}\""); }

        if (ExitCode != other.ExitCode)
        { throw new AssertFailedException($"Exit code isn't what is expected:{Environment.NewLine}Expected: {ExitCode}{Environment.NewLine}Actual:   {other.ExitCode}"); }

        return this;
    }

    public ExpectedResult Assert(BrainfuckRunner.BrainfuckResult other)
    {
        if (!string.Equals(StdOutput.Replace("\r", ""), other.StdOutput.Replace("\r", ""), StringComparison.Ordinal))
        { throw new AssertFailedException($"Standard output isn't what is expected:{Environment.NewLine}Expected: \"{StdOutput.Replace("\r", "").Escape()}\"{Environment.NewLine}Actual:   \"{other.StdOutput.Replace("\r", "").Escape()}\""); }

        if (unchecked((byte)ExitCode) != other.ExitCode)
        { throw new AssertFailedException($"Exit code isn't what is expected:{Environment.NewLine}Expected: {unchecked((byte)ExitCode)}{Environment.NewLine}Actual:   {other.ExitCode}"); }

        return this;
    }

    public ExpectedResult Assert(InterpreterRunner.MainResult other, bool heapShouldBeEmpty)
    {
        Assert(other);

        if (heapShouldBeEmpty && BytecodeHeapImplementation.GetUsedSize(other.Heap.AsSpan()) != 0)
        { throw new AssertFailedException($"Heap isn't empty"); }

        return this;
    }

    public ExpectedResult Assert(BrainfuckRunner.BrainfuckResult other, bool memoryShouldBeEmpty, int? expectedMemoryPointer)
    {
        Assert(other);

        if (memoryShouldBeEmpty)
        {
            // Span<byte> expectedMemory = Utils.GenerateBrainfuckMemory(other.Memory.Length).AsSpan()[1..];
            // Span<byte> actualMemory = other.Memory.AsSpan()[1..];
            //
            // if (!MemoryExtensions.SequenceEqual(expectedMemory, actualMemory))
            // { throw new AssertFailedException($"Memory isn't empty"); }
        }

        if (expectedMemoryPointer.HasValue && other.MemoryPointer != expectedMemoryPointer.Value)
        { throw new AssertFailedException($"Memory pointer isn't what is expected:{Environment.NewLine}Expected: \"{expectedMemoryPointer.Value}\"{Environment.NewLine}Actual: \"{other.MemoryPointer}\""); }

        return this;
    }

    public ExpectedResult Assert(NativeRunner.AssemblyResult other)
    {
        Assert((IResult)other);

        return this;
    }
}
