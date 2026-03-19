using System;

namespace ax25
{
    public class AFSK1200Demodulator
    {
        public bool ConsoleOut = false;

        private int sample_rate;
        private float decay;

        private float[] td_filter = Array.Empty<float>();
        private float[] cd_filter = Array.Empty<float>();

        private int rate_index;

        private float samples_per_bit;
        private float[] u1 = Array.Empty<float>();
        private float[] x = Array.Empty<float>();
        private float[] c0_real = Array.Empty<float>();
        private float[] c0_imag = Array.Empty<float>();
        private float[] c1_real = Array.Empty<float>();
        private float[] c1_imag = Array.Empty<float>();
        private float[] diff = Array.Empty<float>();
        private float previous_fdiff;
        private int last_transition;
        private int data, bitcount;

        private float phase_inc_f0, phase_inc_f1;
        private float phase_inc_symbol;

        private Packet? packet;
        private PacketHandler? handler;
        private sbyte[]? lastPacket;
        private int packetsDecoded = 0;

        private enum State
        {
            WAITING,
            JUST_SEEN_FLAG,
            DECODING
        }

        private State state = State.WAITING;

        private int filter_index;
        private int emphasis;

        private bool interpolate = false;
        private float interpolate_last;
        private bool interpolate_original;

        public PacketHandler OnPacket
        {
            set
            {
                handler = value;
            }
        }

        public int DecodeCount
        {
            get
            {
                return packetsDecoded;
            }
        }

        public sbyte[]? LastPacket
        {
            get
            {
                return lastPacket;
            }
        }

        public AFSK1200Demodulator(int sample_rate, int filter_length, PacketHandler? h)
        {
            Init(sample_rate, filter_length, 0, h);
        }

        public AFSK1200Demodulator(int sample_rate, int filter_length)
        {
            Init(sample_rate, filter_length, 0, null);
        }

        public AFSK1200Demodulator(int sample_rate, int filter_length, int emphasis)
        {
            Init(sample_rate, filter_length, emphasis, null);
        }

        public AFSK1200Demodulator(int sample_rate, int filter_length, int emphasis, PacketHandler h)
        {
            Init(sample_rate, filter_length, emphasis, h);
        }

        private void Init(int sample_rate, int filter_length, int emphasis, PacketHandler? h)
        {
            this.sample_rate = sample_rate;
            this.emphasis = emphasis;
            handler = h;
            decay = (float)(1.0 - Math.Exp(Math.Log(0.5) / sample_rate));
            if (ConsoleOut)
                Console.WriteLine("decay = {0:e}", decay);

            if (this.sample_rate == 8000)
            {
                interpolate = true;
                this.sample_rate = 16000;
            }

            samples_per_bit = sample_rate / 1200.0f;
            if (ConsoleOut)
                Console.WriteLine("samples per bit = {0}", samples_per_bit);

            for (rate_index = 0; rate_index < AFSK1200Filters.sample_rates.Length; rate_index++)
                if (AFSK1200Filters.sample_rates[rate_index] == sample_rate)
                    break;

            if (rate_index == AFSK1200Filters.sample_rates.Length)
                throw new Exception("Sample rate " + sample_rate + " not supported");

            float[][][] tdf;
            switch (emphasis)
            {
                case 0:
                    tdf = AFSK1200Filters.time_domain_filter_none;
                    break;
                case 6:
                    tdf = AFSK1200Filters.time_domain_filter_full;
                    break;
                default:
                    if (ConsoleOut)
                        Console.WriteLine("Filter for de-emphasis of {0}dB is not availabe, using 6dB", emphasis);
                    tdf = AFSK1200Filters.time_domain_filter_full;
                    break;
            }

            for (filter_index = 0; filter_index < tdf.Length; filter_index++)
            {
                if (ConsoleOut)
                    Console.WriteLine("Available filter length {0}", tdf[filter_index][rate_index].Length);
                if (filter_length == tdf[filter_index][rate_index].Length)
                {
                    if (ConsoleOut)
                        Console.WriteLine("Using filter length {0}", filter_length);
                    break;
                }
            }

            if (filter_index == tdf.Length)
            {
                filter_index = tdf.Length - 1;
                if (ConsoleOut)
                    Console.WriteLine(
                        "Filter length {0} not supported, using length {1}",
                        filter_length,
                        tdf[filter_index][rate_index].Length);
            }

            td_filter = tdf[filter_index][rate_index];
            cd_filter = AFSK1200Filters.corr_diff_filter[filter_index][rate_index];

            x = new float[td_filter.Length];
            u1 = new float[td_filter.Length];

            c0_real = new float[(int)Math.Floor(samples_per_bit)];
            c0_imag = new float[(int)Math.Floor(samples_per_bit)];
            c1_real = new float[(int)Math.Floor(samples_per_bit)];
            c1_imag = new float[(int)Math.Floor(samples_per_bit)];

            diff = new float[cd_filter.Length];

            phase_inc_f0 = (float)(2.0 * Math.PI * 1200.0 / sample_rate);
            phase_inc_f1 = (float)(2.0 * Math.PI * 2200.0 / sample_rate);
            phase_inc_symbol = (float)(2.0 * Math.PI * 1200.0 / sample_rate);
        }

        private float sum(float[] values, int index)
        {
            var total = 0.0f;
            for (var i = 0; i < values.Length; i++)
            {
                total += values[index--];
                if (index == -1)
                    index = values.Length - 1;
            }

            return total;
        }

        private int j_td;
        private int j_cd;
        private int j_corr;
        private float phase_f0, phase_f1;
        private int t;
        private int flag_count = 0;
        private bool flag_separator_seen = false;

        public void AddSamples(double[] samples, int count)
        {
            var converted = new float[count];
            for (var i = 0; i < count; i++)
                converted[i] = (float)samples[i];
            AddSamples(converted, count);
        }

        public void AddSamples(float[] samples, int count)
        {
            var i = 0;
            while (i < count)
            {
                float sample;
                if (interpolate)
                {
                    if (interpolate_original)
                    {
                        sample = samples[i];
                        interpolate_last = sample;
                        interpolate_original = false;
                        i++;
                    }
                    else
                    {
                        sample = 0.5f * (samples[i] + interpolate_last);
                        interpolate_original = true;
                    }
                }
                else
                {
                    sample = samples[i];
                    i++;
                }

                u1[j_td] = sample;
                x[j_td] = Filter.filter(u1, j_td, td_filter);

                c0_real[j_corr] = x[j_td] * (float)Math.Cos(phase_f0);
                c0_imag[j_corr] = x[j_td] * (float)Math.Sin(phase_f0);

                c1_real[j_corr] = x[j_td] * (float)Math.Cos(phase_f1);
                c1_imag[j_corr] = x[j_td] * (float)Math.Sin(phase_f1);

                phase_f0 += phase_inc_f0;
                if (phase_f0 > 2.0 * Math.PI)
                    phase_f0 -= (float)(2.0 * Math.PI);
                phase_f1 += phase_inc_f1;
                if (phase_f1 > 2.0 * Math.PI)
                    phase_f1 -= (float)(2.0 * Math.PI);

                var cr = sum(c0_real, j_corr);
                var ci = sum(c0_imag, j_corr);
                var c0 = (float)Math.Sqrt(cr * cr + ci * ci);

                cr = sum(c1_real, j_corr);
                ci = sum(c1_imag, j_corr);
                var c1 = (float)Math.Sqrt(cr * cr + ci * ci);

                diff[j_cd] = c0 - c1;
                var fdiff = Filter.filter(diff, j_cd, cd_filter);

                if (previous_fdiff * fdiff < 0 || previous_fdiff == 0)
                {
                    var period = t - last_transition;
                    last_transition = t;

                    var bits = (int)Math.Round((double)period / samples_per_bit);

                    if (bits == 0 || bits > 7)
                    {
                        state = State.WAITING;
                        flag_count = 0;
                    }
                    else
                    {
                        if (bits == 7)
                        {
                            flag_count++;
                            flag_separator_seen = false;

                            data = 0;
                            bitcount = 0;
                            switch (state)
                            {
                                case State.WAITING:
                                    state = State.JUST_SEEN_FLAG;
                                    break;
                                case State.JUST_SEEN_FLAG:
                                    break;
                                case State.DECODING:
                                    if (packet != null && packet.terminate())
                                    {
                                        packetsDecoded++;
                                        lastPacket = packet.bytesWithoutCRC();
                                        if (handler != null)
                                            handler.handlePacket(packet.bytesWithoutCRC());
                                    }
                                    packet = null;
                                    state = State.JUST_SEEN_FLAG;
                                    break;
                            }
                        }
                        else
                        {
                            switch (state)
                            {
                                case State.WAITING:
                                    break;
                                case State.JUST_SEEN_FLAG:
                                    state = State.DECODING;
                                    break;
                                case State.DECODING:
                                    break;
                            }

                            if (state == State.DECODING)
                            {
                                if (bits != 1)
                                    flag_count = 0;
                                else
                                {
                                    if (flag_count > 0 && !flag_separator_seen)
                                        flag_separator_seen = true;
                                    else
                                        flag_count = 0;
                                }

                                for (var k = 0; k < bits - 1; k++)
                                {
                                    bitcount++;
                                    data >>= 1;
                                    data += 128;
                                    if (bitcount == 8)
                                    {
                                        packet ??= new Packet();
                                        if (!packet.addByte((sbyte)data))
                                        {
                                            state = State.WAITING;
                                        }
                                        data = 0;
                                        bitcount = 0;
                                    }
                                }

                                if (bits - 1 != 5)
                                {
                                    bitcount++;
                                    data >>= 1;
                                    if (bitcount == 8)
                                    {
                                        packet ??= new Packet();
                                        if (!packet.addByte((sbyte)data))
                                        {
                                            state = State.WAITING;
                                        }
                                        data = 0;
                                        bitcount = 0;
                                    }
                                }
                            }
                        }
                    }
                }

                previous_fdiff = fdiff;

                t++;

                j_td++;
                if (j_td == td_filter.Length)
                    j_td = 0;

                j_cd++;
                if (j_cd == cd_filter.Length)
                    j_cd = 0;

                j_corr++;
                if (j_corr == c0_real.Length)
                    j_corr = 0;
            }
        }
    }
}
