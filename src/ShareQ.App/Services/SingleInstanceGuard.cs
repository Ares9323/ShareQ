using System.IO;
using System.IO.Pipes;
using System.Text;

namespace ShareQ.App.Services;

/// <summary>Mutex + named pipe combo that enforces "one ShareQ process per user". The first
/// process owns the mutex (<see cref="IsPrimary"/> = true) and listens on the pipe; subsequent
/// launches detect they're not primary and forward the activation request through the pipe so
/// the primary can react (typically: bring the window to front, or import an .sxcu file passed
/// via Explorer file association).
///
/// Pipe protocol: a single UTF-8 message per connection, terminated by EOF (client closes after
/// writing). Special message <c>"show"</c> means "user re-launched without args, focus your
/// window"; anything else is treated as a payload and surfaced via
/// <see cref="AnotherInstanceStarted"/>'s message arg. Caller decides what to do with it (we use
/// the prefix <c>"sxcu:"</c> followed by an absolute file path for .sxcu imports).</summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = "Global\\ShareQ.SingleInstance";
    private const string PipeName = "ShareQ.SingleInstance.Pipe";

    /// <summary>Conventional payload used by a re-launch with no args — primary brings UI front.</summary>
    public const string ShowMessage = "show";
    /// <summary>Prefix for "open a .sxcu file in the primary instance" messages — followed by
    /// the absolute file path. Kept here so producers / consumers stay in sync.</summary>
    public const string SxcuPrefix = "sxcu:";

    private readonly Mutex _mutex;
    private readonly bool _isPrimary;
    private CancellationTokenSource? _serverCts;

    public bool IsPrimary => _isPrimary;

    /// <summary>Raised on the primary instance whenever a secondary launch reaches us. The
    /// string carries the message exchanged on the pipe — either <see cref="ShowMessage"/> or a
    /// <see cref="SxcuPrefix"/>-prefixed file path. Handlers run on a thread-pool thread; marshal
    /// to the UI dispatcher before touching WPF state.</summary>
    public event EventHandler<string>? AnotherInstanceStarted;

    public SingleInstanceGuard()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out _isPrimary);
    }

    /// <summary>Send a payload to the primary instance. Convenience overload sends
    /// <see cref="ShowMessage"/> for the "just bring window to front" case.</summary>
    public Task NotifyExistingInstanceAsync(CancellationToken cancellationToken)
        => NotifyExistingInstanceAsync(ShowMessage, cancellationToken);

    public async Task NotifyExistingInstanceAsync(string message, CancellationToken cancellationToken)
    {
        if (_isPrimary)
            throw new InvalidOperationException("This process owns the mutex; nothing to notify.");
        ArgumentNullException.ThrowIfNull(message);

        await using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
        await client.ConnectAsync(timeout: 1000, cancellationToken).ConfigureAwait(false);
        var bytes = Encoding.UTF8.GetBytes(message);
        await client.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await client.FlushAsync(cancellationToken).ConfigureAwait(false);
        // Closing the client (via the using above) signals EOF to the server, which is how it
        // knows the message is fully received without us length-prefixing.
    }

    public void StartListening()
    {
        if (!_isPrimary) return;
        _serverCts = new CancellationTokenSource();
        _ = Task.Run(() => RunServerAsync(_serverCts.Token));
    }

    private async Task RunServerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName, PipeDirection.In, maxNumberOfServerInstances: 1);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                // Read the full message until client closes the pipe (EOF). Using a MemoryStream
                // copy because the pipe stream isn't seekable and a StreamReader.ReadToEndAsync
                // would also work but pulls in extra buffering.
                using var ms = new MemoryStream();
                await server.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
                var message = Encoding.UTF8.GetString(ms.ToArray());
                if (string.IsNullOrEmpty(message)) message = ShowMessage;

                AnotherInstanceStarted?.Invoke(this, message);
            }
            catch (OperationCanceledException) { return; }
            catch
            {
                // Pipe failures mid-cycle are non-fatal; loop unless cancellation requested.
            }
        }
    }

    public void Dispose()
    {
        _serverCts?.Cancel();
        _serverCts?.Dispose();
        if (_isPrimary) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
