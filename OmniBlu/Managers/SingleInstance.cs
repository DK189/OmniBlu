using System.IO.Pipes;

namespace OmniBlu.Managers;

/// <summary>
/// Lets only one copy of the app run for each user. The first copy owns a named mutex and listens on a named pipe. A second
/// copy finds the mutex taken, tells the first copy to show its window through the pipe, and then exits.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly string pipeName;
    private readonly CancellationTokenSource stop = new();

    private SingleInstance(Mutex mutex, string pipeName)
    {
        this.mutex = mutex;
        this.pipeName = pipeName;
    }

    /// <summary>Takes ownership for this copy. Returns null when another copy of the app is already running.</summary>
    public static SingleInstance TryAcquire(string name)
    {
        var mutex = new Mutex(true, @"Local\" + name, out var created);
        if (!created)
        {
            mutex.Dispose();
            return null;
        }

        return new SingleInstance(mutex, PipeName(name));
    }

    /// <summary>
    /// Asks the copy that is running to show its window. Returns false when it did not answer in time
    /// (<paramref name="timeoutMs"/> milliseconds).
    /// </summary>
    public static bool SignalExisting(string name, int timeoutMs)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName(name), PipeDirection.Out);
            client.Connect(timeoutMs);
            client.WriteByte(1);
            client.Flush();
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Starts to listen for a second copy. <paramref name="onShowRequested"/> is called on a background thread each time
    /// one asks. If listening cannot start, the app still runs; a second copy then just exits without showing anything.
    /// </summary>
    public void StartListening(Action onShowRequested)
    {
        var token = stop.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await using var server = new NamedPipeServerStream(
                        pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(token);
                    var buffer = new byte[1];
                    if (await server.ReadAsync(buffer, token) > 0)
                    {
                        onShowRequested();
                    }
                }
            }
            catch (Exception)
            {
                // Cancelled when the app exits, or the pipe is not available. Listening just ends.
            }
        }, token);
    }

    public void Dispose()
    {
        stop.Cancel();
        try
        {
            mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Released from another thread than the one that took it. Closing the handle below frees the name anyway.
        }

        mutex.Dispose();
        stop.Dispose();
    }

    private static string PipeName(string name) => $"{name}.{Environment.UserName}";
}
