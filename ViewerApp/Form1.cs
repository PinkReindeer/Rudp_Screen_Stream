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
    private bool _isStreaming;
    private bool _updatingQualityFromRemote;
    private bool _updatingModeFromRemote;

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

    private readonly Label _lblHost = new() { Text = "Host IP:", AutoSize = true, ForeColor = TextPrimary, Font = new Font("Segoe UI", 9.5f) };
    private readonly TextBox _txtHostIp = new()
    {
        Text = "127.0.0.1",
        Size = new Size(110, 26),
        BackColor = InputBg,
        ForeColor = TextPrimary,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Segoe UI", 9.5f)
    };
    private readonly Label _lblUdp = new() { Text = "UDP:", AutoSize = true, ForeColor = TextPrimary, Font = new Font("Segoe UI", 9.5f) };
    private readonly NumericUpDown _numUdpPort = new()
    {
        Minimum = 1000,
        Maximum = 65535,
        Value = 9000,
        Size = new Size(72, 26),
        BackColor = InputBg,
        ForeColor = TextPrimary,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Segoe UI", 9.5f)
    };
    private readonly Label _lblTcp = new() { Text = "TCP:", AutoSize = true, ForeColor = TextPrimary, Font = new Font("Segoe UI", 9.5f) };
    private readonly NumericUpDown _numTcpPort = new()
    {
        Minimum = 1000,
        Maximum = 65535,
        Value = 9001,
        Size = new Size(72, 26),
        BackColor = InputBg,
        ForeColor = TextPrimary,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Segoe UI", 9.5f)
    };

    private readonly ModernButton _btnConnect = new()
    {
        Text = "Connect",
        NormalColor = PrimaryBlue,
        HoverColor = Color.FromArgb(37, 133, 255),
        PressedColor = Color.FromArgb(18, 98, 204),
        BorderRadius = 7,
        Size = new Size(96, 30)
    };
    private readonly ModernButton _btnDisconnect = new()
    {
        Text = "Disconnect",
        NormalColor = DangerRed,
        HoverColor = Color.FromArgb(239, 68, 68),
        PressedColor = Color.FromArgb(185, 28, 28),
        BorderRadius = 7,
        Size = new Size(106, 30),
        Enabled = false
    };

    private readonly Label _lblQuality = new() { Text = "Quality:", AutoSize = true, ForeColor = TextPrimary, Font = new Font("Segoe UI", 9.5f) };
    private readonly DarkSlider _trkQuality = new()
    {
        Minimum = 10,
        Maximum = 100,
        Value = 85,
        Size = new Size(110, 22),
        Enabled = false,
        TrackColor = Color.FromArgb(34, 50, 73),
        ProgressColor = Color.FromArgb(37, 99, 235),
        ThumbColor = AccentCyan
    };
    private readonly Label _lblQualityValue = new()
    {
        Text = "85%",
        AutoSize = true,
        ForeColor = AccentCyan,
        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
    };
    private readonly Label _lblMode = new() { Text = "Mode:", AutoSize = true, ForeColor = TextPrimary, Font = new Font("Segoe UI", 9.5f) };
    private readonly RadioButton _rbRudpMode = new()
    {
        Text = "RUDP",
        Checked = true,
        AutoSize = true,
        ForeColor = TextPrimary,
        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
    };
    private readonly RadioButton _rbTcpMode = new()
    {
        Text = "TCP",
        AutoSize = true,
        ForeColor = TextPrimary,
        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
    };

    private readonly ModernButton _btnStart = new()
    {
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
        Text = "Stop",
        NormalColor = DangerRed,
        HoverColor = Color.FromArgb(239, 68, 68),
        PressedColor = Color.FromArgb(185, 28, 28),
        BorderRadius = 7,
        Size = new Size(88, 30),
        Enabled = false
    };

    private readonly Label _lblSimLoss = new()
    {
        Text = "Sim Loss:",
        AutoSize = true,
        ForeColor = TextPrimary,
        Font = new Font("Segoe UI", 9.5f)
    };
    private readonly DarkSlider _trkSimLoss = new()
    {
        Minimum = 0,
        Maximum = 100,
        Value = 0,
        Size = new Size(85, 22),
        TrackColor = Color.FromArgb(34, 50, 73),
        ProgressColor = Color.FromArgb(239, 68, 68),
        ThumbColor = Color.FromArgb(248, 113, 113)
    };
    private readonly Label _lblSimLossVal = new()
    {
        Text = "0%",
        AutoSize = true,
        ForeColor = Color.FromArgb(248, 113, 113),
        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
    };

    private readonly Label _lblSimLatency = new()
    {
        Text = "Sim Latency:",
        AutoSize = true,
        ForeColor = TextPrimary,
        Font = new Font("Segoe UI", 9.5f)
    };
    private readonly DarkSlider _trkSimLatency = new()
    {
        Minimum = 0,
        Maximum = 500,
        Value = 0,
        Size = new Size(85, 22),
        TrackColor = Color.FromArgb(34, 50, 73),
        ProgressColor = Color.FromArgb(234, 179, 8),
        ThumbColor = Color.FromArgb(250, 204, 21)
    };
    private readonly Label _lblSimLatencyVal = new()
    {
        Text = "0 ms",
        AutoSize = true,
        ForeColor = Color.FromArgb(250, 204, 21),
        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
    };

    private readonly ModernCheckBox _chkNack = new()
    {
        Text = "Enable NACK Retransmission",
        Checked = true,
        CheckColor = PrimaryBlue,
        ForeColor = TextPrimary,
        Font = new Font("Segoe UI", 9.5f),
        Size = new Size(215, 24)
    };

    private readonly ModernButton _btnResetSim = new()
    {
        Text = "Reset",
        NormalColor = Color.FromArgb(30, 41, 59),
        HoverColor = Color.FromArgb(42, 58, 82),
        PressedColor = Color.FromArgb(20, 28, 42),
        BorderRadius = 6,
        Font = new Font("Segoe UI", 8.5f),
        Size = new Size(62, 24)
    };

    private readonly ModernButton _btnToggleSim = new()
    {
        Text = "Sim Controls",
        NormalColor = Color.FromArgb(30, 41, 59),
        HoverColor = Color.FromArgb(42, 58, 82),
        PressedColor = Color.FromArgb(20, 28, 42),
        BorderRadius = 6,
        Font = new Font("Segoe UI", 9f, FontStyle.Bold),
        Size = new Size(130, 28)
    };

    private bool _simExpanded = true;

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
        Text = "RUDP Viewer App - Remote Session";
        Size = new Size(1100, 680);
        MinimumSize = new Size(760, 500);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = AppBackground;

        BuildUi();

        _receiver = new UdpFrameReceiver(listenPort: (int)_numUdpPort.Value)
        {
            EnableNack = _chkNack.Checked
        };

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
            _btnToggleSim, _btnStart, _btnStop,
            _lblSimLoss, _trkSimLoss, _lblSimLossVal,
            _lblSimLatency, _trkSimLatency, _lblSimLatencyVal,
            _chkNack, _btnResetSim
        ]);
        _topToolbar.Height = 114;

        _statusBar.Controls.Add(_lblTelemetry);

        Controls.Add(_canvas);
        Controls.Add(_statusBar);
        Controls.Add(_topToolbar);

        _canvas.BringToFront();

        _topToolbar.Paint += (_, e) =>
        {
            if (_simExpanded)
            {
                using var pen = new Pen(Color.FromArgb(28, 41, 65), 1f);
                e.Graphics.DrawLine(pen, 16, 73, _topToolbar.ClientSize.Width - 16, 73);
            }
        };

        _topToolbar.Resize += (_, _) => UpdateToolbarLayout();
        UpdateToolbarLayout();
    }

    private void ToggleSimCluster()
    {
        _simExpanded = !_simExpanded;
        _btnToggleSim.Text = "Sim Controls";
        _topToolbar.Height = _simExpanded ? 114 : 78;
        SetSimControlsVisibility(_simExpanded);
        _topToolbar.Invalidate();
    }

    private void SetSimControlsVisibility(bool visible)
    {
        _lblSimLoss.Visible = visible;
        _trkSimLoss.Visible = visible;
        _lblSimLossVal.Visible = visible;
        _lblSimLatency.Visible = visible;
        _trkSimLatency.Visible = visible;
        _lblSimLatencyVal.Visible = visible;
        _chkNack.Visible = visible;
        _btnResetSim.Visible = visible;
    }

    private void UpdateToolbarLayout()
    {
        int pad = 16;
        int w = _topToolbar.ClientSize.Width;

        int curX = pad;
        _lblHost.Location = new Point(curX, 12);
        curX = _lblHost.Right + 6;
        _txtHostIp.Location = new Point(curX, 9);
        curX = _txtHostIp.Right + 14;
        _lblUdp.Location = new Point(curX, 12);
        curX = _lblUdp.Right + 6;
        _numUdpPort.Location = new Point(curX, 9);
        curX = _numUdpPort.Right + 14;
        _lblTcp.Location = new Point(curX, 12);
        curX = _lblTcp.Right + 6;
        _numTcpPort.Location = new Point(curX, 9);

        int discX = w - pad - _btnDisconnect.Width;
        int connX = discX - 10 - _btnConnect.Width;
        if (connX < _numTcpPort.Right + 12)
        {
            connX = _numTcpPort.Right + 12;
            discX = connX + _btnConnect.Width + 10;
        }
        _btnConnect.Location = new Point(connX, 7);
        _btnDisconnect.Location = new Point(discX, 7);

        int row2X = pad;
        _lblQuality.Location = new Point(row2X, 47);
        row2X = _lblQuality.Right + 6;
        _trkQuality.Location = new Point(row2X, 45);
        row2X = _trkQuality.Right + 6;
        _lblQualityValue.Location = new Point(row2X, 47);
        row2X = _lblQualityValue.Right + 14;
        _lblMode.Location = new Point(row2X, 47);
        row2X = _lblMode.Right + 8;
        _rbRudpMode.Location = new Point(row2X, 45);
        row2X = _rbRudpMode.Right + 10;
        _rbTcpMode.Location = new Point(row2X, 45);
        row2X = _rbTcpMode.Right + 16;

        _btnToggleSim.Location = new Point(row2X, 42);

        int stopX = w - pad - _btnStop.Width;
        int startX = stopX - 10 - _btnStart.Width;
        if (startX < _btnToggleSim.Right + 12)
        {
            startX = _btnToggleSim.Right + 12;
            stopX = startX + _btnStart.Width + 10;
        }
        _btnStart.Location = new Point(startX, 42);
        _btnStop.Location = new Point(stopX, 42);

        // Row 3: Network Simulation Cluster
        int row3X = pad;
        _lblSimLoss.Location = new Point(row3X, 83);
        row3X = _lblSimLoss.Right + 6;
        _trkSimLoss.Location = new Point(row3X, 81);
        row3X = _trkSimLoss.Right + 6;
        _lblSimLossVal.Location = new Point(row3X, 83);
        row3X = _lblSimLossVal.Right + 18;

        _lblSimLatency.Location = new Point(row3X, 83);
        row3X = _lblSimLatency.Right + 6;
        _trkSimLatency.Location = new Point(row3X, 81);
        row3X = _trkSimLatency.Right + 6;
        _lblSimLatencyVal.Location = new Point(row3X, 83);
        row3X = _lblSimLatencyVal.Right + 18;

        _chkNack.Location = new Point(row3X, 80);
        row3X = _chkNack.Right + 14;

        _btnResetSim.Location = new Point(row3X, 80);
    }

    private void ApplyModeSettings(bool isTcp)
    {
        _isTcpMode = isTcp;
        if (isTcp)
        {
            _chkNack.Enabled = false;
            _receiver.EnableNack = false;
            _trkSimLoss.Enabled = false;
            _lblSimLoss.ForeColor = TextMuted;
            _lblSimLossVal.ForeColor = TextMuted;
            if (_isStreaming)
            {
                StartTcpReceiver();
            }
        }
        else
        {
            _chkNack.Enabled = true;
            _receiver.EnableNack = _chkNack.Checked;
            _trkSimLoss.Enabled = true;
            _lblSimLoss.ForeColor = TextPrimary;
            _lblSimLossVal.ForeColor = Color.FromArgb(248, 113, 113);
            StopTcpReceiver();
        }
    }

    private void HookEvents()
    {
        _trkQuality.ValueChanged += (_, _) =>
        {
            _lblQualityValue.Text = $"{_trkQuality.Value}%";
            if (!_updatingQualityFromRemote && _tcpClient.IsConnected)
                _ = _tcpClient.SendSetQualityAsync((byte)_trkQuality.Value);
        };

        _btnToggleSim.Click += (_, _) => ToggleSimCluster();

        _trkSimLoss.ValueChanged += (_, _) =>
        {
            _lblSimLossVal.Text = $"{_trkSimLoss.Value}%";
            _receiver.Chaos.DropRatePercent = _trkSimLoss.Value;
        };

        _trkSimLatency.ValueChanged += (_, _) =>
        {
            _lblSimLatencyVal.Text = $"{_trkSimLatency.Value} ms";
            _receiver.Chaos.LatencyMs = _trkSimLatency.Value;
        };

        _btnResetSim.Click += (_, _) =>
        {
            _trkSimLoss.Value = 0;
            _trkSimLatency.Value = 0;
            _chkNack.Checked = true;
            _receiver.EnableNack = true;
        };

        _chkNack.CheckedChanged += (_, _) =>
        {
            _receiver.EnableNack = _chkNack.Checked;
        };

        _rbRudpMode.CheckedChanged += (_, _) =>
        {
            if (!_rbRudpMode.Checked) return;
            ApplyModeSettings(isTcp: false);
            if (!_updatingModeFromRemote && _tcpClient.IsConnected)
                _ = _tcpClient.SendSwitchModeAsync(0x00);
        };

        _rbTcpMode.CheckedChanged += (_, _) =>
        {
            if (!_rbTcpMode.Checked) return;
            ApplyModeSettings(isTcp: true);
            if (!_updatingModeFromRemote && _tcpClient.IsConnected)
                _ = _tcpClient.SendSwitchModeAsync(0x01);
        };

        _btnConnect.Click += async (_, _) =>
        {
            if (_tcpClient.IsConnected) return;

            _btnConnect.Enabled = false;
            _canvas.StatusSubText = "Connecting to Host...";

            try
            {
                _receiver.Rebind((int)_numUdpPort.Value);
                _receiver.EnableNack = _chkNack.Checked;
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

            _isStreaming = true;
            _btnStart.Enabled = false;
            _btnStop.Enabled = true;
            _rbRudpMode.Enabled = false;
            _rbTcpMode.Enabled = false;
            _canvas.StatusSubText = "Receiving stream...";

            if (!_isTcpMode)
            {
                _receiver.EnableNack = _chkNack.Checked;
            }
            else
            {
                StartTcpReceiver();
            }

            await _tcpClient.SendStartStreamAsync();
        };

        _btnStop.Click += async (_, _) =>
        {
            if (!_tcpClient.IsConnected) return;

            _isStreaming = false;
            _btnStart.Enabled = true;
            _btnStop.Enabled = false;
            _rbRudpMode.Enabled = true;
            _rbTcpMode.Enabled = true;
            StopTcpReceiver();
            _canvas.ClearFrame("Stream stopped. Click 'Start' to receive screen.");

            await _tcpClient.SendStopStreamAsync();
        };

        _tcpClient.StartStreamReceived += () => Invoke(() =>
        {
            _isStreaming = true;
            _btnStart.Enabled = false;
            _btnStop.Enabled = true;
            _rbRudpMode.Enabled = false;
            _rbTcpMode.Enabled = false;
            _canvas.StatusSubText = "Receiving stream from Host...";

            if (_isTcpMode)
            {
                StartTcpReceiver();
            }
            else
            {
                _receiver.EnableNack = _chkNack.Checked;
            }
        });

        _tcpClient.StopStreamReceived += () => Invoke(() =>
        {
            _isStreaming = false;
            _btnStart.Enabled = true;
            _btnStop.Enabled = false;
            _rbRudpMode.Enabled = true;
            _rbTcpMode.Enabled = true;
            StopTcpReceiver();
            _canvas.ClearFrame("Stream stopped by Host. Click 'Start' to receive screen.");
        });

        _tcpClient.QualityChanged += q => Invoke(() =>
        {
            _updatingQualityFromRemote = true;
            try
            {
                _trkQuality.Value = Math.Clamp(q, _trkQuality.Minimum, _trkQuality.Maximum);
                _lblQualityValue.Text = $"{_trkQuality.Value}%";
            }
            finally
            {
                _updatingQualityFromRemote = false;
            }
        });

        _tcpClient.ModeChanged += mode => Invoke(() =>
        {
            _updatingModeFromRemote = true;
            try
            {
                if (mode == 0x01)
                {
                    _rbTcpMode.Checked = true;
                    ApplyModeSettings(isTcp: true);
                }
                else
                {
                    _rbRudpMode.Checked = true;
                    ApplyModeSettings(isTcp: false);
                }
            }
            finally
            {
                _updatingModeFromRemote = false;
            }
        });

        _tcpClient.Connected += () => Invoke(() =>
        {
            _btnConnect.Enabled = false;
            _btnDisconnect.Enabled = true;
            _txtHostIp.Enabled = false;
            _numTcpPort.Enabled = false;
            _numUdpPort.Enabled = false;
            _btnStart.Enabled = !_isStreaming;
            _btnStop.Enabled = _isStreaming;
            _trkQuality.Enabled = true;
            _canvas.StatusSubText = _isStreaming
                ? "Receiving stream from Host..."
                : "Connected. Click 'Start' to receive screen.";
        });

        _tcpClient.Disconnected += () => Invoke(() =>
        {
            _isStreaming = false;
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
            Text = $"RUDP Viewer App - Host: {msg}";
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
                int retries = 5;
                while (retries > 0 && !ct.IsCancellationRequested)
                {
                    try
                    {
                        await _tcpStreamReceiver.ConnectAsync(host, port, ct);
                        break;
                    }
                    catch when (retries > 1 && !ct.IsCancellationRequested)
                    {
                        retries--;
                        await Task.Delay(100, ct);
                    }
                }
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
                    if (_receiver.Chaos.LatencyMs > 0)
                    {
                        await Task.Delay(_receiver.Chaos.LatencyMs);
                    }

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
                if (_receiver.Chaos.LatencyMs > 0)
                {
                    await Task.Delay(_receiver.Chaos.LatencyMs, ct);
                }

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
            $"Frame: {_lastFrameId} ({_lastFrameBytes / 1024.0:F1} KB)   |   " +
            $"{_metrics.CurrentFps:F1} FPS ({_metrics.CurrentBitrateMbps:F2} Mbps)   |   " +
            $"Mode: {modeText}   |   " +
            $"OK: {r.TotalFramesCompleted}   |   " +
            $"Drop: {r.TotalFramesDroppedIncomplete}   |   " +
            $"Chaos: {chaos.DropRatePercent}% ({chaos.TotalPacketsDropped})   |   " +
            $"Lat: {_lastLatencyMs} ms   |   " +
            $"TCP: {(_tcpClient.IsConnected ? "Connected" : "Disconnected")}";
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
