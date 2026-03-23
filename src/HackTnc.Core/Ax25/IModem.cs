namespace ax25
{
    public interface IModulator
    {
        void GetSamples(Packet packet, out float[] samples);
        int txDelayMs { get; set; }
        int txTailMs { get; set; }
    }

    public interface IDemodulator
    {
        void AddSamples(float[] samples, int count);
    }
}
