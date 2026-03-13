using System.Text;
using System.Threading.Tasks;
using System.Timers;
using StreamJsonRpc;

namespace LanguageCore.Runtime;

public sealed class JsonRpcIO : IO, IDisposable
{
    readonly JsonRpc Rpc;
    readonly Queue<char> InputBuffer;
    readonly StringBuilder OutputBuffer;

    Task? KeyRequest;
    readonly Timer FlushTimer;

    public JsonRpcIO(JsonRpc rpc)
    {
        Rpc = rpc;
        InputBuffer = new();
        OutputBuffer = new();
        KeyRequest = null;
        FlushTimer = new Timer(200);
        FlushTimer.Elapsed += OnFlushTimer;
    }

    void OnFlushTimer(object? sender, ElapsedEventArgs e) => Flush();

    public void Flush()
    {
        lock (OutputBuffer)
        {
            if (OutputBuffer.Length > 0)
            {
                Rpc.NotifyAsync("stdout", OutputBuffer.ToString());
                OutputBuffer.Clear();
            }
        }
    }

    void TryRequestKey()
    {
        if (InputBuffer.Count != 0) return;
        if (KeyRequest is not null && !KeyRequest.IsCompleted) return;

        KeyRequest = Rpc.InvokeAsync<string>("stdin")
            .ContinueWith((task) =>
            {
                if (task.IsCompletedSuccessfully && !string.IsNullOrEmpty(task.Result))
                {
                    foreach (char v in task.Result)
                    {
                        InputBuffer.Enqueue(v);
                    }
                }
                else
                {
                    TryRequestKey();
                }
            });
    }

    public override void Register(List<IExternalFunction> externalFunctions)
    {
        externalFunctions.AddExternalFunction(new ExternalFunctionAsync((ref processor, parameters) =>
        {
            TryRequestKey();

            return (ref processor, returnValue) =>
            {
                if (InputBuffer.TryDequeue(out char c))
                {
                    returnValue.Set(c);
                    return true;
                }
                return false;
            };
        }, externalFunctions.GenerateId(ExternalFunctionNames.StdIn), ExternalFunctionNames.StdIn, 0, sizeof(char)));

        externalFunctions.AddExternalFunction(ExternalFunctionSync.Create(externalFunctions.GenerateId(ExternalFunctionNames.StdOut), ExternalFunctionNames.StdOut, (char @char) =>
        {
            lock (OutputBuffer)
            {
                OutputBuffer.Append(@char);
            }
        }));
    }

    public void Dispose()
    {
        Flush();
        FlushTimer.Dispose();
    }
}
