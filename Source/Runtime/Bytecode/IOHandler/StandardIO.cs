
namespace LanguageCore.Runtime;

public sealed class StandardIO : IO
{
    public static readonly StandardIO Instance = new();

    public override void Register(List<IExternalFunction> externalFunctions)
    {
        externalFunctions.AddExternalFunction(ExternalFunctionSync.Create(externalFunctions.GenerateId(ExternalFunctionNames.StdIn), ExternalFunctionNames.StdIn, Read));
        externalFunctions.AddExternalFunction(ExternalFunctionSync.Create<byte>(externalFunctions.GenerateId(ExternalFunctionNames.StdOut), ExternalFunctionNames.StdOut, Write));
    }

    static byte Read()
    {
        return (byte)Console.Read();
    }

    static void Write(byte v)
    {
        Console.Write((char)v);
    }
}
