using Engine.Transport;

namespace ViewerApp;

public partial class Form1 : Form
{
    private readonly UdpFrameReceiver _receiver;
    private readonly TcpControlClient _tcpClient = new();

    private readonly VideoCanvas _canvas = new() { Dock = DockStyle.Fill };

    private readonly TextBox _txtHostIp = new() { Text = "127.0.0.1", Width = 95, Margin = new Padding(2, 4, 4, 2) };
    private readonly NumericUpDown _numUdpPort = new() { Minimum = 1000, Maximum = 65535, Value = 9000, Width = 62, Margin = new Padding(2, 4, 4, 2) };
    private readonly NumericUpDown _numTcpPort = new() { Minimum = 1000, Maximum = 65535, Value = 9001, Width = 62, Margin = new Padding(2, 4, 4, 2) };
    private readonly Button _btnConnect = new() { Text = "Connect", AutoSize = true, BackColor = Color.SkyBlue, Margin = new Padding(2, 3, 2, 3) };
    private readonly Button _btnDisconnect = new() { Text = "Disconnect", AutoSize = true, BackColor = Color.LightCoral, Enabled = false, Margin = new Padding(2, 3, 4, 3) };
    private readonly Button _btnStart = new() { Text = "▶ Start", AutoSize = true, BackColor = Color.LightGreen, Enabled = false, Margin = new Padding(2, 3, 2, 3) };
    private readonly Button _btnStop = new() { Text = "■ Stop", AutoSize = true, BackColor = Color.Salmon, Enabled = false, Margin = new Padding(2, 3, 4, 3) };
    private readonly TrackBar _trkQuality = new() { Minimum = 10, Maximum = 100, Value = 85, Width = 90, TickFrequency = 10, Enabled = false, Margin = new Padding(0, 2, 0, 2) };
    private readonly Label _lblQuality = new() { AutoSize = true, Text = "85%", ForeColor = Color.WhiteSmoke, Padding = new Padding(0, 6, 0, 0), Margin = new Padding(0, 2, 4, 2) };
    private readonly Label _lblTcpStatus = new() { AutoSize = true, Text = "Disconnected", ForeColor = Color.OrangeRed, Padding = new Padding(2, 6, 0, 0), Margin = new Padding(0, 2, 2, 2) };

    // Chaos Proxy & NACK / RingBuffer controls
    private readonly TrackBar _trkDropRate = new() { Minimum = 0, Maximum = 100, Value = 0, SmallChange = 1, LargeChange = 5, TickFrequency = 10, Width = 100, Margin = new Padding(0, 2, 0, 2) };
    private readonly Label _lblDropRate = new() { AutoSize = true, Text = "0%", ForeColor = Color.WhiteSmoke, Padding = new Padding(0, 6, 0, 0), Margin = new Padding(0, 2, 4, 2) };
    private readonly CheckBox _chkRingBuffer = new() { Text = "RingBuffer", AutoSize = true, ForeColor = Color.FromArgb(240, 210, 100), Font = new Font("Segoe UI", 9, FontStyle.Bold), Padding = new Padding(0, 4, 0, 0), Margin = new Padding(4, 2, 4, 2), Cursor = Cursors.Hand };
    private readonly CheckBox _chkNack = new() { Text = "NACK", AutoSize = true, ForeColor = Color.FromArgb(100, 200, 255), Font = new Font("Segoe UI", 9, FontStyle.Bold), Padding = new Padding(0, 4, 0, 0), Margin = new Padding(4, 2, 4, 2), Cursor = Cursors.Hand };

    private readonly Label _lblTelemetry = new()
    {
        Dock      = DockStyle.Bottom,
        Height    = 36,
        BackColor = Color.FromArgb(15, 18, 25),
        ForeColor = Color.LimeGreen,
        Font      = new Font("Consolas", 9, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleLeft,
        Padding   = new Padding(8, 0, 0, 0)
    };

    private readonly System.Windows.Forms.Timer _telemetryTimer = new() { Interval = 250 };
    private uint _lastFrameId;
    private uint _lastLatencyMs;
    private int  _lastFrameBytes;

    public Form1()
    {
        InitializeComponent();
        Text = "RUDP Viewer App (with Chaos Proxy & NACK Retransmission)";
        Size = new Size(1200, 700);
        MinimumSize = new Size(600, 400);

        BuildUi();

        _receiver = new UdpFrameReceiver(listenPort: (int)_numUdpPort.Value);

        HookEvents();

        _ = Task.Run(DecodeAndRenderLoopAsync);

        _telemetryTimer.Tick += (_, _) => RefreshTelemetry();
        _telemetryTimer.Start();
    }

    private void BuildUi()
    {
        var controlBar = new FlowLayoutPanel
        {
            Dock          = DockStyle.Top,
            AutoSize      = true,
            AutoSizeMode  = AutoSizeMode.GrowAndShrink,
            BackColor     = Color.FromArgb(30, 34, 45),
            Padding       = new Padding(6, 4, 6, 4),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents  = true
        };

        controlBar.Controls.AddRange([
            Label("Host IP:", Color.Silver), _txtHostIp,
            Label("UDP:", Color.Silver),     _numUdpPort,
            Label("TCP:", Color.Silver),     _numTcpPort,
            _btnConnect, _btnDisconnect,
            Separator(),
            _btnStart, _btnStop,
            Separator(),
            Label("Quality:", Color.Silver), _trkQuality, _lblQuality,
            Separator(),
            Label("Drop Rate:", Color.Orange), _trkDropRate, _lblDropRate,
            Separator(),
            _chkRingBuffer,
            _chkNack,
            Separator(),
            _lblTcpStatus
        ]);

        Controls.Add(_canvas);
        Controls.Add(_lblTelemetry);
        Controls.Add(controlBar);
    }

    private static Label Label(string text, Color color) =>
        new() { Text = text, AutoSize = true, ForeColor = color, Padding = new Padding(3, 6, 0, 0), Margin = new Padding(0, 2, 2, 2) };

    private static Label Separator() =>
        new() { Text = "|", AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(2, 6, 2, 0), Margin = new Padding(0, 2, 2, 2) };

    private void HookEvents()
    {
        // When UDP Probe packet arrives from Host, acknowledge over TCP immediately
        _receiver.ProbeReceived += async _ =>
        {
            try
            {
                await _tcpClient.SendUdpProbeAckAsync();
            }
            catch { }
        };

        _trkQuality.ValueChanged += (_, _) =>
        {
            _lblQuality.Text = $"{_trkQuality.Value}%";
            if (_tcpClient.IsConnected)
                _ = _tcpClient.SendSetQualityAsync((byte)_trkQuality.Value);
        };

        _trkDropRate.ValueChanged += (_, _) =>
        {
            int rate = _trkDropRate.Value;
            _lblDropRate.Text = $"{rate}%";
            _receiver.Chaos.DropRatePercent = rate;
            _lblDropRate.ForeColor = rate > 0 ? Color.Salmon : Color.WhiteSmoke;
        };

        _chkRingBuffer.CheckedChanged += (_, _) =>
        {
            _receiver.EnableRingBuffer = _chkRingBuffer.Checked;
        };

        _chkNack.CheckedChanged += (_, _) =>
        {
            _receiver.EnableNack = _chkNack.Checked;
        };

        _btnConnect.Click += async (_, _) =>
        {
            if (_tcpClient.IsConnected) return;

            _btnConnect.Enabled     = false;
            _lblTcpStatus.Text      = "Connecting…";
            _lblTcpStatus.ForeColor = Color.Yellow;

            try
            {
                // Ensure UDP receiver is listening on the currently entered UDP port
                _receiver.Rebind((int)_numUdpPort.Value);

                // Connect with strict verification of Host IP, TCP Port, and UDP Port
                await _tcpClient.ConnectAsync(
                    _txtHostIp.Text.Trim(),
                    (int)_numTcpPort.Value,
                    (ushort)_numUdpPort.Value
                );
            }
            catch (Exception ex)
            {
                _lblTcpStatus.Text      = $"Failed: {ex.Message}";
                _lblTcpStatus.ForeColor = Color.OrangeRed;
                _btnConnect.Enabled     = true;
                _canvas.ClearFrame($"Connection Failed: {ex.Message}");
            }
        };

        _btnDisconnect.Click += async (_, _) =>
        {
            _btnDisconnect.Enabled  = false;
            _lblTcpStatus.Text      = "Disconnecting…";
            _lblTcpStatus.ForeColor = Color.Yellow;

            try
            {
                await _tcpClient.DisconnectAsync();
            }
            finally
            {
                _canvas.ClearFrame("Disconnected from Host. Click 'Connect' to reconnect.");
            }
        };

        _btnStart.Click += async (_, _) =>
        {
            if (_tcpClient.IsConnected)
            {
                await _tcpClient.SendStartStreamAsync();
                _btnStart.Enabled = false;
                _btnStop.Enabled  = true;
            }
        };

        _btnStop.Click += async (_, _) =>
        {
            if (_tcpClient.IsConnected)
            {
                await _tcpClient.SendStopStreamAsync();
                _btnStart.Enabled = true;
                _btnStop.Enabled  = false;
            }
        };

        _tcpClient.Connected += () => Invoke(() =>
        {
            _lblTcpStatus.Text      = "● Connected";
            _lblTcpStatus.ForeColor = Color.LimeGreen;
            _btnConnect.Enabled     = false;
            _btnDisconnect.Enabled  = true;
            _txtHostIp.Enabled      = false;
            _numTcpPort.Enabled     = false;
            _numUdpPort.Enabled     = false;
            _btnStart.Enabled       = true;
            _btnStop.Enabled        = false;
            _trkQuality.Enabled     = true;
            _canvas.StatusText      = "Connected to Host. Click 'Start Stream' to receive screen.";
        });

        _tcpClient.Disconnected += () => Invoke(() =>
        {
            _lblTcpStatus.Text      = "Disconnected";
            _lblTcpStatus.ForeColor = Color.OrangeRed;
            _btnConnect.Enabled     = true;
            _btnDisconnect.Enabled  = false;
            _txtHostIp.Enabled      = true;
            _numTcpPort.Enabled     = true;
            _numUdpPort.Enabled     = true;
            _btnStart.Enabled       = false;
            _btnStop.Enabled        = false;
            _trkQuality.Enabled     = false;
            _canvas.ClearFrame("Disconnected from Host. Click 'Connect' to reconnect.");
        });

        _tcpClient.StatusReceived += msg => Invoke(() =>
        {
            _lblTelemetry.ForeColor = Color.Cyan;
            Text = $"RUDP Viewer – Server says: {msg}";
        });
    }

    private async Task DecodeAndRenderLoopAsync()
    {
        await foreach (var frame in _receiver.CompletedFrames.ReadAllAsync())
        {
            using (frame)
            {
                try
                {
                    using var ms  = new MemoryStream(frame.PooledBuffer, 0, frame.Length, writable: false);
                    var bmp = new Bitmap(ms);

                    _lastFrameId    = frame.FrameID;
                    _lastFrameBytes = frame.Length;
                    _lastLatencyMs  = (uint)Environment.TickCount64 - frame.TimestampMs;

                    _canvas.UpdateFrame(bmp);
                    Invoke(() => _lblTelemetry.ForeColor = Color.LimeGreen);
                }
                catch { }
            }
        }
    }

    private void RefreshTelemetry()
    {
        _receiver.Metrics.UpdateSnapshot();
        var r = _receiver.Reassembler;
        var chaos = _receiver.Chaos;
        var nack = _receiver.NackTracker;
        var m = _receiver.Metrics;

        _lblTelemetry.Text =
            $"FPS: {m.CurrentFps:F1} ({m.CurrentBitrateMbps:F2} Mbps)  |  " +
            $"Frame #{_lastFrameId} ({_lastFrameBytes / 1024.0:F1} KB)  |  " +
            $"Dropped: {r.TotalFramesDroppedIncomplete}  |  " +
            $"Chaos Drop: {chaos.DropRatePercent}% ({chaos.TotalPacketsDropped} pkts)  |  " +
            $"NACK: {(nack.IsEnabled ? "ON" : "OFF")} (Sent: {nack.TotalNacksSent}, Recv: {nack.TotalRecovered})  |  " +
            $"RingBuffer: {(r.EnableRingBuffer ? "ON" : "OFF")}  |  " +
            $"Latency: {_lastLatencyMs} ms  |  " +
            $"TCP: {(_tcpClient.IsConnected ? "✓" : "✗")}";
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _telemetryTimer.Stop();
        _tcpClient.Dispose();
        _receiver.Dispose();
        base.OnFormClosing(e);
    }
}
