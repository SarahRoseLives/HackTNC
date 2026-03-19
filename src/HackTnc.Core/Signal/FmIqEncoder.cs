namespace HackTnc.Core.Signal;

public sealed class FmIqEncoder
{
    private readonly int _outputSampleRate;
    private readonly int _audioSampleRate;
    private readonly double _deviationScale;
    private readonly float _audioGain;
    private double _phase;

    public FmIqEncoder(int outputSampleRate, int audioSampleRate, int fmDeviationHz, double audioGain)
    {
        _outputSampleRate = outputSampleRate;
        _audioSampleRate = audioSampleRate;
        _deviationScale = (2.0 * Math.PI * fmDeviationHz) / outputSampleRate;
        _audioGain = (float)audioGain;
    }

    public byte[] Encode(ReadOnlySpan<float> audioSamples)
    {
        if (audioSamples.IsEmpty)
        {
            return Array.Empty<byte>();
        }

        var sampleCount = (int)Math.Ceiling(audioSamples.Length * (_outputSampleRate / (double)_audioSampleRate));
        var output = new byte[sampleCount * 2];

        for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            var sourcePosition = sampleIndex * (_audioSampleRate / (double)_outputSampleRate);
            var sourceIndex = Math.Min((int)sourcePosition, audioSamples.Length - 1);
            var nextIndex = Math.Min(sourceIndex + 1, audioSamples.Length - 1);
            var fraction = sourcePosition - sourceIndex;

            var audio = audioSamples[sourceIndex] + ((audioSamples[nextIndex] - audioSamples[sourceIndex]) * (float)fraction);
            audio = Math.Clamp(audio * _audioGain, -1.0f, 1.0f);

            _phase += _deviationScale * audio;
            if (_phase > Math.PI)
            {
                _phase -= 2.0 * Math.PI;
            }
            else if (_phase < -Math.PI)
            {
                _phase += 2.0 * Math.PI;
            }

            var i = (sbyte)Math.Clamp((int)Math.Round(Math.Cos(_phase) * 127.0), -127, 127);
            var q = (sbyte)Math.Clamp((int)Math.Round(Math.Sin(_phase) * 127.0), -127, 127);

            output[sampleIndex * 2] = unchecked((byte)i);
            output[(sampleIndex * 2) + 1] = unchecked((byte)q);
        }

        return output;
    }
}
