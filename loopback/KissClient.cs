using System.Net.Sockets;

namespace LoopbackTest;

/// <summary>
/// Wraps a KISS/TCP connection. Handles KISS framing (FEND/FESC escaping).
/// ReceiveAsync returns the raw decoded bytes: byte[0] is the KISS command byte,
/// bytes[1..] are the AX.25 payload.
/// </summary>
internal sealed class KissClient : IDisposable
{
    private const byte Fend  = 0xC0;
    private const byte Fesc  = 0xDB;
    private const byte Tfend = 0xDC;
    private const byte Tfesc = 0xDD;

    private readonly TcpClient     _tcp;
    private readonly NetworkStream _ns;
    private readonly byte[]        _oneByte = new byte[1];

    public string Endpoint { get; }

    public KissClient(string host, int port)
    {
        Endpoint = $"{host}:{port}";
        _tcp = new TcpClient();
        _tcp.Connect(host, port);
        _ns = _tcp.GetStream();
    }

    /// <summary>Wraps <paramref name="ax25Frame"/> in a KISS data frame and sends it.</summary>
    public async Task SendAsync(byte[] ax25Frame, CancellationToken ct = default)
    {
        byte[] kiss = Encode(ax25Frame);
        await _ns.WriteAsync(kiss, ct);
        await _ns.FlushAsync(ct);
    }

    /// <summary>
    /// Reads bytes from the stream until a complete KISS frame arrives.
    /// Returns the decoded content (CMD byte + AX.25 data), or null on EOF.
    /// </summary>
    public async Task<byte[]?> ReceiveAsync(CancellationToken ct = default)
    {
        var buf     = new List<byte>(256);
        bool inFrame = false;
        bool escaped = false;

        while (!ct.IsCancellationRequested)
        {
            int n = await _ns.ReadAsync(_oneByte, ct);
            if (n == 0) return null; // connection closed

            byte b = _oneByte[0];

            if (b == Fend)
            {
                if (inFrame && buf.Count > 0)
                    return buf.ToArray();
                inFrame = true;
                buf.Clear();
                escaped = false;
                continue;
            }

            if (!inFrame) continue;

            if (escaped)
            {
                escaped = false;
                b = b == Tfend ? Fend : b == Tfesc ? Fesc : b;
            }
            else if (b == Fesc)
            {
                escaped = true;
                continue;
            }

            buf.Add(b);
        }

        return null;
    }

    private static byte[] Encode(byte[] data)
    {
        var out_ = new List<byte>(data.Length + 4) { Fend, 0x00 }; // data frame type
        foreach (byte b in data)
        {
            if      (b == Fend) { out_.Add(Fesc); out_.Add(Tfend); }
            else if (b == Fesc) { out_.Add(Fesc); out_.Add(Tfesc); }
            else                  out_.Add(b);
        }
        out_.Add(Fend);
        return out_.ToArray();
    }

    public void Dispose()
    {
        _ns.Dispose();
        _tcp.Dispose();
    }
}
