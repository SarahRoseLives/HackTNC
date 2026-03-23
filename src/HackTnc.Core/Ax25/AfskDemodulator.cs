using System;

namespace ax25
{
    /// <summary>
    /// Generic AFSK demodulator for any baud rate and mark/space tone pair.
    /// FIR filters are computed at construction time using the Hamming window method.
    /// </summary>
    public sealed class AfskDemodulator : IDemodulator
    {
        private readonly float[] _tdFilter;
        private readonly float[] _cdFilter;
        private readonly float _samplesPerBit;
        private readonly float _phaseIncF0;
        private readonly float _phaseIncF1;
        private readonly float _phaseIncSymbol;
        private readonly float[] _u1;
        private readonly float[] _x;
        private readonly float[] _c0r;
        private readonly float[] _c0i;
        private readonly float[] _c1r;
        private readonly float[] _c1i;
        private readonly float[] _diff;

        private PacketHandler? _handler;
        private Packet? _packet;
        private sbyte[]? _lastPacket;
        private int _packetsDecoded;
        private float _prevFdiff;
        private int _lastTransition;
        private int _data;
        private int _bitcount;
        private float _phF0;
        private float _phF1;
        private int _t;
        private int _jtd;
        private int _jcd;
        private int _jcorr;
        private int _flagCount;
        private bool _flagSepSeen;

        private enum State { WAITING, JUST_SEEN_FLAG, DECODING }
        private State _state = State.WAITING;

        public PacketHandler? OnPacket { set => _handler = value; }
        public int DecodeCount => _packetsDecoded;
        public sbyte[]? LastPacket => _lastPacket;

        public AfskDemodulator(int sampleRate, float baudRate, float markHz, float spaceHz, PacketHandler? handler = null)
        {
            _handler = handler;
            _samplesPerBit = sampleRate / baudRate;

            // Choose filter length: one symbol period, clamped and forced odd for symmetric FIR
            int len = (int)Math.Floor(_samplesPerBit);
            if (len < 9)   len = 9;
            if (len > 255) len = 255;
            if ((len & 1) == 0) len--;

            float spacing = Math.Abs(spaceHz - markHz);
            _tdFilter = BandpassFir(len,
                Math.Min(markHz, spaceHz) - spacing,
                Math.Max(markHz, spaceHz) + spacing,
                sampleRate);
            _cdFilter = LowpassFir(len, baudRate * 0.75f, sampleRate);

            _u1   = new float[len];
            _x    = new float[len];
            _diff = new float[len];

            int corrLen = (int)Math.Floor(_samplesPerBit);
            _c0r = new float[corrLen];
            _c0i = new float[corrLen];
            _c1r = new float[corrLen];
            _c1i = new float[corrLen];

            _phaseIncF0     = (float)(2.0 * Math.PI * markHz  / sampleRate);
            _phaseIncF1     = (float)(2.0 * Math.PI * spaceHz / sampleRate);
            _phaseIncSymbol = (float)(2.0 * Math.PI * baudRate / sampleRate);
        }

        public void AddSamples(float[] samples, int count)
        {
            for (int i = 0; i < count; i++)
            {
                float sample = samples[i];

                _u1[_jtd] = sample;
                _x[_jtd]  = Filter.filter(_u1, _jtd, _tdFilter);

                _c0r[_jcorr] = _x[_jtd] * (float)Math.Cos(_phF0);
                _c0i[_jcorr] = _x[_jtd] * (float)Math.Sin(_phF0);
                _c1r[_jcorr] = _x[_jtd] * (float)Math.Cos(_phF1);
                _c1i[_jcorr] = _x[_jtd] * (float)Math.Sin(_phF1);

                _phF0 += _phaseIncF0;
                if (_phF0 > 2.0f * MathF.PI) _phF0 -= 2.0f * MathF.PI;
                _phF1 += _phaseIncF1;
                if (_phF1 > 2.0f * MathF.PI) _phF1 -= 2.0f * MathF.PI;

                float cr = Sum(_c0r, _jcorr);
                float ci = Sum(_c0i, _jcorr);
                float c0 = (float)Math.Sqrt(cr * cr + ci * ci);

                cr = Sum(_c1r, _jcorr);
                ci = Sum(_c1i, _jcorr);
                float c1 = (float)Math.Sqrt(cr * cr + ci * ci);

                _diff[_jcd] = c0 - c1;
                float fdiff = Filter.filter(_diff, _jcd, _cdFilter);

                if (_prevFdiff * fdiff < 0 || _prevFdiff == 0)
                {
                    int period = _t - _lastTransition;
                    _lastTransition = _t;
                    int bits = (int)Math.Round((double)period / _samplesPerBit);

                    if (bits == 0 || bits > 7)
                    {
                        _state = State.WAITING;
                        _flagCount = 0;
                    }
                    else if (bits == 7)
                    {
                        _flagCount++;
                        _flagSepSeen = false;
                        _data = 0;
                        _bitcount = 0;
                        switch (_state)
                        {
                            case State.WAITING:
                                _state = State.JUST_SEEN_FLAG;
                                break;
                            case State.DECODING:
                                if (_packet != null && _packet.terminate())
                                {
                                    _packetsDecoded++;
                                    _lastPacket = _packet.bytesWithoutCRC();
                                    _handler?.handlePacket(_packet.bytesWithoutCRC());
                                }
                                _packet = null;
                                _state = State.JUST_SEEN_FLAG;
                                break;
                        }
                    }
                    else
                    {
                        if (_state == State.JUST_SEEN_FLAG)
                            _state = State.DECODING;

                        if (_state == State.DECODING)
                        {
                            if (bits != 1)
                                _flagCount = 0;
                            else
                            {
                                if (_flagCount > 0 && !_flagSepSeen)
                                    _flagSepSeen = true;
                                else
                                    _flagCount = 0;
                            }

                            for (int k = 0; k < bits - 1; k++)
                            {
                                _bitcount++;
                                _data >>= 1;
                                _data += 128;
                                if (_bitcount == 8)
                                {
                                    _packet ??= new Packet();
                                    if (!_packet.addByte((sbyte)_data))
                                        _state = State.WAITING;
                                    _data = 0;
                                    _bitcount = 0;
                                }
                            }

                            if (bits - 1 != 5)
                            {
                                _bitcount++;
                                _data >>= 1;
                                if (_bitcount == 8)
                                {
                                    _packet ??= new Packet();
                                    if (!_packet.addByte((sbyte)_data))
                                        _state = State.WAITING;
                                    _data = 0;
                                    _bitcount = 0;
                                }
                            }
                        }
                    }
                }

                _prevFdiff = fdiff;
                _t++;

                if (++_jtd  == _tdFilter.Length) _jtd  = 0;
                if (++_jcd  == _cdFilter.Length) _jcd  = 0;
                if (++_jcorr == _c0r.Length)     _jcorr = 0;
            }
        }

        // ── FIR helpers ───────────────────────────────────────────────────────

        private static float Sum(float[] values, int index)
        {
            float total = 0;
            for (int i = 0; i < values.Length; i++)
            {
                total += values[index--];
                if (index == -1) index = values.Length - 1;
            }
            return total;
        }

        /// <summary>Hamming-windowed bandpass FIR between loHz and hiHz.</summary>
        private static float[] BandpassFir(int length, float loHz, float hiHz, float sampleRate)
        {
            int m = length - 1;
            var h = new float[length];
            double fl = loHz / sampleRate;
            double fh = hiHz / sampleRate;
            for (int n = 0; n < length; n++)
            {
                double t = n - m / 2.0;
                double sl = t == 0 ? 2 * fl : Math.Sin(2 * Math.PI * fl * t) / (Math.PI * t);
                double sh = t == 0 ? 2 * fh : Math.Sin(2 * Math.PI * fh * t) / (Math.PI * t);
                double w  = 0.54 - 0.46 * Math.Cos(2.0 * Math.PI * n / m);
                h[n] = (float)((sh - sl) * w);
            }
            // Normalize by peak absolute value so signal amplitude is preserved
            float peak = 0;
            foreach (var v in h) if (Math.Abs(v) > peak) peak = Math.Abs(v);
            if (peak > 0) for (int n = 0; n < length; n++) h[n] /= peak;
            return h;
        }

        /// <summary>Hamming-windowed low-pass FIR with unity DC gain.</summary>
        private static float[] LowpassFir(int length, float cutoffHz, float sampleRate)
        {
            int m = length - 1;
            var h = new float[length];
            double fc = cutoffHz / sampleRate;
            for (int n = 0; n < length; n++)
            {
                double t = n - m / 2.0;
                double s = t == 0 ? 2 * fc : Math.Sin(2 * Math.PI * fc * t) / (Math.PI * t);
                double w = 0.54 - 0.46 * Math.Cos(2.0 * Math.PI * n / m);
                h[n] = (float)(s * w);
            }
            float sum = 0;
            foreach (var v in h) sum += v;
            if (sum > 0) for (int n = 0; n < length; n++) h[n] /= sum;
            return h;
        }
    }
}
