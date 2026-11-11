using System.IO.Pipes;

namespace OmniBlu.Managers;

/// <summary>
/// Lets only one copy of the app run for each user. The first copy owns a named mutex and listens on a named pipe. A second
/// copy finds the mutex taken, tells the first copy to show its window through the pipe, and then exits.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const int ReadTimeoutMs = 2000;

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
    /// (<paramref name="timeoutMs"/> milliseconds). <paramref name="message"/> (see <see cref="InstanceMessage"/>) lets the
    /// running copy know which version and file this copy has; when it is null, only the one-byte "show" signal is sent.
    /// </summary>
    public static bool SignalExisting(string name, int timeoutMs, string message = null)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName(name), PipeDirection.Out);
            client.Connect(timeoutMs);
            if (message == null)
            {
                client.WriteByte(1);
            }
            else
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(message);
                client.Write(bytes, 0, bytes.Length);
            }

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
    public void StartListening(Action onShowRequested) => StartListening(_ => onShowRequested());

    /// <summary>
    /// Same, and <paramref name="onRequest"/> gets the text that the second copy sent (empty for the old one-byte signal). See
    /// <see cref="InstanceMessage.Parse"/>. A problem with one request does not stop the listening.
    /// </summary>
    public void StartListening(Action<string> onRequest)
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

                    // One client must not hold the listener: it has a short time and a size limit. A client that is too slow
                    // or sends too much is cut off (the handler still hears "show", with a text that is not understood).
                    string text;
                    using (var limit = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        limit.CancelAfter(ReadTimeoutMs);
                        try
                        {
                            text = await ReadMessageAsync(server, limit.Token);
                        }
                        catch (OperationCanceledException) when (!token.IsCancellationRequested)
                        {
                            text = string.Empty;
                        }
                    }

                    if (text != null)
                    {
                        try
                        {
                            onRequest(text);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            // The handler failed for this request. The next request must still work.
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Cancelled when the app exits, or the pipe is not available. Listening just ends.
            }
        }, token);
    }

    /// <summary>Reads what the client sends until it closes the pipe, at most <see cref="InstanceMessage.MaxLength"/> characters. Null when nothing came.</summary>
    private static async Task<string> ReadMessageAsync(Stream pipe, CancellationToken token)
    {
        var buffer = new byte[InstanceMessage.MaxLength * 3 + 4]; // UTF-8: at most 3 bytes for each character kept
        var total = 0;
        int read;
        while (total < buffer.Length && (read = await pipe.ReadAsync(buffer.AsMemory(total), token)) > 0)
        {
            total += read;
        }

        if (total == 0)
        {
            return null;
        }

        // The old one-byte signal is byte 1, which is not text: it gives an empty message ("only show the window").
        var text = System.Text.Encoding.UTF8.GetString(buffer, 0, total);
        return total == 1 && buffer[0] == 1 ? string.Empty : text;
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
