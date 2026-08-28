using System.IO.Pipes;
using System.IO;
using System.Text;

namespace XFEToolBox.Client.Utilities;

internal sealed class SingleInstanceService : IDisposable
{
    private const string MutexName = "Local\\XFEToolBox.Client.SingleInstance";
    private const string PipeName = "XFEToolBox.Client.SingleInstance.Pipe";
    private readonly Mutex mutex;
    private readonly string pipeName;
    private readonly CancellationTokenSource cancellationSource = new();
    private Task? listenerTask;

    public SingleInstanceService() : this(string.Empty)
    {
    }

    internal SingleInstanceService(string scope)
    {
        var suffix = string.IsNullOrWhiteSpace(scope) ? string.Empty : $".{scope}";
        pipeName = PipeName + suffix;
        mutex = new Mutex(initiallyOwned: true, MutexName + suffix, out var createdNew);
        IsFirstInstance = createdNew;
    }

    public bool IsFirstInstance { get; }

    public void StartListening(Action<string> messageReceived)
    {
        if (!IsFirstInstance || listenerTask is not null) return;
        listenerTask = Task.Run(async () =>
        {
            while (!cancellationSource.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(cancellationSource.Token);
                    using var reader = new StreamReader(server, Encoding.UTF8, false, 1024, leaveOpen: true);
                    var message = await reader.ReadToEndAsync(cancellationSource.Token);
                    if (!string.IsNullOrWhiteSpace(message)) messageReceived(message.Trim());
                }
                catch (OperationCanceledException) { break; }
                catch
                {
                    await Task.Delay(250, cancellationSource.Token).ConfigureAwait(false);
                }
            }
        }, cancellationSource.Token);
    }

    public static Task<bool> SendAsync(string message) => SendToPipeAsync(message, PipeName);

    internal static async Task<bool> SendAsync(string message, string scope)
        => await SendToPipeAsync(message, PipeName + $".{scope}");

    private static async Task<bool> SendToPipeAsync(string message, string pipeName)
    {
        try
        {
            await using var client = new NamedPipeClientStream(
                ".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await client.ConnectAsync(timeout.Token);
            var bytes = Encoding.UTF8.GetBytes(message);
            await client.WriteAsync(bytes, timeout.Token);
            await client.FlushAsync(timeout.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        cancellationSource.Cancel();
        try { listenerTask?.Wait(TimeSpan.FromSeconds(1)); }
        catch (AggregateException) { }
        cancellationSource.Dispose();
        if (IsFirstInstance)
        {
            try { mutex.ReleaseMutex(); } catch (ApplicationException) { }
        }
        mutex.Dispose();
    }
}
