using System.Net;
using Engine.Capture;
using Engine.Transport;
using Protocol.Control;

namespace HostApp;

public partial class Form1 : Form
{
    // ── Core services ──────────────────────────────────────────────────────────
    private readonly UdpFrameSender    _sender        = new();
    private readonly ScreenCapturer    _capturer      = new();
    private readonly TcpControlServer  _tcpServer     = new();

    // ── Streaming state ────────────────────────────────────────────────────────
    private CancellationTokenSource? _streamCts;

    // ── UI controls ────────────────────────────────────────────────────────────
    private readonly TextBox       _txtViewerIp         = new() { Text = "127.0.0.1", Width = 115 };
    private readonly NumericUpDown _numUdpPort          = new() { Minimum = 1000, Maximum = 65535, Value = 9000, Width = 75 };
    private readonly NumericUpDown _numTcpPort          = new() { Minimum = 1000, Maximum = 65535, Value = 9001, Width = 75 };
    private readonly TrackBar      _trkQuality          = new() { Minimum = 10, Maximum = 100, Value = 85, Width = 180, TickFrequency = 10 };
    private readonly Label         _lblQuality          = new() { AutoSize = true, Text = "JPEG Quality: 85%" };
    private readonly Button        _btnStartStream      = new() { Text = "▶ Start Stream", AutoSize = true, BackColor = Color.LightGreen };
    private readonly Button        _btnStopStream       = new() { Text = "■ Stop Stream",  AutoSize = true, BackColor = Color.Salmon, Enabled = false };
    private readonly Button        _btnDisconnectClient = new() { Text = "✕ Disconnect Client", AutoSize = true, BackColor = Color.LightCoral, Enabled = false };
    private readonly Label         _lblClientStatus     = new() { AutoSize = true, Text = "● No client connected", ForeColor = Color.OrangeRed };
    private readonly Label         _lblStats            = new()
    {
        Dock = DockStyle.Bottom, Height = 36,
        BackColor = Color.FromArgb(20, 24, 35),
        ForeColor = Color.LimeGreen,
        Font = new Font("Consolas", 9, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(8, 0, 0, 0)
    };
    private readonly System.Windows.Forms.Timer _statsTimer = new() { Interval = 500 };

    public Form1()
    {
        InitializeComponent();
        Text = "RUDP Host App";
        Size = new Size(720, 280);
        MinimumSize = Size;

        BuildUi();
        HookEvents();

        // Configure TCP server validation: Strictly verify Viewer IP, UDP Port, and Target Host IP
        _tcpServer.ClientValidator = ValidateIncomingClient;
        _tcpServer.UdpProbeSender  = async ep => await _sender.SendProbeAsync(ep);

        // Start listening on configured TCP port
        _tcpServer.Start((int)_numTcpPort.Value);
    }

    private (bool Valid, string Error) ValidateIncomingClient(IPAddress clientIp, ushort clientUdpPort, string targetHostIp)
    {
        // 1. Validate Target Host IP specified by Viewer
        var validHostIps = ControlProtocol.GetLocalHostIps();
        if (!string.IsNullOrEmpty(targetHostIp) && !validHostIps.Contains(targetHostIp))
        {
            return (false, $"Host IP mismatch: Viewer dialed '{targetHostIp}', but this Host IP is {string.Join(" or ", validHostIps)}!");
        }

        // 2. Validate Viewer IP configured on Host
        string configuredIpStr = _txtViewerIp.Text.Trim();
        if (!IPAddress.TryParse(configuredIpStr, out var configuredIp))
        {
            return (false, $"Host configuration error: Invalid Viewer IP '{configuredIpStr}'");
        }

        // EXACT IP comparison (normalizing IPv4-mapped IPv6)
        IPAddress normConfigured = configuredIp.IsIPv4MappedToIPv6 ? configuredIp.MapToIPv4() : configuredIp;
        IPAddress normClient     = clientIp.IsIPv4MappedToIPv6 ? clientIp.MapToIPv4() : clientIp;

        if (!normConfigured.Equals(normClient))
        {
            return (false, $"Viewer IP mismatch: Host expects Viewer IP '{normConfigured}', but connection arrived from '{normClient}'!");
        }

        // 3. Validate UDP Port
        ushort expectedUdpPort = (ushort)_numUdpPort.Value;
        if (clientUdpPort != expectedUdpPort)
        {
            return (false, $"UDP Port mismatch: Host configured UDP Port is {expectedUdpPort}, but Viewer is on {clientUdpPort}!");
        }

        return (true, string.Empty);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // UI construction
    // ─────────────────────────────────────────────────────────────────────────

    private void BuildUi()
    {
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(12, 10, 12, 4),
            FlowDirection = FlowDirection.TopDown, WrapContents = false
        };

        // Row 1 – addresses
        var row1 = Row();
        row1.Controls.AddRange([
            Label("Viewer IP:"), _txtViewerIp,
            Label("UDP Port:"), _numUdpPort,
            Label("TCP Port:"), _numTcpPort
        ]);

        // Row 2 – quality
        var row2 = Row();
        row2.Controls.AddRange([_lblQuality, _trkQuality]);

        // Row 3 – controls + status
        var row3 = Row();
        row3.Controls.AddRange([_btnStartStream, _btnStopStream, _btnDisconnectClient, _lblClientStatus]);

        flow.Controls.AddRange([row1, row2, row3]);
        Controls.Add(flow);
        Controls.Add(_lblStats);
    }

    private static FlowLayoutPanel Row() =>
        new() { AutoSize = true, Margin = new Padding(0, 4, 0, 4) };

    private static Label Label(string text) =>
        new() { Text = text, AutoSize = true, Padding = new Padding(4, 5, 0, 0) };

    // ─────────────────────────────────────────────────────────────────────────
    // Event wiring
    // ─────────────────────────────────────────────────────────────────────────

    private void HookEvents()
    {
        // When TCP Port is changed on Host, restart listener if idle
        _numTcpPort.ValueChanged += (_, _) =>
        {
            if (!_tcpServer.IsClientConnected)
            {
                _tcpServer.Start((int)_numTcpPort.Value);
            }
        };

        // Quality slider
        _trkQuality.ValueChanged += (_, _) =>
        {
            _lblQuality.Text = $"JPEG Quality: {_trkQuality.Value}%";
            _capturer.JpegQuality = _trkQuality.Value;
        };

        // Manual stream buttons
        _btnStartStream.Click += (_, _) => StartStreaming();
        _btnStopStream.Click  += (_, _) => StopStreaming();
        _btnDisconnectClient.Click += (_, _) => _tcpServer.DisconnectClient();

        // TCP server events (may arrive on thread-pool thread → Invoke to UI)
        _tcpServer.ClientConnected += (remoteEp, viewerUdpPort) => Invoke(() =>
        {
            _lblClientStatus.Text = $"● Viewer connected ({remoteEp.Address}:{viewerUdpPort})";
            _lblClientStatus.ForeColor = Color.LimeGreen;

            // Lock endpoint parameters while connected
            _txtViewerIp.Enabled = false;
            _numUdpPort.Enabled = false;
            _numTcpPort.Enabled = false;
            _btnDisconnectClient.Enabled = true;
        });

        _tcpServer.ClientDisconnected += () => Invoke(() =>
        {
            _lblClientStatus.Text      = "● No client connected";
            _lblClientStatus.ForeColor = Color.OrangeRed;

            // Re-enable configuration controls
            _txtViewerIp.Enabled = true;
            _numUdpPort.Enabled = true;
            _numTcpPort.Enabled = true;
            _btnDisconnectClient.Enabled = false;

            StopStreaming();
        });

        _tcpServer.StartStreamRequested  += () => Invoke(StartStreaming);
        _tcpServer.StopStreamRequested   += () => Invoke(StopStreaming);
        _tcpServer.QualityChangeRequested += q => Invoke(() =>
        {
            _trkQuality.Value    = Math.Clamp(q, _trkQuality.Minimum, _trkQuality.Maximum);
            _capturer.JpegQuality = q;
        });

        // Stats ticker
        _statsTimer.Tick += (_, _) => RefreshStats();
        _statsTimer.Start();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Streaming logic
    // ─────────────────────────────────────────────────────────────────────────

    private void StartStreaming()
    {
        if (_streamCts != null) return;   // Already running

        if (!IPAddress.TryParse(_txtViewerIp.Text.Trim(), out var targetIp))
        {
            MessageBox.Show($"Invalid Viewer IP '{_txtViewerIp.Text}'", "Streaming Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _streamCts = new CancellationTokenSource();
        _btnStartStream.Enabled = false;
        _btnStopStream.Enabled  = true;

        var endpoint = new IPEndPoint(targetIp, (int)_numUdpPort.Value);

        var token = _streamCts.Token;
        _ = Task.Run(() => StreamLoopAsync(endpoint, token), token);
    }

    private void StopStreaming()
    {
        if (_streamCts == null) return;

        _streamCts.Cancel();
        _streamCts = null;
        _btnStartStream.Enabled = true;
        _btnStopStream.Enabled  = false;
    }

    private async Task StreamLoopAsync(IPEndPoint ep, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(33));
        while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                using var ms = _capturer.CaptureJpeg();
                var frameBytes = ms.ToArray();
                await _sender.SendFrameAsync(frameBytes, ep, ct);
            }
            catch (OperationCanceledException) { break; }
            catch { }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Stats
    // ─────────────────────────────────────────────────────────────────────────

    private void RefreshStats()
    {
        double mbSent = _sender.TotalBytesSent / 1_048_576.0;
        bool streaming = _streamCts != null;
        _lblStats.Text =
            $"Status: {(streaming ? "🔴 Streaming" : "⏸ Idle")}  |  " +
            $"Packets sent: {_sender.TotalPacketsSent:N0}  |  " +
            $"Data sent: {mbSent:F2} MB  |  " +
            $"Quality: {_capturer.JpegQuality}%  |  " +
            $"TCP: {(_tcpServer.IsClientConnected ? "Client connected" : $"Listening on :{_numTcpPort.Value}")}";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Cleanup
    // ─────────────────────────────────────────────────────────────────────────

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _statsTimer.Stop();
        StopStreaming();
        _tcpServer.Dispose();
        _sender.Dispose();
        base.OnFormClosing(e);
    }
}
