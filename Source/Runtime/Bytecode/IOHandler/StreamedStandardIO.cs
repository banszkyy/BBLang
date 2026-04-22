
using System.IO;

namespace LanguageCore.Runtime;

public sealed class StreamedStandardIO : IO
{
    readonly Stream _in;
    readonly Stream _out;

    public StreamedStandardIO()
    {
        _in = Console.OpenStandardInput();
        _out = Console.OpenStandardOutput();
    }

    public override void Dispose()
    {
        _in.Dispose();
        _out.Dispose();
    }

    public override void Register(List<IExternalFunction> externalFunctions)
    {
        externalFunctions.AddExternalFunction(ExternalFunctionSync.Create(externalFunctions.GenerateId(ExternalFunctionNames.StdIn), ExternalFunctionNames.StdIn, byte () => (byte)_in.ReadByte()));
        externalFunctions.AddExternalFunction(ExternalFunctionSync.Create(externalFunctions.GenerateId(ExternalFunctionNames.StdOut), ExternalFunctionNames.StdOut, void (byte v) => _out.WriteByte(v)));
    }
}
