using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace HackTnc.Core.Kiss;

public sealed class KissTcpServer : IAsyncDisposable
{
    private readonly IPAddress _bindAddress;
    private readonly int _port;
    private readonly Action<string> _log;
    private readonly ConcurrentDictionary<Guid, KissClientSession> _sessions = new();
    private TcpListener? _listener;
    private Task? _acceptTask;

    public KissTcpServer(string bindAddress, int port, Action<string> log)
    {
        _bindAddress = ResolveAddress(bindAddress);
        _port = port;
        _log = log;
    }

    public event Func<byte[], Task>? FrameReceived;
    public event Action<string>? ClientConnected;
    public event Action<string>? ClientDisconnected;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _listener = new TcpListener(_bindAddress, _port);
        _listener.Start();
        _acceptTask = Task.Run(() => AcceptLoopAsync(cancellationToken), cancellationToken);
        _log($"KISS/TCP listening on {_bindAddress}:{_port}");
        return Task.CompletedTask;
    }

    public async Task BroadcastFrameAsync(byte[] payload, CancellationToken cancellationToken)
    {
        var encoded = KissProtocol.EncodeDataFrame(payload);
        foreach (var session in _sessions.Values)
        {
            try
            {
                await session.SendAsync(encoded, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
            {
                _log($"KISS client {session.RemoteEndPoint} send failed: {exception.Message}");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_listener != null)
        {
            _listener.Stop();
        }

        if (_acceptTask != null)
        {
            try
            {
                await _acceptTask.ConfigureAwait(false);
            }
            catch
            {
            }
        }

        foreach (var session in _sessions.Values)
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        if (_listener == null)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            var sessionId = Guid.NewGuid();
            var session = new KissClientSession(
                client,
                HandleIncomingFrameAsync,
                closed =>
                {
                    _sessions.TryRemove(sessionId, out _);
                    ClientDisconnected?.Invoke(closed.RemoteEndPoint);
                    _ = closed.DisposeAsync();
                },
                _log);

            _sessions[sessionId] = session;
            var endpoint = session.RemoteEndPoint;
            _log($"KISS client connected: {endpoint}");
            ClientConnected?.Invoke(endpoint);
            _ = Task.Run(() => session.RunAsync(cancellationToken), cancellationToken);
        }
    }

    private Task HandleIncomingFrameAsync(byte[] frame)
    {
        return FrameReceived?.Invoke(frame) ?? Task.CompletedTask;
    }

    private static IPAddress ResolveAddress(string bindAddress)
    {
        if (IPAddress.TryParse(bindAddress, out var address))
        {
            return address;
        }

        return Dns.GetHostAddresses(bindAddress)[0];
    }
}
