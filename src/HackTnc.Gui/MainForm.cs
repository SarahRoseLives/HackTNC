using System.Collections.Concurrent;
using HackTnc.Core.Configuration;
using HackTnc.Core.Services;

namespace HackTnc.Gui;

public sealed partial class MainForm : Form
{
    private static readonly Color BackgroundDark = Color.FromArgb(18, 18, 24);
    private static readonly Color PanelDark = Color.FromArgb(28, 28, 38);
    private static readonly Color BorderColor = Color.FromArgb(55, 55, 75);
    private static readonly Color AccentGreen = Color.FromArgb(80, 220, 120);
    private static readonly Color AccentRed = Color.FromArgb(220, 70, 70);
    private static readonly Color AccentBlue = Color.FromArgb(80, 160, 240);
    private static readonly Color AccentAmber = Color.FromArgb(240, 180, 50);
    private static readonly Color TextPrimary = Color.FromArgb(230, 230, 240);
    private static readonly Color TextMuted = Color.FromArgb(130, 130, 150);

    private HackrfKissTncService? _service;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<string, DateTime> _clients = new();
    private bool _running;

    public MainForm()
    {
        InitializeComponent();
        ApplyTheme();
        UpdateControls();
    }

    // ── Start / Stop ──────────────────────────────────────────────────────────

    private async void BtnStartStop_Click(object sender, EventArgs e)
    {
        if (_running)
        {
            await StopServiceAsync();
        }
        else
        {
            await StartServiceAsync();
        }
    }

    private async Task StartServiceAsync()
    {
        btnStartStop.Enabled = false;
        SetStatus("Starting…", AccentAmber);

        try
        {
            var options = BuildOptions();
            _cts = new CancellationTokenSource();
            _service = new HackrfKissTncService(options, msg => AppendLog(msg));

            _service.ClientConnected += ep => BeginInvoke(() => OnClientConnected(ep));
            _service.ClientDisconnected += ep => BeginInvoke(() => OnClientDisconnected(ep));
            _service.PacketReceived += pkt => BeginInvoke(() => AppendPacket("RX", pkt, AccentGreen));
            _service.PacketTransmitted += pkt => BeginInvoke(() => AppendPacket("TX", pkt, AccentBlue));

            await _service.StartAsync(_cts.Token);

            lblFirmware.Text = $"Firmware: {_service.FirmwareVersion}";
            _running = true;
            SetStatus("Listening", AccentGreen);
            AppendLog($"Started on {options.FrequencyHz / 1e6:F4} MHz  KISS {options.BindAddress}:{options.KissPort}");
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex.Message}");
            SetStatus("Error", AccentRed);
            _service?.DisposeAsync().AsTask().ContinueWith(_ => { });
            _service = null;
        }
        finally
        {
            btnStartStop.Enabled = true;
            UpdateControls();
        }
    }

    private async Task StopServiceAsync()
    {
        btnStartStop.Enabled = false;
        SetStatus("Stopping…", AccentAmber);

        try
        {
            _cts?.Cancel();
            if (_service != null)
            {
                await _service.DisposeAsync();
                _service = null;
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Stop error: {ex.Message}");
        }
        finally
        {
            _running = false;
            _clients.Clear();
            RefreshClientList();
            lblFirmware.Text = "Firmware: —";
            SetStatus("Stopped", TextMuted);
            btnStartStop.Enabled = true;
            UpdateControls();
        }
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnClientConnected(string endpoint)
    {
        _clients[endpoint] = DateTime.Now;
        RefreshClientList();
        SetStatus($"Connected  ({_clients.Count} client{(_clients.Count == 1 ? "" : "s")})", AccentGreen);
        AppendLog($"KISS client connected: {endpoint}");
    }

    private void OnClientDisconnected(string endpoint)
    {
        _clients.TryRemove(endpoint, out _);
        RefreshClientList();
        var status = _clients.IsEmpty
            ? "Listening"
            : $"Connected  ({_clients.Count} client{(_clients.Count == 1 ? "" : "s")})";
        SetStatus(status, _clients.IsEmpty ? AccentGreen : AccentBlue);
        AppendLog($"KISS client disconnected: {endpoint}");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private TncOptions BuildOptions()
    {
        var freqMhz = (double)nudFrequency.Value;
        return new TncOptions
        {
            FrequencyHz = (long)(freqMhz * 1_000_000),
            BindAddress = txtBindAddress.Text.Trim(),
            KissPort = (int)nudKissPort.Value,
            LnaGainDb = (int)nudLnaGain.Value,
            VgaGainDb = (int)nudVgaGain.Value,
            TxVgaGainDb = (int)nudTxVgaGain.Value,
            AmpEnable = chkAmp.Checked,
            SampleRateHz = 2_000_000,
            AudioSampleRate = 48_000,
            BasebandFilterBandwidthHz = 1_750_000,
            FmDeviationHz = 3_000,
            TxDelayMs = 300,
            TxTailMs = 50
        };
    }

    private void SetStatus(string text, Color color)
    {
        if (InvokeRequired) { BeginInvoke(() => SetStatus(text, color)); return; }
        lblStatusText.Text = text;
        lblStatusText.ForeColor = color;
        statusDot.BackColor = color;
    }

    private void AppendLog(string message)
    {
        if (InvokeRequired) { BeginInvoke(() => AppendLog(message)); return; }
        var ts = DateTime.Now.ToString("HH:mm:ss");
        txtLog.AppendText($"[{ts}] {message}{Environment.NewLine}");
    }

    private void AppendPacket(string direction, string info, Color color)
    {
        var ts = DateTime.Now.ToString("HH:mm:ss");
        var item = new ListViewItem(ts) { ForeColor = color };
        item.SubItems.Add(direction);
        item.SubItems.Add(info);
        listPackets.Items.Insert(0, item);
        if (listPackets.Items.Count > 200)
        {
            listPackets.Items.RemoveAt(200);
        }
    }

    private void RefreshClientList()
    {
        listClients.Items.Clear();
        foreach (var kv in _clients)
        {
            var item = new ListViewItem(kv.Key) { ForeColor = AccentGreen };
            item.SubItems.Add(kv.Value.ToString("HH:mm:ss"));
            listClients.Items.Add(item);
        }
        lblClientCount.Text = _clients.IsEmpty
            ? "No clients connected"
            : $"{_clients.Count} client{(_clients.Count == 1 ? "" : "s")} connected";
    }

    private void UpdateControls()
    {
        btnStartStop.Text = _running ? "⏹  Stop TNC" : "▶  Start TNC";
        btnStartStop.BackColor = _running ? Color.FromArgb(100, 35, 35) : Color.FromArgb(30, 80, 45);
        btnStartStop.ForeColor = _running ? AccentRed : AccentGreen;

        nudFrequency.Enabled = !_running;
        txtBindAddress.Enabled = !_running;
        nudKissPort.Enabled = !_running;
        nudLnaGain.Enabled = !_running;
        nudVgaGain.Enabled = !_running;
        nudTxVgaGain.Enabled = !_running;
        chkAmp.Enabled = !_running;
    }

    private void ApplyTheme()
    {
        BackColor = BackgroundDark;
        ForeColor = TextPrimary;

        foreach (Control c in Controls)
        {
            ApplyThemeRecursive(c);
        }
    }

    private void ApplyThemeRecursive(Control control)
    {
        switch (control)
        {
            case Panel p:
                p.BackColor = PanelDark;
                break;
            case GroupBox gb:
                gb.ForeColor = TextMuted;
                break;
            case Label lbl when lbl != lblStatusText:
                lbl.ForeColor = TextMuted;
                break;
            case TextBox tb:
                tb.BackColor = Color.FromArgb(38, 38, 52);
                tb.ForeColor = TextPrimary;
                tb.BorderStyle = BorderStyle.FixedSingle;
                break;
            case NumericUpDown nud:
                nud.BackColor = Color.FromArgb(38, 38, 52);
                nud.ForeColor = TextPrimary;
                break;
            case CheckBox cb:
                cb.ForeColor = TextPrimary;
                break;
            case ListView lv:
                lv.BackColor = Color.FromArgb(22, 22, 32);
                lv.ForeColor = TextPrimary;
                break;
        }

        foreach (Control child in control.Controls)
        {
            ApplyThemeRecursive(child);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_running)
        {
            _ = StopServiceAsync();
        }
        base.OnFormClosing(e);
    }
}
