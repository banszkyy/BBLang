using System.IO;

namespace LanguageCore.Runtime;

public sealed class FixedIO : IO
{
    readonly ImmutableArray<byte> Input;
    int InputPosition;
    public readonly AsciiBuilder Output;

    public FixedIO(string input, AsciiBuilder? output = null)
        : this(Encoding.UTF8.GetBytes(input).AsImmutableUnsafe(), output)
    { }

    public FixedIO(ImmutableArray<byte> input, AsciiBuilder? output = null)
    {
        Input = input;
        InputPosition = 0;
        Output = output ?? new AsciiBuilder();
    }

    public override void Register(List<IExternalFunction> externalFunctions)
    {
        externalFunctions.AddExternalFunction(ExternalFunctionSync.Create(externalFunctions.GenerateId(ExternalFunctionNames.StdIn), ExternalFunctionNames.StdIn, byte () =>
        {
            if (InputPosition >= Input.Length)
            {
                throw new EndOfStreamException();
            }
            return Input[InputPosition++];
        }));
        externalFunctions.AddExternalFunction(ExternalFunctionSync.Create(externalFunctions.GenerateId(ExternalFunctionNames.StdOut), ExternalFunctionNames.StdOut, (byte v) =>
        {
            Output.Append(v);
        }));
    }

    public void Reset()
    {
        InputPosition = 0;
        Output.Clear();
    }
}
