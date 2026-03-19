using System.Threading.Channels;
using HackTnc.Core.Configuration;
using HackTnc.Core.Interop;
using HackTnc.Core.Kiss;
using HackTnc.Core.Signal;

namespace HackTnc.Core.Services;

public sealed class HackrfKissTncService : IAsyncDisposable
{
    private readonly TncOptions _options;
    private readonly Action<string> _log;
    private readonly Channel<byte[]> _rxIqChannel = Channel.CreateUnbounded<byte[]>();
    private readonly Channel<byte[]> _txFrameChannel = Channel.CreateUnbounded<byte[]>();
    private readonly SemaphoreSlim _modeGate = new(1, 1);
    private readonly HackrfSession _hackrfSession;
    private readonly HackrfDevice _hackrfDevice;
    private readonly KissTcpServer _kissServer;
    private readonly FmAudioDecoder _audioDecoder;
    private readonly FmIqEncoder _iqEncoder;
    private readonly ax25.AFSK1200Modulator _modulator;
    private readonly ax25.AFSK1200Demodulator _demodulator;
    private CancellationTokenSource? _lifetimeCts;
    private Task? _rxTask;
    private Task? _txTask;
    private bool _started;

    public HackrfKissTncService(TncOptions options, Action<string> log)
    {
        _options = options;
        _log = log;

        HackrfNative.ConfigureLibraryPath(options.HackrfLibraryPath);
        _hackrfSession = new HackrfSession();
        _hackrfDevice = _hackrfSession.OpenDevice(options.SerialSuffix);
        _kissServer = new KissTcpServer(options.BindAddress, options.KissPort, _log);
        _audioDecoder = new FmAudioDecoder(
            options.SampleRateHz,
            options.AudioSampleRate,
            options.RxAudioGain > 0
                ? options.RxAudioGain
                : options.SampleRateHz / (2.0 * Math.PI * options.FmDeviationHz));
        _iqEncoder = new FmIqEncoder(options.SampleRateHz, options.AudioSampleRate, options.FmDeviationHz, options.TxAudioGain);
        _modulator = new ax25.AFSK1200Modulator(options.AudioSampleRate)
        {
            txDelayMs = options.TxDelayMs,
            txTailMs = options.TxTailMs
        };
        _demodulator = new ax25.AFSK1200Demodulator(
            options.AudioSampleRate,
            ResolveFilterLength(options.AudioSampleRate),
            0,
            new ReceivedPacketHandler(this));
    }

    public event Action<string>? ClientConnected;
    public event Action<string>? ClientDisconnected;
    public event Action<string>? PacketReceived;
    public event Action<string>? PacketTransmitted;

    public string FirmwareVersion => _hackrfDevice.FirmwareVersion;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _kissServer.FrameReceived += EnqueueTransmitFrameAsync;
        _kissServer.ClientConnected += ep => ClientConnected?.Invoke(ep);
        _kissServer.ClientDisconnected += ep => ClientDisconnected?.Invoke(ep);
        await _kissServer.StartAsync(_lifetimeCts.Token).ConfigureAwait(false);

        _rxTask = Task.Run(() => ProcessReceiveAsync(_lifetimeCts.Token), _lifetimeCts.Token);
        _txTask = Task.Run(() => ProcessTransmitAsync(_lifetimeCts.Token), _lifetimeCts.Token);

        await _modeGate.WaitAsync(_lifetimeCts.Token).ConfigureAwait(false);
        try
        {
            StartReceiveMode();
        }
        finally
        {
            _modeGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_lifetimeCts != null && !_lifetimeCts.IsCancellationRequested)
        {
            _lifetimeCts.Cancel();
        }

        try
        {
            _hackrfDevice.StopReceive();
        }
        catch
        {
        }

        try
        {
            _hackrfDevice.StopTransmit();
        }
        catch
        {
        }

        if (_rxTask != null)
        {
            try
            {
                await _rxTask.ConfigureAwait(false);
            }
            catch
            {
            }
        }

        if (_txTask != null)
        {
            try
            {
                await _txTask.ConfigureAwait(false);
            }
            catch
            {
            }
        }

        await _kissServer.DisposeAsync().ConfigureAwait(false);
        _hackrfDevice.Dispose();
        _hackrfSession.Dispose();
        _modeGate.Dispose();
        _lifetimeCts?.Dispose();
    }

    private void StartReceiveMode()
    {
        _hackrfDevice.ConfigureReceive(_options);
        _hackrfDevice.StartReceive(chunk =>
        {
            if (!_rxIqChannel.Writer.TryWrite(chunk))
            {
                _log("Dropped RX IQ chunk because the processing channel is unavailable.");
            }
        });
        _log($"HackRF RX started at {_options.FrequencyHz} Hz.");
    }

    private async Task ProcessReceiveAsync(CancellationToken cancellationToken)
    {
        await foreach (var chunk in _rxIqChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            var audio = _audioDecoder.Decode(chunk);
            if (audio.Length > 0)
            {
                _demodulator.AddSamples(audio, audio.Length);
            }
        }
    }

    private async Task ProcessTransmitAsync(CancellationToken cancellationToken)
    {
        await foreach (var frame in _txFrameChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            await _modeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _hackrfDevice.StopReceive();
                await TransmitFrameAsync(frame, cancellationToken).ConfigureAwait(false);
                if (!cancellationToken.IsCancellationRequested)
                {
                    StartReceiveMode();
                }
            }
            finally
            {
                _modeGate.Release();
            }
        }
    }

    private async Task TransmitFrameAsync(byte[] frame, CancellationToken cancellationToken)
    {
        var packet = new ax25.Packet(ToSBytes(frame));
        _modulator.GetSamples(packet, out float[] audioSamples);
        var iqBytes = _iqEncoder.Encode(audioSamples);

        var formatted = ax25.Packet.Format(ToSBytes(frame));
        _hackrfDevice.ConfigureTransmit(_options);
        _log($"TX {formatted}");
        PacketTransmitted?.Invoke(formatted);
        await _hackrfDevice.TransmitAsync(iqBytes, cancellationToken).ConfigureAwait(false);
        _log($"TX complete ({iqBytes.Length} IQ bytes).");
    }

    private Task EnqueueTransmitFrameAsync(byte[] frame)
    {
        if (!_txFrameChannel.Writer.TryWrite(frame))
        {
            throw new InvalidOperationException("Unable to enqueue KISS transmit frame.");
        }

        _log($"Queued TX frame ({frame.Length} bytes).");
        return Task.CompletedTask;
    }

    private async Task ForwardReceivedPacketAsync(sbyte[] frame)
    {
        var bytes = ToBytes(frame);
        var formatted = ax25.Packet.Format(frame);
        _log($"RX {formatted}");
        PacketReceived?.Invoke(formatted);
        await _kissServer.BroadcastFrameAsync(bytes, _lifetimeCts?.Token ?? CancellationToken.None).ConfigureAwait(false);
    }

    private static int ResolveFilterLength(int audioSampleRate)
    {
        for (var index = 0; index < ax25.AFSK1200Filters.sample_rates.Length; index++)
        {
            if (ax25.AFSK1200Filters.sample_rates[index] == audioSampleRate)
            {
                return ax25.AFSK1200Filters.bit_periods[index];
            }
        }

        throw new InvalidOperationException($"Unsupported AFSK audio sample rate: {audioSampleRate}.");
    }

    private static byte[] ToBytes(sbyte[] input)
    {
        var bytes = new byte[input.Length];
        Buffer.BlockCopy(input, 0, bytes, 0, input.Length);
        return bytes;
    }

    private static sbyte[] ToSBytes(byte[] input)
    {
        var bytes = new sbyte[input.Length];
        Buffer.BlockCopy(input, 0, bytes, 0, input.Length);
        return bytes;
    }

    private sealed class ReceivedPacketHandler : ax25.PacketHandler
    {
        private readonly HackrfKissTncService _service;

        public ReceivedPacketHandler(HackrfKissTncService service)
        {
            _service = service;
        }

        public void handlePacket(sbyte[] bytes)
        {
            _ = _service.ForwardReceivedPacketAsync(bytes);
        }
    }
}
