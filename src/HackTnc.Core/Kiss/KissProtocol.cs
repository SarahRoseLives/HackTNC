namespace HackTnc.Core.Kiss;

public static class KissProtocol
{
    public const byte Fend = 0xC0;
    public const byte Fesc = 0xDB;
    public const byte Tfend = 0xDC;
    public const byte Tfesc = 0xDD;
    public const byte DataFrameCommand = 0x00;

    public static byte[] EncodeDataFrame(ReadOnlySpan<byte> payload)
    {
        var frame = new List<byte>(payload.Length + 3) { Fend, DataFrameCommand };
        AppendEscaped(frame, payload);
        frame.Add(Fend);
        return frame.ToArray();
    }

    public static bool TryExtractDataFrame(ReadOnlySpan<byte> frame, out byte[] payload)
    {
        payload = Array.Empty<byte>();
        if (frame.Length == 0)
        {
            return false;
        }

        var command = frame[0];
        if ((command & 0x0F) != DataFrameCommand)
        {
            return false;
        }

        payload = frame[1..].ToArray();
        return true;
    }

    public sealed class Decoder
    {
        private readonly List<byte> _buffer = new();
        private bool _escaped;

        public IReadOnlyList<byte[]> Feed(ReadOnlySpan<byte> data)
        {
            var frames = new List<byte[]>();

            foreach (var value in data)
            {
                if (value == Fend)
                {
                    if (_buffer.Count > 0)
                    {
                        frames.Add(_buffer.ToArray());
                        _buffer.Clear();
                        _escaped = false;
                    }

                    continue;
                }

                if (_escaped)
                {
                    _buffer.Add(value switch
                    {
                        Tfend => Fend,
                        Tfesc => Fesc,
                        _ => value
                    });
                    _escaped = false;
                    continue;
                }

                if (value == Fesc)
                {
                    _escaped = true;
                    continue;
                }

                _buffer.Add(value);
            }

            return frames;
        }
    }

    private static void AppendEscaped(List<byte> destination, ReadOnlySpan<byte> payload)
    {
        foreach (var value in payload)
        {
            switch (value)
            {
                case Fend:
                    destination.Add(Fesc);
                    destination.Add(Tfend);
                    break;
                case Fesc:
                    destination.Add(Fesc);
                    destination.Add(Tfesc);
                    break;
                default:
                    destination.Add(value);
                    break;
            }
        }
    }
}
