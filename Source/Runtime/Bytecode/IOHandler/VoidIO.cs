
namespace LanguageCore.Runtime;

public sealed class VoidIO : IO
{
#pragma warning disable CS0618
    public static readonly VoidIO Instance = new();
#pragma warning restore CS0618

    [Obsolete($"Use {nameof(Instance)} instead")]
    public VoidIO()
    {

    }

    public override void Register(List<IExternalFunction> externalFunctions)
    {
        externalFunctions.AddExternalFunction(ExternalFunctionSync.Create(externalFunctions.GenerateId(ExternalFunctionNames.StdIn), ExternalFunctionNames.StdIn, Read));
        externalFunctions.AddExternalFunction(ExternalFunctionSync.Create<byte>(externalFunctions.GenerateId(ExternalFunctionNames.StdOut), ExternalFunctionNames.StdOut, Write));
    }

    static byte Read()
    {
        throw new InvalidOperationException("Trying to read from void");
    }

    static void Write(byte v)
    {

    }
}
