using System.Text;

namespace LoopbackTest;

/// <summary>
/// Minimal AX.25 UI frame builder and parser.
/// Frames built here have no digipeater path and no CRC — the TNC adds CRC on TX
/// and strips/validates it on RX before delivering to KISS.
/// </summary>
internal sealed record Ax25Frame(
    string DestCall, int DestSsid,
    string SrcCall,  int SrcSsid,
    string Info)
{
    /// <summary>Builds an AX.25 UI frame ready to send over KISS.</summary>
    public static byte[] BuildUI(
        string destCall, int destSsid,
        string srcCall,  int srcSsid,
        string info)
    {
        var frame = new List<byte>(32);
        frame.AddRange(EncodeCall(destCall, destSsid, isLast: false));
        frame.AddRange(EncodeCall(srcCall,  srcSsid,  isLast: true));
        frame.Add(0x03); // Control: UI frame
        frame.Add(0xF0); // PID: no layer 3
        frame.AddRange(Encoding.ASCII.GetBytes(info));
        return frame.ToArray();
    }

    /// <summary>
    /// Parses an AX.25 UI frame (no digipeaters).
    /// Returns null if data is too short or does not look like a UI frame.
    /// </summary>
    public static Ax25Frame? Parse(byte[] data)
    {
        // Minimum: 7 dest + 7 src + 1 ctrl + 1 pid = 16 bytes
        if (data.Length < 16) return null;

        string destCall = DecodeCall(data, 0, out int destSsid);
        string srcCall  = DecodeCall(data, 7, out int srcSsid);

        // Expect UI control (0x03) and no-L3 PID (0xF0)
        if (data[14] != 0x03 || data[15] != 0xF0) return null;

        string info = Encoding.ASCII.GetString(data, 16, data.Length - 16);
        return new Ax25Frame(destCall, destSsid, srcCall, srcSsid, info);
    }

    // ── Encoding helpers ─────────────────────────────────────────────────────

    private static byte[] EncodeCall(string call, int ssid, bool isLast)
    {
        var result = new byte[7];
        string padded = (call.ToUpper() + "      ")[..6];
        for (int i = 0; i < 6; i++)
            result[i] = (byte)(padded[i] << 1);
        // SSID byte: bits 7-5 = 011 (reserved), bits 4-1 = SSID, bit 0 = extension
        result[6] = (byte)(0x60 | ((ssid & 0x0F) << 1) | (isLast ? 0x01 : 0x00));
        return result;
    }

    private static string DecodeCall(byte[] data, int offset, out int ssid)
    {
        var sb = new StringBuilder(6);
        for (int i = 0; i < 6; i++)
            sb.Append((char)(data[offset + i] >> 1));
        ssid = (data[offset + 6] >> 1) & 0x0F;
        return sb.ToString().TrimEnd();
    }
}
