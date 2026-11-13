using System.Net;
using Engine.Capture;
using Engine.Transport;

namespace HostApp;

public partial class Form1 : Form
{
    private readonly UdpFrameSender _sender = new();
    private readonly ScreenCapturer _capturer = new();
    private readonly TcpControlServer _tcpServer = new();

    private CancellationTokenSource? _streamCts;
    private IPEndPoint? _connectedClientEndpoint;

    private readonly TextBox _txtViewerIp = new() { Text = "127.0.0.1", Width = 110 };
    private readonly NumericUpDown _numUdpPort = new() { Minimum = 1000, Maximum = 65535, Value = 9000, Width = 70 };
    private readonly NumericUpDown _numTcpPort = new() { Minimum = 1000, Maximum = 65535, Value = 9001, Width = 70 };
    private readonly TrackBar _trkQuality = new() { Minimum = 10, Maximum = 100, Value = 85, Width = 160, TickFrequency = 10 };
    private readonly Label _lblQuality = new() { AutoSize = true, Text = "JPEG Quality: 85%" };
    private readonly Button _btnStartStream = new() { Text = "▶ Start Stream", AutoSize = true, BackColor = Color.LightGreen };
    private readonly Button _btnStopStream = new() { Text = "■ Stop Stream", AutoSize = true, BackColor = Color.Salmon, Enabled = false };
    private readonly Button _btnDisconnectClient = new() { Text = "✕ Disconnect Client", AutoSize = true, BackColor = Color.LightCoral, Enabled = false };
    private readonly Label _lblClientStatus = new() { AutoSize = true, Text = "● No client connected", ForeColor = Color.OrangeRed };
    private readonly Label _lblStats = new()
    {
        Dock = DockStyle.Bottom,
        Height = 36,
        BackColor = Color.FromArgb(20, 24, 35),
        ForeColor = Color.LimeGreen,
        Font = new Font("Consolas", 9, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(8, 0, 0, 0)
    };
    private readonly System.Windows.Forms.Timer _statsTimer = new() { Interval = 400 };

    public Form1()
    {
        InitializeComponent();
        Text = "RUDP Host App";
        Size = new Size(760, 280);
        MinimumSize = Size;

        BuildUi();
        HookEvents();

        _tcpServer.Start((int)_numTcpPort.Value);
    }

    private void BuildUi()
    {
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 10, 12, 4),
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };

        var row1 = Row();
        row1.Controls.AddRange([
            Label("Viewer IP:"), _txtViewerIp,
            Label("UDP Port:"), _numUdpPort,
            Label("TCP Port:"), _numTcpPort
        ]);

        var row2 = Row();
        row2.Controls.AddRange([_lblQuality, _trkQuality]);

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

    private void HookEvents()
    {
        _numTcpPort.ValueChanged += (_, _) =>
        {
            if (!_tcpServer.IsClientConnected)
            {
                _tcpServer.Start((int)_numTcpPort.Value);
            }
        };

        _trkQuality.ValueChanged += (_, _) =>
        {
            _lblQuality.Text = $"JPEG Quality: {_trkQuality.Value}%";
            _capturer.JpegQuality = _trkQuality.Value;
        };

        _btnStartStream.Click += (_, _) => StartStreaming();
        _btnStopStream.Click += (_, _) => StopStreaming();
        _btnDisconnectClient.Click += (_, _) => _tcpServer.DisconnectClient();

        _tcpServer.ClientConnected += clientUdpEp => Invoke(() =>
        {
            _connectedClientEndpoint = clientUdpEp;
            _txtViewerIp.Text = clientUdpEp.Address.ToString();
            _numUdpPort.Value = clientUdpEp.Port;
            _lblClientStatus.Text = $"● Viewer connected ({clientUdpEp})";
            _lblClientStatus.ForeColor = Color.LimeGreen;

            _txtViewerIp.Enabled = false;
            _numUdpPort.Enabled = false;
            _numTcpPort.Enabled = false;
            _btnDisconnectClient.Enabled = true;
        });

        _tcpServer.ClientDisconnected += () => Invoke(() =>
        {
            _connectedClientEndpoint = null;
            _lblClientStatus.Text = "● No client connected";
            _lblClientStatus.ForeColor = Color.OrangeRed;

            _txtViewerIp.Enabled = true;
            _numUdpPort.Enabled = true;
            _numTcpPort.Enabled = true;
            _btnDisconnectClient.Enabled = false;

            StopStreaming();
        });

        _tcpServer.StartStreamRequested += () => Invoke(StartStreaming);
        _tcpServer.StopStreamRequested += () => Invoke(StopStreaming);
        _tcpServer.QualityChangeRequested += q => Invoke(() =>
        {
            _trkQuality.Value = Math.Clamp(q, _trkQuality.Minimum, _trkQuality.Maximum);
            _capturer.JpegQuality = q;
        });

        _statsTimer.Tick += (_, _) => RefreshStats();
        _statsTimer.Start();
    }

    private void StartStreaming()
    {
        if (_streamCts != null) return;

        IPEndPoint endpoint;
        if (_connectedClientEndpoint != null)
        {
            endpoint = _connectedClientEndpoint;
        }
        else
        {
            if (!IPAddress.TryParse(_txtViewerIp.Text.Trim(), out var targetIp))
            {
                MessageBox.Show($"Invalid Viewer IP '{_txtViewerIp.Text}'", "Streaming Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            endpoint = new IPEndPoint(targetIp, (int)_numUdpPort.Value);
        }

        _streamCts = new CancellationTokenSource();
        _btnStartStream.Enabled = false;
        _btnStopStream.Enabled = true;

        var token = _streamCts.Token;
        _ = Task.Run(() => StreamLoopAsync(endpoint, token), token);
    }

    private void StopStreaming()
    {
        if (_streamCts == null) return;

        _streamCts.Cancel();
        _streamCts = null;
        _btnStartStream.Enabled = true;
        _btnStopStream.Enabled = false;
    }

    private async Task StreamLoopAsync(IPEndPoint ep, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(33)); // ~30 FPS
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

    private void RefreshStats()
    {
        double mbSent = _sender.TotalBytesSent / 1_048_576.0;
        bool streaming = _streamCts != null;
        _lblStats.Text =
            $"Status: {(streaming ? "🔴 Streaming" : "⏸ Idle")}  |  " +
            $"Sent: {_sender.TotalPacketsSent:N0} pkts ({mbSent:F2} MB)  |  " +
            $"NACK recv: {_sender.TotalNacksReceived:N0}  |  " +
            $"Retransmit: {_sender.TotalRetransmittedPackets:N0}  |  " +
            $"Quality: {_capturer.JpegQuality}%  |  " +
            $"TCP: {(_tcpServer.IsClientConnected ? "Connected" : $":{_numTcpPort.Value}")}";
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _statsTimer.Stop();
        StopStreaming();
        _tcpServer.Dispose();
        _sender.Dispose();
        base.OnFormClosing(e);
    }
}
