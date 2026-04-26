using System.IO.Pipes;

namespace ShareQ.App.Services;

public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = "Global\\ShareQ.SingleInstance";
    private const string PipeName = "ShareQ.SingleInstance.Pipe";

    private readonly Mutex _mutex;
    private readonly bool _isPrimary;
    private CancellationTokenSource? _serverCts;

    public bool IsPrimary => _isPrimary;

    public event EventHandler? AnotherInstanceStarted;

    public SingleInstanceGuard()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out _isPrimary);
    }

    public async Task NotifyExistingInstanceAsync(CancellationToken cancellationToken)
    {
        if (_isPrimary)
            throw new InvalidOperationException("This process owns the mutex; nothing to notify.");

        await using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
        await client.ConnectAsync(timeout: 1000, cancellationToken).ConfigureAwait(false);
        await client.WriteAsync("show"u8.ToArray(), cancellationToken).ConfigureAwait(false);
        await client.FlushAsync(cancellationToken).ConfigureAwait(false);
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

                var buffer = new byte[16];
                _ = await server.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);

                AnotherInstanceStarted?.Invoke(this, EventArgs.Empty);
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
