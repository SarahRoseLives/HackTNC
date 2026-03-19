namespace HackTnc.Core.Signal;

public sealed class FmAudioDecoder
{
    private readonly int _inputSampleRate;
    private readonly int _outputSampleRate;
    private readonly float _audioGain;
    private readonly double _inputSamplesPerOutput;
    private bool _haveIqSeed;
    private float _previousI;
    private float _previousQ;
    private bool _haveResamplerSeed;
    private float _previousAudio;
    private long _inputAudioIndex;
    private double _nextOutputAt;
    private float _dcEstimate;

    public FmAudioDecoder(int inputSampleRate, int outputSampleRate, double audioGain)
    {
        _inputSampleRate = inputSampleRate;
        _outputSampleRate = outputSampleRate;
        _audioGain = (float)audioGain;
        _inputSamplesPerOutput = inputSampleRate / (double)outputSampleRate;
        _nextOutputAt = _inputSamplesPerOutput;
    }

    public float[] Decode(ReadOnlySpan<byte> interleavedIq)
    {
        var output = new List<float>(Math.Max(1, interleavedIq.Length / 64));
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

            var real = (_previousI * i) + (_previousQ * q);
            var imag = (_previousI * q) - (_previousQ * i);
            var demodulated = MathF.Atan2(imag, real);
            _previousI = i;
            _previousQ = q;

            _dcEstimate = (0.995f * _dcEstimate) + (0.005f * demodulated);
            var audio = (demodulated - _dcEstimate) * _audioGain;
            AddResampledSample(audio, output);
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
            output.Add(Math.Clamp(interpolated, -1.0f, 1.0f));
            _nextOutputAt += _inputSamplesPerOutput;
        }

        _previousAudio = sample;
    }
}
