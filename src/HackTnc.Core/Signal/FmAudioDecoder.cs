namespace HackTnc.Core.Signal;

public sealed class FmAudioDecoder
{
    private readonly float _audioGain;
    private readonly double _inputSamplesPerOutput;

    // IQ FM discriminator state
    private bool _haveIqSeed;
    private float _previousI;
    private float _previousQ;

    // Slow IIR DC blocker
    private float _dcEstimate;

    // Anti-aliasing: moving-average (boxcar) over one decimation window.
    // Provides a sinc-shaped low-pass that eliminates most energy above
    // outputRate/2 before the resampler, preventing aliasing noise.
    private readonly float[] _aaBuffer;
    private int _aaIndex;
    private float _aaSum;
    private int _aaFill;

    // Linear interpolation resampler state
    private bool _haveResamplerSeed;
    private float _previousAudio;
    private long _inputAudioIndex;
    private double _nextOutputAt;

    public FmAudioDecoder(int inputSampleRate, int outputSampleRate, double audioGain)
    {
        _audioGain = (float)audioGain;
        _inputSamplesPerOutput = inputSampleRate / (double)outputSampleRate;
        _nextOutputAt = _inputSamplesPerOutput;

        var aaLength = Math.Max(1, inputSampleRate / outputSampleRate);
        _aaBuffer = new float[aaLength];
    }

    public float[] Decode(ReadOnlySpan<byte> interleavedIq)
    {
        var output = new List<float>(Math.Max(1, interleavedIq.Length / (_aaBuffer.Length * 2)));

        for (var index = 0; index + 1 < interleavedIq.Length; index += 2)
        {
            var i = ((sbyte)interleavedIq[index]) / 128f;
            var q = ((sbyte)interleavedIq[index + 1]) / 128f;

            if (!_haveIqSeed)
            {
                _previousI = i;
                _previousQ = q;
                _haveIqSeed = true;
                continue;
            }

            // FM discriminator: arg(prev* . curr) = instantaneous phase increment
            var real = (_previousI * i) + (_previousQ * q);
            var imag = (_previousI * q) - (_previousQ * i);
            var demodulated = MathF.Atan2(imag, real);
            _previousI = i;
            _previousQ = q;

            // DC removal (removes carrier offset and HackRF DC spike)
            _dcEstimate = (0.995f * _dcEstimate) + (0.005f * demodulated);
            var audio = (demodulated - _dcEstimate) * _audioGain;

            // Anti-aliasing: running mean over the decimation window.
            // For a 2 Msps→48 kHz downconversion this window is ~41 samples,
            // attenuating the noise triangle from the FM discriminator before
            // the linear interpolation resampler sees it.
            _aaSum -= _aaBuffer[_aaIndex];
            _aaBuffer[_aaIndex] = audio;
            _aaSum += audio;
            _aaIndex = (_aaIndex + 1) % _aaBuffer.Length;
            if (_aaFill < _aaBuffer.Length)
            {
                _aaFill++;
            }

            var filtered = _aaSum / _aaFill;
            AddResampledSample(filtered, output);
        }

        return output.ToArray();
    }

    private void AddResampledSample(float sample, List<float> output)
    {
        if (!_haveResamplerSeed)
        {
            _previousAudio = sample;
            _haveResamplerSeed = true;
            _inputAudioIndex = 0;
            return;
        }

        _inputAudioIndex++;
        while (_nextOutputAt <= _inputAudioIndex)
        {
            var fraction = (float)(_nextOutputAt - (_inputAudioIndex - 1));
            var interpolated = _previousAudio + ((sample - _previousAudio) * fraction);
            output.Add(interpolated);
            _nextOutputAt += _inputSamplesPerOutput;
        }

        _previousAudio = sample;
    }
}
