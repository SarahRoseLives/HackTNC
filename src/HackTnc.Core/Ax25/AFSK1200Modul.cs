using System;
using System.Collections.Generic;

namespace ax25
{
    public class AFSK1200Modulator : IModulator
    {
        public bool ConsoleOut = false;

        private float phase_inc_f0, phase_inc_f1;
        private float phase_inc_symbol;
        private int sample_rate;

        public AFSK1200Modulator(int sample_rate)
        {
            this.sample_rate = sample_rate;
            phase_inc_f0 = (float)(2.0 * Math.PI * 1200.0 / sample_rate);
            phase_inc_f1 = (float)(2.0 * Math.PI * 2200.0 / sample_rate);
            phase_inc_symbol = (float)(2.0 * Math.PI * 1200.0 / sample_rate);
        }

        public int txDelayMs
        {
            set { tx_delay = value / 10; }
            get { return tx_delay * 10; }
        }

        public int txTailMs
        {
            set { tx_tail = value / 10; }
            get { return tx_tail * 10; }
        }

        private enum TxState
        {
            IDLE,
            PREAMBLE,
            DATA,
            TRAILER
        }

        private TxState tx_state = TxState.IDLE;
        private sbyte[]? tx_bytes;
        private int tx_index;
        private int tx_delay = 50;
        private int tx_tail = 0;
        private float tx_symbol_phase, tx_dds_phase;

        private float[]? tx_samples;
        private int tx_last_symbol;
        private int tx_stuff_count;

        private void prepareToTransmit(Packet packet)
        {
            if (tx_state != TxState.IDLE)
            {
                if (ConsoleOut)
                    Console.WriteLine("Warning: trying to trasmit while Afsk1200 modulator is busy, discarding");
                return;
            }

            tx_bytes = packet.bytesWithCRC();
            tx_state = TxState.PREAMBLE;
            tx_index = (int)Math.Ceiling(tx_delay * 0.01 / (8.0 / 1200.0));
            if (tx_index < 1)
                tx_index = 1;
            tx_symbol_phase = tx_dds_phase = 0.0f;
        }

        public void GetSamples(Packet packet, out float[] samples)
        {
            prepareToTransmit(packet);

            var list = new List<float>();
            int count;
            var buffer = txSamplesBuffer;
            while ((count = this.samples) > 0)
                for (var i = 0; i < count; i++)
                    list.Add(buffer[i]);

            samples = list.ToArray();
        }

        private float[] txSamplesBuffer
        {
            get
            {
                tx_samples ??= new float[(int)(Math.Ceiling((10.0 / 1200.0) * sample_rate) + 1)];
                return tx_samples;
            }
        }

        private int generateSymbolSamples(int symbol, float[] samples, int position)
        {
            var count = 0;
            while (tx_symbol_phase < 2.0 * Math.PI)
            {
                samples[position] = (float)Math.Sin(tx_dds_phase);

                if (symbol == 0)
                    tx_dds_phase += phase_inc_f0;
                else
                    tx_dds_phase += phase_inc_f1;

                tx_symbol_phase += phase_inc_symbol;
                if (tx_dds_phase > 2.0 * Math.PI)
                    tx_dds_phase -= (float)(2.0 * Math.PI);

                position++;
                count++;
            }

            tx_symbol_phase -= (float)(2.0 * Math.PI);
            return count;
        }

        private int byteToSymbols(int bits, bool stuff)
        {
            var position = 0;

            for (var i = 0; i < 8; i++)
            {
                var bit = bits & 1;
                bits >>= 1;

                int symbol;
                int count;
                if (bit == 0)
                {
                    symbol = tx_last_symbol == 0 ? 1 : 0;
                    count = generateSymbolSamples(symbol, txSamplesBuffer, position);
                    position += count;
                    if (stuff)
                        tx_stuff_count = 0;
                    tx_last_symbol = symbol;
                }
                else
                {
                    symbol = tx_last_symbol == 0 ? 0 : 1;
                    count = generateSymbolSamples(symbol, txSamplesBuffer, position);
                    position += count;

                    if (stuff)
                        tx_stuff_count++;
                    tx_last_symbol = symbol;

                    if (stuff && tx_stuff_count == 5)
                    {
                        symbol = tx_last_symbol == 0 ? 1 : 0;
                        count = generateSymbolSamples(symbol, txSamplesBuffer, position);
                        position += count;
                        tx_stuff_count = 0;
                        tx_last_symbol = symbol;
                    }
                }
            }

            return position;
        }

        private int samples
        {
            get
            {
                int count;
                switch (tx_state)
                {
                    case TxState.IDLE:
                        return 0;
                    case TxState.PREAMBLE:
                        count = byteToSymbols(0x7E, false);
                        tx_index--;
                        if (tx_index == 0)
                        {
                            tx_state = TxState.DATA;
                            tx_index = 0;
                            tx_stuff_count = 0;
                        }
                        break;
                    case TxState.DATA:
                        if (tx_bytes == null)
                        {
                            tx_state = TxState.IDLE;
                            return 0;
                        }

                        count = byteToSymbols(tx_bytes[tx_index], true);
                        tx_index++;
                        if (tx_index == tx_bytes.Length)
                        {
                            tx_state = TxState.TRAILER;
                            if (tx_tail <= 0)
                                tx_index = 2;
                            else
                            {
                                tx_index = (int)Math.Ceiling(tx_tail * 0.01 / (8.0 / 1200.0));
                                if (tx_tail < 2)
                                    tx_tail = 2;
                            }
                        }
                        break;
                    case TxState.TRAILER:
                        count = byteToSymbols(0x7E, false);
                        tx_index--;
                        if (tx_index == 0)
                            tx_state = TxState.IDLE;
                        break;
                    default:
                        count = -1;
                        break;
                }

                return count;
            }
        }
    }
}
