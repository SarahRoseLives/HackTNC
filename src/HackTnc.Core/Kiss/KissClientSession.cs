using System.Net.Sockets;

namespace HackTnc.Core.Kiss;

internal sealed class KissClientSession : IAsyncDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly KissProtocol.Decoder _decoder = new();
    private readonly Func<byte[], Task> _onFrame;
    private readonly Action<KissClientSession> _onClosed;
    private readonly Action<string> _log;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public KissClientSession(TcpClient client, Func<byte[], Task> onFrame, Action<KissClientSession> onClosed, Action<string> log)
    {
        _client = client;
        _stream = client.GetStream();
        _onFrame = onFrame;
        _onClosed = onClosed;
        _log = log;
    }

    public string RemoteEndPoint => _client.Client.RemoteEndPoint?.ToString() ?? "unknown";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await _stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                var frames = _decoder.Feed(buffer.AsSpan(0, read));
                foreach (var frame in frames)
                {
                    if (KissProtocol.TryExtractDataFrame(frame, out var payload))
                    {
                        await _onFrame(payload).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException exception)
        {
            _log($"KISS client {RemoteEndPoint} disconnected: {exception.Message}");
        }
        finally
        {
            _onClosed(this);
        }
    }

    public async Task SendAsync(byte[] frame, CancellationToken cancellationToken)
    {
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _stream.Close();
            _client.Close();
        }
        finally
        {
            _sendLock.Dispose();
            await Task.CompletedTask;
        }
    }
}
