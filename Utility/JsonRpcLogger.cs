using System.Threading.Tasks;
using StreamJsonRpc;

class JsonRpcLogger
{
    readonly JsonRpc Rpc;

    public JsonRpcLogger(JsonRpc rpc)
    {
        Rpc = rpc;
    }

    static string FormatMessage(object? message) => message switch
    {
        Exception ex => $"{ex.GetType().Name} {ex.Message}\n{ex.StackTrace}\n\n{FormatMessage(ex.InnerException)}".TrimEnd(),
        null => string.Empty,
        _ => message.ToString() ?? string.Empty,
    };

    public Task Error(string message) => Rpc.NotifyAsync("log", "error", message);
    public Task Warn(string message) => Rpc.NotifyAsync("log", "warn", message);
    public Task Info(string message) => Rpc.NotifyAsync("log", "info", message);
    public Task Debug(string message) => Rpc.NotifyAsync("log", "debug", message);
    public Task Trace(string message) => Rpc.NotifyAsync("log", "trace", message);

    public Task Error(object? message) => Error(FormatMessage(message));
    public Task Warn(object? message) => Warn(FormatMessage(message));
    public Task Info(object? message) => Info(FormatMessage(message));
    public Task Debug(object? message) => Debug(FormatMessage(message));
    public Task Trace(object? message) => Trace(FormatMessage(message));
}

