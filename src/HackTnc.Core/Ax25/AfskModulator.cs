using System;
using System.Collections.Generic;

namespace ax25
{
    /// <summary>
    /// Generic AFSK modulator for any baud rate and mark/space tone pair.
    /// </summary>
    public sealed class AfskModulator : IModulator
    {
        private readonly int _sampleRate;
        private readonly float _baudRate;
        private readonly float _phaseIncF0;
        private readonly float _phaseIncF1;
        private readonly float _phaseIncSymbol;

        private int _txDelay = 50;
        private int _txTail  = 0;

        private enum TxState { IDLE, PREAMBLE, DATA, TRAILER }
        private TxState _txState = TxState.IDLE;

        private sbyte[]? _txBytes;
        private int      _txIndex;
        private float    _txSymbolPhase;
        private float    _txDdsPhase;
        private float[]? _txSamples;
        private int      _txLastSymbol;
        private int      _txStuffCount;

        public int txDelayMs
        {
            set => _txDelay = value / 10;
            get => _txDelay * 10;
        }

        public int txTailMs
        {
            set => _txTail = value / 10;
            get => _txTail * 10;
        }

        public AfskModulator(int sampleRate, float baudRate, float markHz, float spaceHz)
        {
            _sampleRate     = sampleRate;
            _baudRate       = baudRate;
            _phaseIncF0     = (float)(2.0 * Math.PI * markHz  / sampleRate);
            _phaseIncF1     = (float)(2.0 * Math.PI * spaceHz / sampleRate);
            _phaseIncSymbol = (float)(2.0 * Math.PI * baudRate / sampleRate);
        }

        public void GetSamples(Packet packet, out float[] samples)
        {
            PrepareToTransmit(packet);
            var list   = new List<float>();
            var buffer = TxSamplesBuffer;
            int count;
            while ((count = Samples) > 0)
                for (int i = 0; i < count; i++)
                    list.Add(buffer[i]);
            samples = list.ToArray();
        }

        // ── Internal helpers ──────────────────────────────────────────────────

        private float[] TxSamplesBuffer
        {
            get
            {
                _txSamples ??= new float[(int)(Math.Ceiling(10.0 / _baudRate * _sampleRate) + 1)];
                return _txSamples;
            }
        }

        private void PrepareToTransmit(Packet packet)
        {
            if (_txState != TxState.IDLE) return;
            _txBytes = packet.bytesWithCRC();
            _txState = TxState.PREAMBLE;
            _txIndex = (int)Math.Ceiling(_txDelay * 0.01 / (8.0 / _baudRate));
            if (_txIndex < 1) _txIndex = 1;
            _txSymbolPhase = _txDdsPhase = 0.0f;
        }

        private int GenerateSymbolSamples(int symbol, float[] samples, int position)
        {
            int count = 0;
            while (_txSymbolPhase < 2.0 * Math.PI)
            {
                samples[position] = (float)Math.Sin(_txDdsPhase);
                _txDdsPhase += symbol == 0 ? _phaseIncF0 : _phaseIncF1;
                _txSymbolPhase += _phaseIncSymbol;
                if (_txDdsPhase > 2.0 * Math.PI)
                    _txDdsPhase -= (float)(2.0 * Math.PI);
                position++;
                count++;
            }
            _txSymbolPhase -= (float)(2.0 * Math.PI);
            return count;
        }

        private int ByteToSymbols(int bits, bool stuff)
        {
            int position = 0;
            for (int i = 0; i < 8; i++)
            {
                int bit = bits & 1;
                bits >>= 1;
                int symbol, count;
                if (bit == 0)
                {
                    symbol = _txLastSymbol == 0 ? 1 : 0;
                    count  = GenerateSymbolSamples(symbol, TxSamplesBuffer, position);
                    position += count;
                    if (stuff) _txStuffCount = 0;
                    _txLastSymbol = symbol;
                }
                else
                {
                    symbol = _txLastSymbol == 0 ? 0 : 1;
                    count  = GenerateSymbolSamples(symbol, TxSamplesBuffer, position);
                    position += count;
                    if (stuff) _txStuffCount++;
                    _txLastSymbol = symbol;

                    if (stuff && _txStuffCount == 5)
                    {
                        symbol = _txLastSymbol == 0 ? 1 : 0;
                        count  = GenerateSymbolSamples(symbol, TxSamplesBuffer, position);
                        position += count;
                        _txStuffCount = 0;
                        _txLastSymbol = symbol;
                    }
                }
            }
            return position;
        }

        private int Samples
        {
            get
            {
                int count;
                switch (_txState)
                {
                    case TxState.IDLE:
                        return 0;

                    case TxState.PREAMBLE:
                        count = ByteToSymbols(0x7E, false);
                        _txIndex--;
                        if (_txIndex == 0)
                        {
                            _txState     = TxState.DATA;
                            _txIndex     = 0;
                            _txStuffCount = 0;
                        }
                        break;

                    case TxState.DATA:
                        if (_txBytes == null) { _txState = TxState.IDLE; return 0; }
                        count = ByteToSymbols(_txBytes[_txIndex], true);
                        _txIndex++;
                        if (_txIndex == _txBytes.Length)
                        {
                            _txState = TxState.TRAILER;
                            if (_txTail <= 0)
                                _txIndex = 2;
                            else
                            {
                                _txIndex = (int)Math.Ceiling(_txTail * 0.01 / (8.0 / _baudRate));
                                if (_txIndex < 2) _txIndex = 2;
                            }
                        }
                        break;

                    case TxState.TRAILER:
                        count = ByteToSymbols(0x7E, false);
                        _txIndex--;
                        if (_txIndex == 0) _txState = TxState.IDLE;
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
