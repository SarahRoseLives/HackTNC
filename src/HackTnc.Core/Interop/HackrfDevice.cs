using System.Runtime.InteropServices;
using HackTnc.Core.Configuration;

namespace HackTnc.Core.Interop;

public sealed class HackrfDevice : IDisposable
{
    // Number of full zero-filled transfer buffers to send after all packet data
    // is exhausted. This ensures the last USB transfer reaches the RF chain
    // before we call hackrf_stop_tx. Two buffers at 2 Msps ≈ 130 ms of tail.
    private const int TxDrainBufferCount = 2;

    private readonly IntPtr _device;
    private readonly GCHandle _selfHandle;
    private readonly object _sync = new();
    private readonly HackrfNative.HackrfSampleBlockCallback _rxCallback;
    private readonly HackrfNative.HackrfSampleBlockCallback _txCallback;
    private Action<byte[]>? _rxHandler;
    private byte[]? _txBuffer;
    private int _txOffset;
    private int _txDrainCount;
    private SemaphoreSlim? _txCallbackDone;
    private bool _rxActive;
    private bool _txActive;
    private bool _disposed;

    internal HackrfDevice(IntPtr device)
    {
        _device = device;
        _selfHandle = GCHandle.Alloc(this);
        _rxCallback = HandleRx;
        _txCallback = HandleTx;
    }

    public string FirmwareVersion => HackrfNative.ReadVersionString(_device);

    public void ConfigureReceive(TncOptions options)
    {
        ThrowIfDisposed();
        ConfigureShared(options);
        HackrfNative.Check(HackrfNative.hackrf_set_lna_gain(_device, (uint)NormalizeStep(options.LnaGainDb, 0, 40, 8)), nameof(HackrfNative.hackrf_set_lna_gain));
        HackrfNative.Check(HackrfNative.hackrf_set_vga_gain(_device, (uint)NormalizeStep(options.VgaGainDb, 0, 62, 2)), nameof(HackrfNative.hackrf_set_vga_gain));
    }

    public void ConfigureTransmit(TncOptions options)
    {
        ThrowIfDisposed();
        ConfigureShared(options);
        HackrfNative.Check(HackrfNative.hackrf_set_txvga_gain(_device, (uint)NormalizeStep(options.TxVgaGainDb, 0, 47, 1)), nameof(HackrfNative.hackrf_set_txvga_gain));
    }

    public void StartReceive(Action<byte[]> onSamples)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(onSamples);

        lock (_sync)
        {
            if (_rxActive)
            {
                throw new InvalidOperationException("HackRF RX is already active.");
            }

            _rxHandler = onSamples;
            HackrfNative.Check(
                HackrfNative.hackrf_start_rx(_device, _rxCallback, GCHandle.ToIntPtr(_selfHandle)),
                nameof(HackrfNative.hackrf_start_rx));
            _rxActive = true;
        }
    }

    public void StopReceive()
    {
        lock (_sync)
        {
            if (!_rxActive || _disposed)
            {
                return;
            }

            HackrfNative.Check(HackrfNative.hackrf_stop_rx(_device), nameof(HackrfNative.hackrf_stop_rx));
            _rxActive = false;
            _rxHandler = null;
        }
    }

    public async Task TransmitAsync(byte[] iqBytes, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(iqBytes);

        SemaphoreSlim callbackDone;

        lock (_sync)
        {
            if (_txActive)
            {
                throw new InvalidOperationException("HackRF TX is already active.");
            }

            _txBuffer = iqBytes;
            _txOffset = 0;
            _txDrainCount = 0;
            callbackDone = new SemaphoreSlim(0, 1);
            _txCallbackDone = callbackDone;

            HackrfNative.Check(
                HackrfNative.hackrf_start_tx(_device, _txCallback, GCHandle.ToIntPtr(_selfHandle)),
                nameof(HackrfNative.hackrf_start_tx));
            _txActive = true;
        }

        try
        {
            // Wait for the TX callback to signal it has returned -1 (all data
            // plus drain buffers have been submitted to libusb).
            await callbackDone.WaitAsync(cancellationToken).ConfigureAwait(false);

            // Poll hackrf_is_streaming until the device is no longer actively
            // streaming (in-flight USB transfers have drained), or a 5-second
            // safety timeout elapses.
            var deadline = Environment.TickCount64 + 5_000;
            while (Environment.TickCount64 < deadline && !cancellationToken.IsCancellationRequested)
            {
                var status = HackrfNative.hackrf_is_streaming(_device);
                if (status != HackrfNative.HACKRF_TRUE)
                {
                    break;
                }

                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            StopTransmit();
            callbackDone.Dispose();
        }
    }

    public void StopTransmit()
    {
        lock (_sync)
        {
            if (!_txActive || _disposed)
            {
                return;
            }

            HackrfNative.Check(HackrfNative.hackrf_stop_tx(_device), nameof(HackrfNative.hackrf_stop_tx));
            _txActive = false;
            _txBuffer = null;
            _txOffset = 0;
            _txDrainCount = 0;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            StopReceive();
        }
        catch
        {
        }

        try
        {
            StopTransmit();
        }
        catch
        {
        }

        HackrfNative.Check(HackrfNative.hackrf_close(_device), nameof(HackrfNative.hackrf_close));
        _selfHandle.Free();
    }

    private void ConfigureShared(TncOptions options)
    {
        HackrfNative.Check(HackrfNative.hackrf_set_freq(_device, (ulong)options.FrequencyHz), nameof(HackrfNative.hackrf_set_freq));
        HackrfNative.Check(HackrfNative.hackrf_set_sample_rate(_device, options.SampleRateHz), nameof(HackrfNative.hackrf_set_sample_rate));
        var filterBandwidth = HackrfNative.hackrf_compute_baseband_filter_bw((uint)options.BasebandFilterBandwidthHz);
        HackrfNative.Check(
            HackrfNative.hackrf_set_baseband_filter_bandwidth(_device, filterBandwidth),
            nameof(HackrfNative.hackrf_set_baseband_filter_bandwidth));
        HackrfNative.Check(HackrfNative.hackrf_set_amp_enable(_device, options.AmpEnable ? (byte)1 : (byte)0), nameof(HackrfNative.hackrf_set_amp_enable));
        HackrfNative.Check(
            HackrfNative.hackrf_set_antenna_enable(_device, options.AntennaPowerEnable ? (byte)1 : (byte)0),
            nameof(HackrfNative.hackrf_set_antenna_enable));
    }

    private int HandleRx(ref HackrfNative.HackrfTransfer transfer)
    {
        try
        {
            var length = Math.Min(transfer.valid_length, transfer.buffer_length);
            if (length <= 0)
            {
                return 0;
            }

            var buffer = new byte[length];
            Marshal.Copy(transfer.buffer, buffer, 0, length);
            _rxHandler?.Invoke(buffer);
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    private int HandleTx(ref HackrfNative.HackrfTransfer transfer)
    {
        try
        {
            var source = _txBuffer;
            if (source == null)
            {
                _txCallbackDone?.Release();
                return -1;
            }

            var remaining = source.Length - _txOffset;

            if (remaining > 0)
            {
                // Copy as much data as fits in this transfer buffer.
                var count = Math.Min(remaining, transfer.buffer_length);
                Marshal.Copy(source, _txOffset, transfer.buffer, count);

                // Zero-pad the tail of the buffer so we always send a full
                // buffer_length frame (required by libhackrf on Windows).
                if (count < transfer.buffer_length)
                {
                    var pad = new byte[transfer.buffer_length - count];
                    Marshal.Copy(pad, 0, IntPtr.Add(transfer.buffer, count), pad.Length);
                }

                transfer.valid_length = transfer.buffer_length;
                _txOffset += count;
                return 0;
            }

            // All packet data has been submitted. Send TxDrainBufferCount
            // additional zero-filled buffers so the last real data clears the
            // USB transfer queue before we signal completion.
            if (_txDrainCount < TxDrainBufferCount)
            {
                var zeros = new byte[transfer.buffer_length];
                Marshal.Copy(zeros, 0, transfer.buffer, transfer.buffer_length);
                transfer.valid_length = transfer.buffer_length;
                _txDrainCount++;
                return 0;
            }

            // Signal the awaiting TransmitAsync, then tell libhackrf to stop.
            _txCallbackDone?.Release();
            return -1;
        }
        catch
        {
            _txCallbackDone?.Release();
            return -1;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static int NormalizeStep(int value, int min, int max, int step)
    {
        var clamped = Math.Clamp(value, min, max);
        return ((clamped - min) / step) * step + min;
    }
}
