namespace HackTnc.Core.Configuration;

public sealed class TncOptions
{
    public string BindAddress { get; init; } = "127.0.0.1";

    public int KissPort { get; init; } = 8001;

    public long FrequencyHz { get; init; } = 144_390_000;

    public int SampleRateHz { get; init; } = 2_000_000;

    public int AudioSampleRate { get; init; } = 48_000;

    public int BasebandFilterBandwidthHz { get; init; } = 1_750_000;

    public int LnaGainDb { get; init; } = 24;

    public int VgaGainDb { get; init; } = 24;

    public int TxVgaGainDb { get; init; } = 20;

    public bool AmpEnable { get; init; }

    public bool AntennaPowerEnable { get; init; }

    public int FmDeviationHz { get; init; } = 3_000;

    public double RxAudioGain { get; init; } = 0.0;

    public double TxAudioGain { get; init; } = 1.0;

    public int TxDelayMs { get; init; } = 300;

    public int TxTailMs { get; init; } = 50;

    public string? SerialSuffix { get; init; }

    public string? HackrfLibraryPath { get; init; }
}
