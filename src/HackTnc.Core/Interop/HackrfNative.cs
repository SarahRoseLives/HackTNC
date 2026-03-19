using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace HackTnc.Core.Interop;

internal static class HackrfNative
{
    private const string LibraryName = "hackrf.dll";
    private static string? _configuredPath;
    private static IntPtr _loadedHandle;

    static HackrfNative()
    {
        NativeLibrary.SetDllImportResolver(typeof(HackrfNative).Assembly, ResolveLibrary);
    }

    internal const int HACKRF_SUCCESS = 0;
    internal const int HACKRF_TRUE = 1;

    internal static void ConfigureLibraryPath(string? libraryPath)
    {
        if (string.IsNullOrWhiteSpace(libraryPath))
        {
            return;
        }

        _configuredPath = Path.GetFullPath(libraryPath);
        if (_loadedHandle == IntPtr.Zero)
        {
            _loadedHandle = NativeLibrary.Load(_configuredPath);
        }
    }

    internal static void Check(int result, string operation)
    {
        if (result == HACKRF_SUCCESS)
        {
            return;
        }

        throw new InvalidOperationException($"{operation} failed: {GetErrorName(result)} ({result}).");
    }

    internal static string GetErrorName(int errorCode)
    {
        var pointer = hackrf_error_name(errorCode);
        return pointer == IntPtr.Zero
            ? "unknown"
            : Marshal.PtrToStringAnsi(pointer) ?? "unknown";
    }

    internal static string ReadVersionString(IntPtr device)
    {
        var buffer = new byte[255];
        Check(hackrf_version_string_read(device, buffer, 254), nameof(hackrf_version_string_read));
        var length = Array.IndexOf(buffer, (byte)0);
        if (length < 0)
        {
            length = buffer.Length;
        }

        return Encoding.ASCII.GetString(buffer, 0, length);
    }

    private static IntPtr ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!libraryName.Equals(LibraryName, StringComparison.OrdinalIgnoreCase) &&
            !libraryName.Equals("hackrf", StringComparison.OrdinalIgnoreCase))
        {
            return IntPtr.Zero;
        }

        if (_loadedHandle != IntPtr.Zero)
        {
            return _loadedHandle;
        }

        if (!string.IsNullOrWhiteSpace(_configuredPath))
        {
            _loadedHandle = NativeLibrary.Load(_configuredPath);
            return _loadedHandle;
        }

        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HackrfTransfer
    {
        public IntPtr device;
        public IntPtr buffer;
        public int buffer_length;
        public int valid_length;
        public IntPtr rx_ctx;
        public IntPtr tx_ctx;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int HackrfSampleBlockCallback(ref HackrfTransfer transfer);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int hackrf_init();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int hackrf_exit();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int hackrf_open(out IntPtr device);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern int hackrf_open_by_serial(string? desiredSerialNumber, out IntPtr device);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int hackrf_close(IntPtr device);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int hackrf_start_rx(IntPtr device, HackrfSampleBlockCallback callback, IntPtr rxContext);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int hackrf_stop_rx(IntPtr device);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int hackrf_start_tx(IntPtr device, HackrfSampleBlockCallback callback, IntPtr txContext);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int hackrf_stop_tx(IntPtr device);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int hackrf_is_streaming(IntPtr device);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int hackrf_set_freq(IntPtr device, ulong frequencyHz);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int hackrf_set_sample_rate(IntPtr device, double sampleRateHz);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int hackrf_set_baseband_filter_bandwidth(IntPtr device, uint bandwidthHz);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint hackrf_compute_baseband_filter_bw(uint bandwidthHz);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int hackrf_set_amp_enable(IntPtr device, byte value);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int hackrf_set_antenna_enable(IntPtr device, byte value);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int hackrf_set_lna_gain(IntPtr device, uint value);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int hackrf_set_vga_gain(IntPtr device, uint value);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int hackrf_set_txvga_gain(IntPtr device, uint value);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr hackrf_error_name(int errorCode);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int hackrf_version_string_read(IntPtr device, byte[] version, byte length);
}
