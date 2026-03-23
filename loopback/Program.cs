using System.Collections.Concurrent;
using System.Diagnostics;
using LoopbackTest;

// ── Default configuration ────────────────────────────────────────────────────
string host      = "127.0.0.1";
int    portA     = 8000;
int    portB     = 8001;
int    count     = 10;
int    intervalMs   = 500;
int    timeoutMs    = 5000;
bool   verbose   = false;

// TNC-A and TNC-B test callsigns (non-real, clearly identifiable in logs)
const string CallA = "LBTEST"; const int SsidA = 0;
const string CallB = "LBTEST"; const int SsidB = 1;

// ── Argument parsing ─────────────────────────────────────────────────────────
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--host":     host       = args[++i];              break;
        case "--port-a":   portA      = int.Parse(args[++i]);   break;
        case "--port-b":   portB      = int.Parse(args[++i]);   break;
        case "--count":    count      = int.Parse(args[++i]);   break;
        case "--interval": intervalMs = int.Parse(args[++i]);   break;
        case "--timeout":  timeoutMs  = int.Parse(args[++i]);   break;
        case "--verbose":  verbose    = true;                   break;
        case "-h":
        case "--help":
            PrintHelp();
            return 0;
        default:
            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            PrintHelp();
            return 1;
    }
}

// ── Banner ────────────────────────────────────────────────────────────────────
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("╔══════════════════════════════════════╗");
Console.WriteLine("║     HackTNC KISS Loopback Tester     ║");
Console.WriteLine("╚══════════════════════════════════════╝");
Console.ResetColor();
Console.WriteLine();

// ── Connect ───────────────────────────────────────────────────────────────────
KissClient? tncA = TryConnect("TNC-A", host, portA);
if (tncA == null) return 1;

KissClient? tncB = TryConnect("TNC-B", host, portB);
if (tncB == null) { tncA.Dispose(); return 1; }

// ── Test header ───────────────────────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine($"  Path:     {CallA}-{SsidA} (TNC-A:{portA})  ──►  {CallB}-{SsidB} (TNC-B:{portB})  ──►  back");
Console.WriteLine($"  Packets:  {count}   Interval: {intervalMs} ms   Timeout: {timeoutMs} ms");
Console.WriteLine();

// ── Shared state ─────────────────────────────────────────────────────────────
using var cts     = new CancellationTokenSource();
var pending       = new ConcurrentDictionary<int, (TaskCompletionSource<long> Tcs, long SentAt)>();
var rtts          = new List<double>();
int received      = 0;
int width         = count.ToString().Length;

// ── TNC-B echo task ───────────────────────────────────────────────────────────
// Receives frames on TNC-B; if it's a PING addressed to us, sends a PONG reply.
var echoTask = Task.Run(async () =>
{
    try
    {
        while (!cts.Token.IsCancellationRequested)
        {
            byte[]? raw = await tncB.ReceiveAsync(cts.Token);
            if (raw == null) break;
            if (raw.Length < 2 || raw[0] != 0x00) continue; // skip non-data frames

            Ax25Frame? f = Ax25Frame.Parse(raw[1..]);
            if (f == null) continue;
            // Only echo PINGs addressed to TNC-B
            if (f.DestCall != CallB || f.DestSsid != SsidB) continue;
            if (!f.Info.StartsWith("PING ")) continue;

            if (verbose) VerboseLog($"TNC-B RX  {f.SrcCall}-{f.SrcSsid} → {f.DestCall}-{f.DestSsid}  \"{f.Info}\"");

            string replyInfo = "PONG" + f.Info[4..]; // PING → PONG, keep rest
            byte[] reply = Ax25Frame.BuildUI(f.SrcCall, f.SrcSsid, CallB, SsidB, replyInfo);
            await tncB.SendAsync(reply, cts.Token);

            if (verbose) VerboseLog($"TNC-B TX  {CallB}-{SsidB} → {f.SrcCall}-{f.SrcSsid}  \"{replyInfo}\"");
        }
    }
    catch (OperationCanceledException) { }
    catch (Exception ex) when (verbose) { VerboseLog($"[echo error] {ex.Message}"); }
});

// ── TNC-A receive task ────────────────────────────────────────────────────────
// Collects PONG replies; resolves the matching pending TaskCompletionSource.
var recvTask = Task.Run(async () =>
{
    try
    {
        while (!cts.Token.IsCancellationRequested)
        {
            byte[]? raw = await tncA.ReceiveAsync(cts.Token);
            if (raw == null) break;
            if (raw.Length < 2 || raw[0] != 0x00) continue;

            Ax25Frame? f = Ax25Frame.Parse(raw[1..]);
            if (f == null) continue;
            // Only accept PONGs from TNC-B addressed to TNC-A
            if (f.SrcCall != CallB  || f.SrcSsid != SsidB)  continue;
            if (f.DestCall != CallA || f.DestSsid != SsidA) continue;
            if (!f.Info.StartsWith("PONG ")) continue;

            if (verbose) VerboseLog($"TNC-A RX  {f.SrcCall}-{f.SrcSsid} → {f.DestCall}-{f.DestSsid}  \"{f.Info}\"");

            if (int.TryParse(f.Info.Split(' ')[1], out int seq) &&
                pending.TryRemove(seq, out var entry))
            {
                entry.Tcs.TrySetResult(Stopwatch.GetTimestamp());
            }
        }
    }
    catch (OperationCanceledException) { }
    catch (Exception ex) when (verbose) { VerboseLog($"[recv error] {ex.Message}"); }
});

// ── Send loop ─────────────────────────────────────────────────────────────────
for (int i = 1; i <= count && !cts.Token.IsCancellationRequested; i++)
{
    var tcs   = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
    long sent = Stopwatch.GetTimestamp();
    pending[i] = (tcs, sent);

    byte[] ping = Ax25Frame.BuildUI(CallB, SsidB, CallA, SsidA, $"PING {i:D4}");
    await tncA.SendAsync(ping, cts.Token);

    Console.Write($"  [{i.ToString().PadLeft(width)}/{count}] PING sent ... ");

    try
    {
        using var timeoutCts = new CancellationTokenSource(timeoutMs);
        long gotAt = await tcs.Task.WaitAsync(timeoutCts.Token);

        double rttMs = (gotAt - sent) * 1000.0 / Stopwatch.Frequency;
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"PONG received   RTT: {rttMs:F1} ms  ✓");
        Console.ResetColor();
        rtts.Add(rttMs);
        received++;
    }
    catch (OperationCanceledException)
    {
        pending.TryRemove(i, out _);
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"TIMEOUT ({timeoutMs} ms)  ✗");
        Console.ResetColor();
    }

    if (i < count)
    {
        try { await Task.Delay(intervalMs, cts.Token); }
        catch (OperationCanceledException) { break; }
    }
}

// ── Cleanup ───────────────────────────────────────────────────────────────────
cts.Cancel();
try { await Task.WhenAll(echoTask, recvTask).WaitAsync(TimeSpan.FromSeconds(2)); }
catch { /* background tasks may have already stopped */ }

// ── Statistics ────────────────────────────────────────────────────────────────
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("  ─────────── Statistics ────────────");
Console.ResetColor();
Console.WriteLine($"  Sent:         {count}");
Console.WriteLine($"  Received:     {received}");

double loss = (count - received) * 100.0 / count;
Console.ForegroundColor = loss == 0 ? ConsoleColor.Green
                        : loss < 20  ? ConsoleColor.Yellow
                        :              ConsoleColor.Red;
Console.WriteLine($"  Packet loss:  {loss:F1}%");
Console.ResetColor();

if (rtts.Count > 0)
    Console.WriteLine($"  RTT (ms):     min {rtts.Min():F1}  avg {rtts.Average():F1}  max {rtts.Max():F1}");
else
    Console.WriteLine("  RTT (ms):     n/a");

Console.WriteLine();

tncA.Dispose();
tncB.Dispose();
return received == count ? 0 : 1;

// ── Local helpers ─────────────────────────────────────────────────────────────
static void VerboseLog(string msg) =>
    Console.WriteLine($"  [{DateTime.Now:HH:mm:ss.fff}] {msg}");

static KissClient? TryConnect(string label, string host, int port)
{
    Console.Write($"  {label}  {host}:{port} ... ");
    try
    {
        var c = new KissClient(host, port);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Connected ✓");
        Console.ResetColor();
        return c;
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Failed — {ex.Message}");
        Console.ResetColor();
        return null;
    }
}

static void PrintHelp()
{
    Console.WriteLine("HackTNC KISS Loopback Tester");
    Console.WriteLine();
    Console.WriteLine("Usage: loopback [options]");
    Console.WriteLine();
    Console.WriteLine("  Connects to two KISS/TCP TNCs and bounces packets between them.");
    Console.WriteLine("  TNC-A sends a PING; TNC-B receives it and replies with a PONG;");
    Console.WriteLine("  TNC-A receives the PONG and records the round-trip time.");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --host HOST      TNC host address      (default: 127.0.0.1)");
    Console.WriteLine("  --port-a PORT    TNC-A KISS TCP port   (default: 8000)");
    Console.WriteLine("  --port-b PORT    TNC-B KISS TCP port   (default: 8001)");
    Console.WriteLine("  --count N        Number of packets     (default: 10)");
    Console.WriteLine("  --interval MS    Delay between PINGs   (default: 500)");
    Console.WriteLine("  --timeout MS     Wait per PONG (ms)    (default: 5000)");
    Console.WriteLine("  --verbose        Print raw frame info");
    Console.WriteLine("  -h, --help       Show this help");
}
