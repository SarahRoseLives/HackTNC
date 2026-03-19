using System.Globalization;
using HackTnc.Core.Configuration;
using HackTnc.Core.Services;

var options = ParseOptions(args);
if (options is null)
{
    return 0;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

await using var service = new HackrfKissTncService(options, Log);

Log("Starting HackRF KISS/TCP TNC");
Log($"Firmware: {service.FirmwareVersion}");
Log($"Frequency: {options.FrequencyHz} Hz | KISS: {options.BindAddress}:{options.KissPort}");

await service.StartAsync(cts.Token);

try
{
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException)
{
    Log("Shutdown requested.");
}

return 0;

static void Log(string message)
{
    Console.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss}] {message}");
}

static TncOptions? ParseOptions(string[] args)
{
    if (args.Any(argument => argument is "--help" or "-h" or "/?"))
    {
        PrintHelp();
        return null;
    }

    var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    for (var index = 0; index < args.Length; index++)
    {
        var argument = args[index];
        if (!argument.StartsWith('-'))
        {
            throw new ArgumentException($"Unexpected argument '{argument}'.");
        }

        if (index + 1 < args.Length && !args[index + 1].StartsWith('-'))
        {
            values[argument] = args[index + 1];
            index++;
        }
        else
        {
            flags.Add(argument);
        }
    }

    return new TncOptions
    {
        BindAddress = GetString(values, "--bind", "127.0.0.1"),
        KissPort = GetInt(values, "--kiss-port", 8001),
        FrequencyHz = GetFrequency(values, "--frequency", 144_390_000),
        SampleRateHz = GetInt(values, "--sample-rate", 2_000_000),
        AudioSampleRate = GetInt(values, "--audio-rate", 48_000),
        BasebandFilterBandwidthHz = GetInt(values, "--baseband-filter", 1_750_000),
        LnaGainDb = GetInt(values, "--lna-gain", 24),
        VgaGainDb = GetInt(values, "--vga-gain", 24),
        TxVgaGainDb = GetInt(values, "--tx-vga-gain", 20),
        FmDeviationHz = GetInt(values, "--fm-deviation", 3000),
        TxDelayMs = GetInt(values, "--tx-delay", 300),
        TxTailMs = GetInt(values, "--tx-tail", 50),
        RxAudioGain = GetDouble(values, "--rx-audio-gain", 1.0),
        TxAudioGain = GetDouble(values, "--tx-audio-gain", 1.0),
        SerialSuffix = GetOptionalString(values, "--serial"),
        HackrfLibraryPath = GetOptionalString(values, "--hackrf-dll"),
        AmpEnable = flags.Contains("--amp"),
        AntennaPowerEnable = flags.Contains("--antenna-power")
    };
}

static string GetString(IDictionary<string, string> values, string key, string defaultValue)
{
    return values.TryGetValue(key, out var value) ? value : defaultValue;
}

static string? GetOptionalString(IDictionary<string, string> values, string key)
{
    return values.TryGetValue(key, out var value) ? value : null;
}

static int GetInt(IDictionary<string, string> values, string key, int defaultValue)
{
    return values.TryGetValue(key, out var value)
        ? int.Parse(value, CultureInfo.InvariantCulture)
        : defaultValue;
}

static long GetFrequency(IDictionary<string, string> values, string key, long defaultValue)
{
    if (!values.TryGetValue(key, out var value))
    {
        return defaultValue;
    }

    var normalized = value.Trim();
    var multiplier = 1.0;

    if (normalized.EndsWith("mhz", StringComparison.OrdinalIgnoreCase))
    {
        multiplier = 1_000_000.0;
        normalized = normalized[..^3];
    }
    else if (normalized.EndsWith("m", StringComparison.OrdinalIgnoreCase))
    {
        multiplier = 1_000_000.0;
        normalized = normalized[..^1];
    }
    else if (normalized.EndsWith("khz", StringComparison.OrdinalIgnoreCase))
    {
        multiplier = 1_000.0;
        normalized = normalized[..^3];
    }
    else if (normalized.EndsWith("k", StringComparison.OrdinalIgnoreCase))
    {
        multiplier = 1_000.0;
        normalized = normalized[..^1];
    }

    var parsed = double.Parse(normalized, CultureInfo.InvariantCulture);
    if (multiplier == 1.0 && parsed > 0 && parsed < 10_000_000)
    {
        multiplier = 1_000.0;
    }

    return checked((long)Math.Round(parsed * multiplier));
}

static double GetDouble(IDictionary<string, string> values, string key, double defaultValue)
{
    return values.TryGetValue(key, out var value)
        ? double.Parse(value, CultureInfo.InvariantCulture)
        : defaultValue;
}

static void PrintHelp()
{
    Console.WriteLine("HackRF KISS/TCP TNC for AFSK1200 AX.25");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --frequency <hz|kHz|MHz>   RF center frequency. Examples: 144390000, 144390, 144.390M");
    Console.WriteLine("  --bind <address>           KISS/TCP bind address. Default: 127.0.0.1");
    Console.WriteLine("  --kiss-port <port>         KISS/TCP listen port. Default: 8001");
    Console.WriteLine("  --sample-rate <hz>         HackRF IQ sample rate. Default: 2000000");
    Console.WriteLine("  --audio-rate <hz>          AFSK audio sample rate. Default: 48000");
    Console.WriteLine("  --baseband-filter <hz>     HackRF baseband filter. Default: 1750000");
    Console.WriteLine("  --lna-gain <db>            RX LNA gain. Default: 24");
    Console.WriteLine("  --vga-gain <db>            RX VGA gain. Default: 24");
    Console.WriteLine("  --tx-vga-gain <db>         TX VGA gain. Default: 20");
    Console.WriteLine("  --fm-deviation <hz>        FM deviation. Default: 3000");
    Console.WriteLine("  --tx-delay <ms>            AX.25 TX delay flags. Default: 300");
    Console.WriteLine("  --tx-tail <ms>             AX.25 TX tail flags. Default: 50");
    Console.WriteLine("  --rx-audio-gain <gain>     RX discriminator gain. Default: 1.0");
    Console.WriteLine("  --tx-audio-gain <gain>     TX audio gain. Default: 1.0");
    Console.WriteLine("  --serial <suffix>          HackRF serial suffix to open.");
    Console.WriteLine("  --hackrf-dll <path>        Explicit path to hackrf.dll.");
    Console.WriteLine("  --amp                      Enable HackRF RF amplifier.");
    Console.WriteLine("  --antenna-power            Enable HackRF antenna bias tee.");
}
