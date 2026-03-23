namespace HackTnc.Core.Interop;

using System.Runtime.InteropServices;

public sealed class HackrfSession : IDisposable
{
    private bool _disposed;

    public HackrfSession()
    {
        HackrfNative.Check(HackrfNative.hackrf_init(), nameof(HackrfNative.hackrf_init));
    }

    public HackrfDevice OpenDevice(string? serialSuffix)
    {
        ThrowIfDisposed();

        IntPtr device;
        if (string.IsNullOrWhiteSpace(serialSuffix))
        {
            HackrfNative.Check(HackrfNative.hackrf_open(out device), nameof(HackrfNative.hackrf_open));
        }
        else
        {
            HackrfNative.Check(
                HackrfNative.hackrf_open_by_serial(serialSuffix, out device),
                nameof(HackrfNative.hackrf_open_by_serial));
        }

        return new HackrfDevice(device);
    }

    public IReadOnlyList<string> ListDevices()
    {
        ThrowIfDisposed();

        var listPtr = HackrfNative.hackrf_device_list();
        if (listPtr == IntPtr.Zero)
            return [];

        try
        {
            var list = Marshal.PtrToStructure<HackrfNative.HackrfDeviceList>(listPtr);
            if (list.devicecount <= 0 || list.serial_numbers == IntPtr.Zero)
                return [];

            var serials = new List<string>(list.devicecount);
            for (int i = 0; i < list.devicecount; i++)
            {
                var serialPtr = Marshal.ReadIntPtr(list.serial_numbers + i * IntPtr.Size);
                serials.Add(serialPtr != IntPtr.Zero
                    ? Marshal.PtrToStringAnsi(serialPtr) ?? string.Empty
                    : string.Empty);
            }

            return serials;
        }
        finally
        {
            HackrfNative.hackrf_device_list_free(listPtr);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        HackrfNative.Check(HackrfNative.hackrf_exit(), nameof(HackrfNative.hackrf_exit));
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
