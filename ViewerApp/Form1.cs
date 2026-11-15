using Engine.Telemetry;
using Engine.Transport;
using Engine.UI;

namespace ViewerApp;

public partial class Form1 : Form
{
    private readonly UdpFrameReceiver _receiver;
    private readonly TcpControlClient _tcpClient = new();
    private readonly NetworkMetrics _metrics = new();
    private TcpStreamReceiver? _tcpStreamReceiver;
    private CancellationTokenSource? _tcpDecodeCts;
    private bool _isTcpMode;

    private static readonly Color AppBackground = Color.FromArgb(7, 11, 20);
    private static readonly Color ToolbarBg = Color.FromArgb(13, 21, 39);
    private static readonly Color InputBg = Color.FromArgb(14, 23, 38);
    private static readonly Color TextPrimary = Color.FromArgb(226, 232, 240);
    private static readonly Color TextMuted = Color.FromArgb(148, 163, 184);
    private static readonly Color SuccessGreen = Color.FromArgb(16, 185, 129);
    private static readonly Color DangerRed = Color.FromArgb(220, 38, 38);
    private static readonly Color PrimaryBlue = Color.FromArgb(24, 119, 242);
    private static readonly Color AccentCyan = Color.FromArgb(56, 189, 248);

    private readonly Panel _topToolbar = new() { Dock = DockStyle.Top, BackColor = ToolbarBg, Height = 80 };
    private readonly VideoCanvas _canvas = new() { Dock = DockStyle.Fill };
    private readonly Panel _statusBar = new() { Dock = DockStyle.Bottom, Height = 34, BackColor = Color.FromArgb(11, 18, 32) };

    private readonly Label _lblHost = new() { Text = "Host IP:", ForeColor = TextPrimary, Font = new Font("Segoe UI", 9.5f), Location = new Point(16, 12), Size = new Size(56, 20) };
    private readonly TextBox _txtHostIp = new()
    {
        Text = "127.0.0.1",
        Location = new Point(74, 9),
        Size = new Size(110, 26),
        BackColor = InputBg,
        ForeColor = TextPrimary,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Segoe UI", 9.5f)
    };
    private readonly Label _lblUdp = new() { Text = "UDP:", ForeColor = TextPrimary, Font = new Font("Segoe UI", 9.5f), Location = new Point(196, 12), Size = new Size(36, 20) };
    private readonly NumericUpDown _numUdpPort = new()
    {
        Minimum = 1000,
        Maximum = 65535,
        Value = 9000,
        Location = new Point(236, 9),
        Size = new Size(72, 26),
        BackColor = InputBg,
        ForeColor = TextPrimary,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Segoe UI", 9.5f)
    };
    private readonly Label _lblTcp = new() { Text = "TCP:", ForeColor = TextPrimary, Font = new Font("Segoe UI", 9.5f), Location = new Point(320, 12), Size = new Size(34, 20) };
    private readonly NumericUpDown _numTcpPort = new()
    {
        Minimum = 1000,
        Maximum = 65535,
        Value = 9001,
        Location = new Point(356, 9),
        Size = new Size(72, 26),
        BackColor = InputBg,
        ForeColor = TextPrimary,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Segoe UI", 9.5f)
    };

    private readonly ModernButton _btnConnect = new()
    {
        CustomIcon = "link",
        Text = "Connect",
        NormalColor = PrimaryBlue,
        HoverColor = Color.FromArgb(37, 133, 255),
        PressedColor = Color.FromArgb(18, 98, 204),
        BorderRadius = 7,
        Size = new Size(96, 30)
    };
    private readonly ModernButton _btnDisconnect = new()
    {
        CustomIcon = "✕",
        Text = "Disconnect",
        NormalColor = DangerRed,
        HoverColor = Color.FromArgb(239, 68, 68),
        PressedColor = Color.FromArgb(185, 28, 28),
        BorderRadius = 7,
        Size = new Size(106, 30),
        Enabled = false
    };

    private readonly Label _lblQuality = new() { Text = "Quality:", ForeColor = TextPrimary, Font = new Font("Segoe UI", 9.5f), Location = new Point(16, 48), Size = new Size(54, 20) };
    private readonly DarkSlider _trkQuality = new()
    {
        Minimum = 10,
        Maximum = 100,
        Value = 85,
        Location = new Point(74, 46),
        Size = new Size(110, 22),
        Enabled = false,
        TrackColor = Color.FromArgb(34, 50, 73),
        ProgressColor = Color.FromArgb(37, 99, 235),
        ThumbColor = AccentCyan
    };
    private readonly Label _lblQualityValue = new()
    {
        Text = "85%",
        ForeColor = AccentCyan,
        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
        Location = new Point(190, 48),
        Size = new Size(42, 20)
    };
    private readonly Label _lblMode = new() { Text = "Mode:", ForeColor = TextPrimary, Font = new Font("Segoe UI", 9.5f), Location = new Point(246, 48), Size = new Size(46, 20) };
    private readonly RadioButton _rbRudpMode = new()
    {
        Text = "RUDP",
        Checked = true,
        ForeColor = TextPrimary,
        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
        Location = new Point(296, 46),
        Size = new Size(76, 24)
    };
    private readonly RadioButton _rbTcpMode = new()
    {
        Text = "TCP",
        ForeColor = TextPrimary,
        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
        Location = new Point(378, 46),
        Size = new Size(66, 24)
    };

    private readonly ModernButton _btnStart = new()
    {
        CustomIcon = "▶",
        Text = "Start",
        NormalColor = SuccessGreen,
        HoverColor = Color.FromArgb(5, 150, 105),
        PressedColor = Color.FromArgb(4, 120, 87),
        BorderRadius = 7,
        Size = new Size(88, 30),
        Enabled = false
    };
    private readonly ModernButton _btnStop = new()
    {
        CustomIcon = "■",
        Text = "Stop",
        NormalColor = DangerRed,
        HoverColor = Color.FromArgb(239, 68, 68),
        PressedColor = Color.FromArgb(185, 28, 28),
        BorderRadius = 7,
        Size = new Size(88, 30),
        Enabled = false
    };

    private readonly ModernCheckBox _chkNack = new()
    {
        Text = "NACK",
        Checked = true,
        CheckColor = PrimaryBlue,
        ForeColor = TextPrimary,
        Dock = DockStyle.Right,
        Width = 80
    };

    private readonly Label _lblTelemetry = new()
    {
        Dock = DockStyle.Fill,
        BackColor = Color.Transparent,
        ForeColor = TextMuted,
        Font = new Font("Consolas", 8.5f),
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(12, 0, 0, 0)
    };

    private readonly System.Windows.Forms.Timer _telemetryTimer = new() { Interval = 250 };
    private uint _lastFrameId;
    private uint _lastLatencyMs;
    private int _lastFrameBytes;

    public Form1()
    {
        InitializeComponent();
        Text = "↔  RUDP Viewer App – Remote Session";
        Size = new Size(1100, 680);
        MinimumSize = new Size(760, 500);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = AppBackground;

        BuildUi();

        _receiver = new UdpFrameReceiver(listenPort: (int)_numUdpPort.Value);

        HookEvents();

        _ = Task.Run(UdpDecodeAndRenderLoopAsync);

        _telemetryTimer.Tick += (_, _) => RefreshTelemetry();
        _telemetryTimer.Start();
    }

    private void BuildUi()
    {
        _topToolbar.Controls.AddRange([
            _lblHost, _txtHostIp, _lblUdp, _numUdpPort, _lblTcp, _numTcpPort,
            _btnConnect, _btnDisconnect,
            _lblQuality, _trkQuality, _lblQualityValue, _lblMode, _rbRudpMode, _rbTcpMode,
            _btnStart, _btnStop
        ]);

        _statusBar.Controls.Add(_lblTelemetry);
        _statusBar.Controls.Add(_chkNack);

        Controls.Add(_canvas);
        Controls.Add(_statusBar);
        Controls.Add(_topToolbar);

        _canvas.BringToFront();

        _topToolbar.Resize += (_, _) => UpdateToolbarLayout();
        UpdateToolbarLayout();
    }

    private void UpdateToolbarLayout()
    {
        int pad = 16;
        int w = _topToolbar.ClientSize.Width;

        int discX = w - pad - _btnDisconnect.Width;
        int connX = discX - 10 - _btnConnect.Width;
        if (connX < _numTcpPort.Right + 12)
        {
            connX = _numTcpPort.Right + 12;
            discX = connX + _btnConnect.Width + 10;
        }
        _btnConnect.Location = new Point(connX, 7);
        _btnDisconnect.Location = new Point(discX, 7);

        int stopX = w - pad - _btnStop.Width;
        int startX = stopX - 10 - _btnStart.Width;
        if (startX < _rbTcpMode.Right + 12)
        {
            startX = _rbTcpMode.Right + 12;
            stopX = startX + _btnStart.Width + 10;
        }
        _btnStart.Location = new Point(startX, 43);
        _btnStop.Location = new Point(stopX, 43);
    }

    private void HookEvents()
    {
        _trkQuality.ValueChanged += (_, _) =>
        {
            _lblQualityValue.Text = $"{_trkQuality.Value}%";
            if (_tcpClient.IsConnected)
                _ = _tcpClient.SendSetQualityAsync((byte)_trkQuality.Value);
        };

        _chkNack.CheckedChanged += (_, _) =>
        {
            _receiver.EnableNack = _chkNack.Checked;
        };

        _rbRudpMode.CheckedChanged += (_, _) =>
        {
            if (!_rbRudpMode.Checked) return;
            _isTcpMode = false;
            _chkNack.Enabled = true;
            if (_tcpClient.IsConnected)
                _ = _tcpClient.SendSwitchModeAsync(0x00);
            StopTcpReceiver();
        };

        _rbTcpMode.CheckedChanged += (_, _) =>
        {
            if (!_rbTcpMode.Checked) return;
            _isTcpMode = true;
            _chkNack.Enabled = false;
            if (_tcpClient.IsConnected)
            {
                _ = _tcpClient.SendSwitchModeAsync(0x01);
                StartTcpReceiver();
            }
        };

        _btnConnect.Click += async (_, _) =>
        {
            if (_tcpClient.IsConnected) return;

            _btnConnect.Enabled = false;
            _canvas.StatusSubText = "Connecting to Host...";

            try
            {
                _receiver.Rebind((int)_numUdpPort.Value);
                await _tcpClient.ConnectAsync(
                    _txtHostIp.Text.Trim(),
                    (int)_numTcpPort.Value,
                    (ushort)_numUdpPort.Value
                );
            }
            catch (Exception ex)
            {
                _btnConnect.Enabled = true;
                _canvas.ClearFrame($"Connection Failed: {ex.Message}");
            }
        };

        _btnDisconnect.Click += async (_, _) =>
        {
            _btnDisconnect.Enabled = false;
            _canvas.StatusSubText = "Disconnecting...";

            try { await _tcpClient.DisconnectAsync(); }
            finally { _canvas.ClearFrame("Disconnected. Click 'Connect' to reconnect."); }
        };

        _btnStart.Click += async (_, _) =>
        {
            if (!_tcpClient.IsConnected) return;

            await _tcpClient.SendStartStreamAsync();
            _btnStart.Enabled = false;
            _btnStop.Enabled = true;
            _rbRudpMode.Enabled = false;
            _rbTcpMode.Enabled = false;

            if (_isTcpMode) StartTcpReceiver();
        };

        _btnStop.Click += async (_, _) =>
        {
            if (!_tcpClient.IsConnected) return;

            await _tcpClient.SendStopStreamAsync();
            _btnStart.Enabled = true;
            _btnStop.Enabled = false;
            _rbRudpMode.Enabled = true;
            _rbTcpMode.Enabled = true;
            StopTcpReceiver();
        };

        _tcpClient.Connected += () => Invoke(() =>
        {
            _btnConnect.Enabled = false;
            _btnDisconnect.Enabled = true;
            _txtHostIp.Enabled = false;
            _numTcpPort.Enabled = false;
            _numUdpPort.Enabled = false;
            _btnStart.Enabled = true;
            _btnStop.Enabled = false;
            _trkQuality.Enabled = true;
            _canvas.StatusSubText = "Connected. Click 'Start' to receive screen.";
        });

        _tcpClient.Disconnected += () => Invoke(() =>
        {
            _btnConnect.Enabled = true;
            _btnDisconnect.Enabled = false;
            _txtHostIp.Enabled = true;
            _numTcpPort.Enabled = true;
            _numUdpPort.Enabled = true;
            _btnStart.Enabled = false;
            _btnStop.Enabled = false;
            _trkQuality.Enabled = false;
            _rbRudpMode.Enabled = true;
            _rbTcpMode.Enabled = true;
            StopTcpReceiver();
            _canvas.ClearFrame("UDP packets on port " + _numUdpPort.Value + "...");
        });

        _tcpClient.StatusReceived += msg => Invoke(() =>
        {
            Text = $"↔  RUDP Viewer App – Host: {msg}";
        });
    }

    private void StartTcpReceiver()
    {
        StopTcpReceiver();
        _tcpStreamReceiver = new TcpStreamReceiver();
        _tcpDecodeCts = new CancellationTokenSource();
        var host = _txtHostIp.Text.Trim();
        int port = (int)_numUdpPort.Value + 10;
        var ct = _tcpDecodeCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await _tcpStreamReceiver.ConnectAsync(host, port, ct);
                await TcpDecodeAndRenderLoopAsync(ct);
            }
            catch { }
        });
    }

    private void StopTcpReceiver()
    {
        _tcpDecodeCts?.Cancel();
        _tcpDecodeCts?.Dispose();
        _tcpDecodeCts = null;
        _tcpStreamReceiver?.Dispose();
        _tcpStreamReceiver = null;
    }

    private async Task UdpDecodeAndRenderLoopAsync()
    {
        await foreach (var frame in _receiver.CompletedFrames.ReadAllAsync())
        {
            using (frame)
            {
                if (_isTcpMode) continue;
                try
                {
                    using var ms = new MemoryStream(frame.PooledBuffer, 0, frame.Length, writable: false);
                    var bmp = new Bitmap(ms);

                    _lastFrameId = frame.FrameID;
                    _lastFrameBytes = frame.Length;
                    _lastLatencyMs = (uint)Math.Max(0, Environment.TickCount64 - frame.TimestampMs);

                    _metrics.RecordFrame(frame.Length, _lastLatencyMs);
                    _canvas.UpdateFrame(bmp);
                }
                catch { }
            }
        }
    }

    private async Task TcpDecodeAndRenderLoopAsync(CancellationToken ct)
    {
        if (_tcpStreamReceiver == null) return;

        await foreach (var (data, length, timestamp) in _tcpStreamReceiver.CompletedFrames.ReadAllAsync(ct))
        {
            if (!_isTcpMode) continue;
            try
            {
                using var ms = new MemoryStream(data, 0, length, writable: false);
                var bmp = new Bitmap(ms);

                uint latency = (uint)Math.Max(0, Environment.TickCount64 - timestamp);
                _lastFrameBytes = length;
                _lastLatencyMs = latency;
                _lastFrameId++;

                _metrics.RecordFrame(length, latency);
                _canvas.UpdateFrame(bmp);
            }
            catch { }
        }
    }

    private void RefreshTelemetry()
    {
        _metrics.UpdateSnapshot();
        var r = _receiver.Reassembler;
        var chaos = _receiver.Chaos;

        string modeText = _isTcpMode ? "TCP" : "RUDP";

        _lblTelemetry.Text =
            $"● Frame: {_lastFrameId} ({_lastFrameBytes / 1024.0:F1} KB)   |   " +
            $"{_metrics.CurrentFps:F1} FPS ({_metrics.CurrentBitrateMbps:F2} Mbps)   |   " +
            $"Mode: {modeText}   |   " +
            $"OK: {r.TotalFramesCompleted}   |   " +
            $"Drop: {r.TotalFramesDroppedIncomplete}   |   " +
            $"Chaos: {chaos.DropRatePercent}% ({chaos.TotalPacketsDropped})   |   " +
            $"Lat: {_lastLatencyMs} ms   |   " +
            $"TCP: {(_tcpClient.IsConnected ? "✓" : "X")}";
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _telemetryTimer.Stop();
        StopTcpReceiver();
        _tcpClient.Dispose();
        _receiver.Dispose();
        base.OnFormClosing(e);
    }
}
