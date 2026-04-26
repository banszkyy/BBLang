
namespace LanguageCore.Runtime;

public sealed class CallbackIO : IO
{
    readonly Action<byte> Out;
    readonly Func<byte> In;

    public CallbackIO(Action<byte> @out, Func<byte> @in)
    {
        Out = @out;
        In = @in;
    }

    public override void Register(List<IExternalFunction> externalFunctions)
    {
        externalFunctions.AddExternalFunction(ExternalFunctionSync.Create(externalFunctions.GenerateId(ExternalFunctionNames.StdIn), ExternalFunctionNames.StdIn, In));
        externalFunctions.AddExternalFunction(ExternalFunctionSync.Create(externalFunctions.GenerateId(ExternalFunctionNames.StdOut), ExternalFunctionNames.StdOut, Out));
    }
}
