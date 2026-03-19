namespace HackTnc.Core.Interop;

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
