namespace HackTnc.Gui;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;

    // ── Header bar ────────────────────────────────────────────────────────────
    private Panel pnlHeader = null!;
    private Label lblTitle = null!;
    private Label lblFirmware = null!;

    // ── Status strip ─────────────────────────────────────────────────────────
    private Panel pnlStatus = null!;
    private Panel statusDot = null!;
    private Label lblStatusLabel = null!;
    private Label lblStatusText = null!;

    // ── Left config panel ─────────────────────────────────────────────────────
    private Panel pnlConfig = null!;

    private GroupBox grpDevice = null!;
    private Label lblDevice = null!;
    private ComboBox cmbDevice = null!;
    private Button btnRefreshDevices = null!;

    private GroupBox grpFreq = null!;
    private Label lblFreqMhz = null!;
    private NumericUpDown nudFrequency = null!;

    private GroupBox grpKiss = null!;
    private Label lblBind = null!;
    private TextBox txtBindAddress = null!;
    private Label lblPort = null!;
    private NumericUpDown nudKissPort = null!;

    private GroupBox grpGain = null!;
    private Label lblLna = null!;
    private NumericUpDown nudLnaGain = null!;
    private Label lblVga = null!;
    private NumericUpDown nudVgaGain = null!;
    private Label lblTxVga = null!;
    private NumericUpDown nudTxVgaGain = null!;
    private CheckBox chkAmp = null!;

    private Button btnStartStop = null!;

    // ── Right content panel ───────────────────────────────────────────────────
    private Panel pnlContent = null!;

    private GroupBox grpClients = null!;
    private Label lblClientCount = null!;
    private ListView listClients = null!;

    private GroupBox grpPackets = null!;
    private ListView listPackets = null!;

    private GroupBox grpLog = null!;
    private TextBox txtLog = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null)
            components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        SuspendLayout();

        // ── Form ──────────────────────────────────────────────────────────────
        Text = "HackTNC  —  AFSK1200 KISS/TCP TNC";
        Size = new Size(1020, 720);
        MinimumSize = new Size(900, 640);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f, FontStyle.Regular);
        BackColor = BackgroundDark;
        ForeColor = TextPrimary;

        // ── Header ────────────────────────────────────────────────────────────
        pnlHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 52,
            BackColor = Color.FromArgb(24, 24, 34),
            Padding = new Padding(16, 0, 16, 0)
        };

        lblTitle = new Label
        {
            Text = "HackTNC",
            Font = new Font("Segoe UI", 16f, FontStyle.Bold),
            ForeColor = AccentBlue,
            AutoSize = true,
            Location = new Point(16, 12)
        };

        lblFirmware = new Label
        {
            Text = "Firmware: —",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = TextMuted,
            AutoSize = true,
            Location = new Point(150, 18)
        };

        pnlHeader.Controls.AddRange(new Control[] { lblTitle, lblFirmware });

        // ── Status bar ────────────────────────────────────────────────────────
        pnlStatus = new Panel
        {
            Dock = DockStyle.Top,
            Height = 30,
            BackColor = Color.FromArgb(20, 20, 30),
            Padding = new Padding(12, 0, 0, 0)
        };

        statusDot = new Panel
        {
            Size = new Size(10, 10),
            Location = new Point(14, 10),
            BackColor = TextMuted
        };
        MakeRound(statusDot);

        lblStatusLabel = new Label
        {
            Text = "Status:",
            ForeColor = TextMuted,
            Font = new Font("Segoe UI", 8.5f),
            AutoSize = true,
            Location = new Point(30, 7)
        };

        lblStatusText = new Label
        {
            Text = "Stopped",
            ForeColor = TextMuted,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(75, 7)
        };

        pnlStatus.Controls.AddRange(new Control[] { statusDot, lblStatusLabel, lblStatusText });

        // ── Config panel (left) ───────────────────────────────────────────────
        pnlConfig = new Panel
        {
            Dock = DockStyle.Left,
            Width = 240,
            BackColor = PanelDark,
            Padding = new Padding(12)
        };

        // Device group
        grpDevice = MakeGroup("HackRF Device", 8);

        lblDevice = new Label { Text = "Select device", ForeColor = TextMuted, AutoSize = true, Location = new Point(8, 20) };

        cmbDevice = new ComboBox
        {
            Location = new Point(8, 36),
            Width = 168,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(38, 38, 52),
            ForeColor = TextPrimary,
            FlatStyle = FlatStyle.Flat
        };

        btnRefreshDevices = new Button
        {
            Text = "↻",
            Location = new Point(180, 34),
            Width = 28,
            Height = 24,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10f),
            BackColor = Color.FromArgb(38, 38, 52),
            ForeColor = TextMuted,
            Cursor = Cursors.Hand
        };
        btnRefreshDevices.FlatAppearance.BorderColor = BorderColor;
        btnRefreshDevices.FlatAppearance.BorderSize = 1;
        btnRefreshDevices.Click += (_, _) => LoadDeviceList();

        grpDevice.Controls.AddRange(new Control[] { lblDevice, cmbDevice, btnRefreshDevices });
        grpDevice.Height = 72;

        // Frequency group
        grpFreq = MakeGroup("Frequency", 88);

        lblFreqMhz = new Label { Text = "MHz", ForeColor = TextMuted, AutoSize = true, Location = new Point(178, 24) };

        nudFrequency = new NumericUpDown
        {
            Location = new Point(8, 20),
            Width = 160,
            DecimalPlaces = 4,
            Minimum = 1m,
            Maximum = 6000m,
            Value = 144.3900m,
            Increment = 0.0250m,
            BackColor = Color.FromArgb(38, 38, 52),
            ForeColor = TextPrimary,
            Font = new Font("Segoe UI", 12f, FontStyle.Bold)
        };

        grpFreq.Controls.AddRange(new Control[] { nudFrequency, lblFreqMhz });
        grpFreq.Height = 58;

        // KISS group
        grpKiss = MakeGroup("KISS/TCP", 158);

        lblBind = new Label { Text = "Bind address", ForeColor = TextMuted, AutoSize = true, Location = new Point(8, 20) };
        txtBindAddress = new TextBox
        {
            Location = new Point(8, 36),
            Width = 196,
            Text = "127.0.0.1",
            BackColor = Color.FromArgb(38, 38, 52),
            ForeColor = TextPrimary
        };
        lblPort = new Label { Text = "Port", ForeColor = TextMuted, AutoSize = true, Location = new Point(8, 62) };
        nudKissPort = new NumericUpDown
        {
            Location = new Point(8, 78),
            Width = 100,
            Minimum = 1,
            Maximum = 65535,
            Value = 8001,
            BackColor = Color.FromArgb(38, 38, 52),
            ForeColor = TextPrimary
        };

        grpKiss.Controls.AddRange(new Control[] { lblBind, txtBindAddress, lblPort, nudKissPort });
        grpKiss.Height = 112;

        // Gain group
        grpGain = MakeGroup("RF Gain", 282);

        lblLna = new Label { Text = "LNA gain (dB)", ForeColor = TextMuted, AutoSize = true, Location = new Point(8, 20) };
        nudLnaGain = MakeGainSpinner(new Point(8, 36), 40, 24);

        lblVga = new Label { Text = "VGA gain (dB)", ForeColor = TextMuted, AutoSize = true, Location = new Point(8, 64) };
        nudVgaGain = MakeGainSpinner(new Point(8, 80), 62, 24);

        lblTxVga = new Label { Text = "TX VGA gain (dB)", ForeColor = TextMuted, AutoSize = true, Location = new Point(8, 108) };
        nudTxVgaGain = MakeGainSpinner(new Point(8, 124), 47, 20);

        chkAmp = new CheckBox
        {
            Text = "Enable RF amplifier (+11 dB)",
            ForeColor = TextPrimary,
            AutoSize = true,
            Location = new Point(8, 156)
        };

        grpGain.Controls.AddRange(new Control[] { lblLna, nudLnaGain, lblVga, nudVgaGain, lblTxVga, nudTxVgaGain, chkAmp });
        grpGain.Height = 180;

        // Start/Stop button
        btnStartStop = new Button
        {
            Text = "▶  Start TNC",
            Location = new Point(12, 474),
            Width = 216,
            Height = 40,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            BackColor = Color.FromArgb(30, 80, 45),
            ForeColor = AccentGreen,
            Cursor = Cursors.Hand
        };
        btnStartStop.FlatAppearance.BorderColor = Color.FromArgb(50, 130, 70);
        btnStartStop.FlatAppearance.BorderSize = 1;
        btnStartStop.Click += BtnStartStop_Click;

        pnlConfig.Controls.AddRange(new Control[] { grpDevice, grpFreq, grpKiss, grpGain, btnStartStop });

        // ── Content panel (right) ─────────────────────────────────────────────
        pnlContent = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = BackgroundDark,
            Padding = new Padding(10)
        };

        // Clients group (top-right, fixed height)
        grpClients = new GroupBox
        {
            Text = "KISS Clients",
            ForeColor = TextMuted,
            Dock = DockStyle.Top,
            Height = 130,
            Padding = new Padding(6)
        };

        lblClientCount = new Label
        {
            Text = "No clients connected",
            ForeColor = TextMuted,
            Dock = DockStyle.Bottom,
            Height = 18,
            TextAlign = ContentAlignment.MiddleLeft
        };

        listClients = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = false,
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(22, 22, 32),
            ForeColor = TextPrimary,
            HeaderStyle = ColumnHeaderStyle.Nonclickable
        };
        listClients.Columns.Add("Endpoint", 220);
        listClients.Columns.Add("Connected at", 120);

        grpClients.Controls.AddRange(new Control[] { listClients, lblClientCount });

        // Packets group (middle, stretches)
        grpPackets = new GroupBox
        {
            Text = "Packet Activity",
            ForeColor = TextMuted,
            Dock = DockStyle.Fill,
            Padding = new Padding(6)
        };

        listPackets = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = false,
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(22, 22, 32),
            ForeColor = TextPrimary,
            HeaderStyle = ColumnHeaderStyle.Nonclickable
        };
        listPackets.Columns.Add("Time", 70);
        listPackets.Columns.Add("Dir", 40);
        listPackets.Columns.Add("Packet", 500);

        grpPackets.Controls.Add(listPackets);

        // Log group (bottom, fixed height)
        grpLog = new GroupBox
        {
            Text = "Log",
            ForeColor = TextMuted,
            Dock = DockStyle.Bottom,
            Height = 160,
            Padding = new Padding(6)
        };

        txtLog = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(14, 14, 20),
            ForeColor = Color.FromArgb(160, 210, 160),
            Font = new Font("Cascadia Mono", 8.5f) is { } f && FontExists("Cascadia Mono")
                ? new Font("Cascadia Mono", 8.5f)
                : new Font("Consolas", 8.5f),
            BorderStyle = BorderStyle.None,
            WordWrap = false
        };

        grpLog.Controls.Add(txtLog);

        // stack content: log at bottom, clients at top, packets fills middle
        pnlContent.Controls.Add(grpPackets);
        pnlContent.Controls.Add(grpLog);
        pnlContent.Controls.Add(grpClients);

        // ── Separator between panels ──────────────────────────────────────────
        var sep = new Panel
        {
            Dock = DockStyle.Left,
            Width = 1,
            BackColor = BorderColor
        };

        Controls.Add(pnlContent);
        Controls.Add(sep);
        Controls.Add(pnlConfig);
        Controls.Add(pnlStatus);
        Controls.Add(pnlHeader);

        ResumeLayout(false);
    }

    // ── Layout helpers ────────────────────────────────────────────────────────

    private static GroupBox MakeGroup(string title, int top)
    {
        return new GroupBox
        {
            Text = title,
            Location = new Point(12, top),
            Width = 216,
            ForeColor = TextMuted,
            Font = new Font("Segoe UI", 8.5f)
        };
    }

    private static NumericUpDown MakeGainSpinner(Point location, int max, int defaultValue)
    {
        return new NumericUpDown
        {
            Location = location,
            Width = 80,
            Minimum = 0,
            Maximum = max,
            Value = defaultValue,
            BackColor = Color.FromArgb(38, 38, 52),
            ForeColor = TextPrimary
        };
    }

    private static void MakeRound(Panel panel)
    {
        panel.Region = new Region(
            new System.Drawing.Drawing2D.GraphicsPath().Tap(p =>
            {
                p.AddEllipse(0, 0, panel.Width, panel.Height);
            }));
    }

    private static bool FontExists(string name)
    {
        using var test = new Font(name, 9f);
        return string.Equals(test.Name, name, StringComparison.OrdinalIgnoreCase);
    }
}

// tiny extension so GraphicsPath.Tap reads cleanly
file static class Extensions
{
    public static T Tap<T>(this T obj, Action<T> action) { action(obj); return obj; }
}
