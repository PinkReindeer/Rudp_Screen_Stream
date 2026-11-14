using System.Net;
using Engine.Capture;
using Engine.Transport;
using Engine.UI;

namespace HostApp;

public partial class Form1 : Form
{
    private readonly UdpFrameSender _udpSender = new();
    private readonly ScreenCapturer _capturer = new();
    private readonly TcpControlServer _tcpServer = new();
    private TcpStreamSender? _tcpStreamSender;

    private CancellationTokenSource? _streamCts;
    private IPEndPoint? _connectedClientEndpoint;
    private bool _isTcpMode;

    private static readonly Color AppBackground = Color.FromArgb(11, 17, 30);
    private static readonly Color CardBackground = Color.FromArgb(22, 34, 53);
    private static readonly Color InputBackground = Color.FromArgb(14, 23, 38);
    private static readonly Color TextPrimary = Color.FromArgb(226, 232, 240);
    private static readonly Color TextMuted = Color.FromArgb(148, 163, 184);
    private static readonly Color SuccessGreen = Color.FromArgb(34, 197, 94);
    private static readonly Color AccentCyan = Color.FromArgb(56, 189, 248);

    private readonly HostSidebar _sidebar = new() { Dock = DockStyle.Left };
    private readonly Panel _pnlContent = new() { Dock = DockStyle.Fill, BackColor = AppBackground, AutoScroll = true };

    private readonly ModernCard _cardNet = new()
    {
        CardIcon = "📶",
        Title = "Network Configuration",
        StatusBadge = "● Ready",
        StatusBadgeColor = SuccessGreen
    };
    private readonly ModernCard _cardMode = new()
    {
        CardIcon = "((•))",
        Title = "Streaming Mode"
    };
    private readonly ModernCard _cardCtrl = new()
    {
        CardIcon = "🎛️",
        Title = "Stream Controls",
        PillBadge = "85%"
    };

    private readonly TextBox _txtViewerIp = new()
    {
        Text = "127.0.0.1",
        BackColor = InputBackground,
        ForeColor = TextPrimary,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Segoe UI", 9.5f)
    };
    private readonly ModernButton _btnCopyIp = new()
    {
        Text = "❐",
        NormalColor = Color.FromArgb(30, 41, 59),
        HoverColor = Color.FromArgb(42, 58, 82),
        PressedColor = Color.FromArgb(20, 28, 42),
        BorderRadius = 6,
        Font = new Font("Segoe UI", 10.5f),
        Size = new Size(34, 26)
    };

    private readonly NumericUpDown _numUdpPort = new()
    {
        Minimum = 1000,
        Maximum = 65535,
        Value = 9000,
        BackColor = InputBackground,
        ForeColor = TextPrimary,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Segoe UI", 9.5f)
    };
    private readonly NumericUpDown _numTcpPort = new()
    {
        Minimum = 1000,
        Maximum = 65535,
        Value = 9001,
        BackColor = InputBackground,
        ForeColor = TextPrimary,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Segoe UI", 9.5f)
    };

    private readonly RadioButton _rbRudpMode = new()
    {
        Text = "RUDP Stream (Low Latency)",
        Checked = true,
        AutoSize = true,
        Font = new Font("Segoe UI", 9.5f),
        ForeColor = TextPrimary
    };
    private readonly RadioButton _rbTcpMode = new()
    {
        Text = "TCP Stream (Network Stream)",
        AutoSize = true,
        Font = new Font("Segoe UI", 9.5f),
        ForeColor = TextPrimary
    };

    private readonly Label _lblQuality = new()
    {
        Text = "JPEG Quality: 85%",
        AutoSize = true,
        Font = new Font("Segoe UI", 9.5f),
        ForeColor = TextPrimary
    };
    private readonly DarkSlider _trkQuality = new()
    {
        Minimum = 10,
        Maximum = 100,
        Value = 85,
        Height = 24,
        TrackColor = Color.FromArgb(34, 50, 73),
        ProgressColor = Color.FromArgb(37, 99, 235),
        ThumbColor = AccentCyan
    };

    private readonly Panel _pnlActions = new() { BackColor = Color.Transparent };
    private readonly ModernButton _btnStart = new()
    {
        CustomIcon = "▶",
        Text = "Start Streaming",
        NormalColor = Color.FromArgb(24, 119, 242),
        HoverColor = Color.FromArgb(37, 133, 255),
        PressedColor = Color.FromArgb(18, 98, 204),
        BorderRadius = 8,
        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
    };
    private readonly ModernButton _btnStop = new()
    {
        CustomIcon = "■",
        Text = "Stop",
        NormalColor = Color.FromArgb(220, 38, 38),
        HoverColor = Color.FromArgb(239, 68, 68),
        PressedColor = Color.FromArgb(185, 28, 28),
        BorderRadius = 8,
        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
        Enabled = false
    };
    private readonly ModernButton _btnDisconnect = new()
    {
        CustomIcon = "✕",
        Text = "Disconnect Client",
        NormalColor = Color.FromArgb(36, 48, 68),
        HoverColor = Color.FromArgb(51, 65, 85),
        PressedColor = Color.FromArgb(28, 37, 52),
        BorderRadius = 8,
        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
        Enabled = false
    };

    private readonly Label _lblStats = new()
    {
        Dock = DockStyle.Bottom,
        Height = 30,
        BackColor = Color.FromArgb(13, 21, 39),
        ForeColor = SuccessGreen,
        Font = new Font("Consolas", 8.5f, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(12, 0, 0, 0)
    };
    private readonly System.Windows.Forms.Timer _statsTimer = new() { Interval = 400 };

    public Form1()
    {
        InitializeComponent();
        Text = "↔  RUDP Host App";
        Size = new Size(540, 680);
        MinimumSize = new Size(480, 580);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = AppBackground;

        BuildUi();
        HookEvents();

        _tcpServer.Start((int)_numTcpPort.Value);
    }

    private void BuildUi()
    {
        Controls.Add(_pnlContent);
        Controls.Add(_sidebar);
        Controls.Add(_lblStats);

        var lblIp = new Label
        {
            Text = "Viewer IP:",
            Location = new Point(16, 46),
            Size = new Size(72, 22),
            ForeColor = TextPrimary,
            Font = new Font("Segoe UI", 9.5f)
        };
        _txtViewerIp.Location = new Point(94, 43);
        _txtViewerIp.Size = new Size(160, 26);
        _btnCopyIp.Location = new Point(260, 43);

        var lblUdp = new Label
        {
            Text = "UDP:",
            Location = new Point(16, 82),
            Size = new Size(72, 22),
            ForeColor = TextPrimary,
            Font = new Font("Segoe UI", 9.5f)
        };
        _numUdpPort.Location = new Point(94, 79);
        _numUdpPort.Size = new Size(200, 26);

        var lblTcp = new Label
        {
            Text = "TCP:",
            Location = new Point(16, 118),
            Size = new Size(72, 22),
            ForeColor = TextPrimary,
            Font = new Font("Segoe UI", 9.5f)
        };
        _numTcpPort.Location = new Point(94, 115);
        _numTcpPort.Size = new Size(200, 26);

        _cardNet.Controls.AddRange([lblIp, _txtViewerIp, _btnCopyIp, lblUdp, _numUdpPort, lblTcp, _numTcpPort]);
        _cardNet.Height = 160;

        _rbRudpMode.Location = new Point(16, 42);
        _rbTcpMode.Location = new Point(16, 70);
        _cardMode.Controls.AddRange([_rbRudpMode, _rbTcpMode]);
        _cardMode.Height = 104;

        _lblQuality.Location = new Point(16, 42);
        _trkQuality.Location = new Point(16, 68);
        _cardCtrl.Controls.AddRange([_lblQuality, _trkQuality]);
        _cardCtrl.Height = 108;

        _pnlActions.Controls.AddRange([_btnStart, _btnStop, _btnDisconnect]);
        _pnlActions.Height = 44;

        _pnlContent.Controls.AddRange([_cardNet, _cardMode, _cardCtrl, _pnlActions]);

        _pnlContent.Resize += (_, _) => UpdateLayout();
        UpdateLayout();
    }

    private void UpdateLayout()
    {
        int pad = 16;
        int w = Math.Max(320, _pnlContent.ClientSize.Width - pad * 2);

        _cardNet.Location = new Point(pad, pad);
        _cardNet.Width = w;

        _cardMode.Location = new Point(pad, _cardNet.Bottom + 12);
        _cardMode.Width = w;

        _cardCtrl.Location = new Point(pad, _cardMode.Bottom + 12);
        _cardCtrl.Width = w;
        _trkQuality.Width = Math.Max(100, w - 32);

        _pnlActions.Location = new Point(pad, _cardCtrl.Bottom + 16);
        _pnlActions.Width = w;

        int btnGap = 8;
        int totalBtnW = w - btnGap * 2;
        int startW = (int)(totalBtnW * 0.40f);
        int stopW = (int)(totalBtnW * 0.22f);
        int discW = totalBtnW - startW - stopW;

        _btnStart.Location = new Point(0, 0);
        _btnStart.Size = new Size(startW, 40);

        _btnStop.Location = new Point(startW + btnGap, 0);
        _btnStop.Size = new Size(stopW, 40);

        _btnDisconnect.Location = new Point(startW + stopW + btnGap * 2, 0);
        _btnDisconnect.Size = new Size(discW, 40);

        int inputW = Math.Clamp(w - 140, 140, 280);
        _txtViewerIp.Width = inputW - 40;
        _btnCopyIp.Location = new Point(_txtViewerIp.Right + 6, _txtViewerIp.Top);
        _numUdpPort.Width = inputW;
        _numTcpPort.Width = inputW;
    }

    private void HookEvents()
    {
        _btnCopyIp.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_txtViewerIp.Text))
            {
                Clipboard.SetText(_txtViewerIp.Text.Trim());
                _btnCopyIp.Text = "✓";
                Task.Delay(1000).ContinueWith(_ =>
                {
                    if (!IsDisposed && IsHandleCreated)
                        BeginInvoke(() => _btnCopyIp.Text = "❐");
                });
            }
        };

        _numTcpPort.ValueChanged += (_, _) =>
        {
            if (!_tcpServer.IsClientConnected)
                _tcpServer.Start((int)_numTcpPort.Value);
        };

        _trkQuality.ValueChanged += (_, _) =>
        {
            _lblQuality.Text = $"JPEG Quality: {_trkQuality.Value}%";
            _cardCtrl.PillBadge = $"{_trkQuality.Value}%";
            _capturer.JpegQuality = _trkQuality.Value;
        };

        _rbRudpMode.CheckedChanged += (_, _) =>
        {
            if (_rbRudpMode.Checked) _isTcpMode = false;
        };
        _rbTcpMode.CheckedChanged += (_, _) =>
        {
            if (_rbTcpMode.Checked) _isTcpMode = true;
        };

        _btnStart.Click += (_, _) => StartStreaming();
        _btnStop.Click += (_, _) => StopStreaming();
        _btnDisconnect.Click += (_, _) => _tcpServer.DisconnectClient();

        _tcpServer.ClientConnected += clientUdpEp => Invoke(() =>
        {
            _connectedClientEndpoint = clientUdpEp;
            _txtViewerIp.Text = clientUdpEp.Address.ToString();
            _numUdpPort.Value = clientUdpEp.Port;
            _cardNet.StatusBadge = "● Connected";
            _cardNet.StatusBadgeColor = SuccessGreen;
            _txtViewerIp.Enabled = false;
            _numUdpPort.Enabled = false;
            _numTcpPort.Enabled = false;
            _btnDisconnect.Enabled = true;
        });

        _tcpServer.ClientDisconnected += () => Invoke(() =>
        {
            _connectedClientEndpoint = null;
            _cardNet.StatusBadge = "● Ready";
            _cardNet.StatusBadgeColor = SuccessGreen;
            _txtViewerIp.Enabled = true;
            _numUdpPort.Enabled = true;
            _numTcpPort.Enabled = true;
            _btnDisconnect.Enabled = false;
            StopStreaming();
        });

        _tcpServer.StartStreamRequested += () => Invoke(StartStreaming);
        _tcpServer.StopStreamRequested += () => Invoke(StopStreaming);
        _tcpServer.QualityChangeRequested += q => Invoke(() =>
        {
            _trkQuality.Value = Math.Clamp(q, _trkQuality.Minimum, _trkQuality.Maximum);
            _capturer.JpegQuality = q;
        });

        _tcpServer.SwitchModeRequested += mode => Invoke(() =>
        {
            bool wasTcp = _isTcpMode;
            _isTcpMode = mode == 0x01;
            if (_isTcpMode)
                _rbTcpMode.Checked = true;
            else
                _rbRudpMode.Checked = true;

            if (_streamCts != null && wasTcp != _isTcpMode)
            {
                StopStreaming();
                StartStreaming();
            }
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
                MessageBox.Show($"Invalid Viewer IP '{_txtViewerIp.Text}'", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            endpoint = new IPEndPoint(targetIp, (int)_numUdpPort.Value);
        }

        if (_isTcpMode)
        {
            int tcpStreamPort = (int)_numUdpPort.Value + 10;
            _tcpStreamSender = new TcpStreamSender();
            _tcpStreamSender.Start(tcpStreamPort);
        }

        _streamCts = new CancellationTokenSource();
        _btnStart.Enabled = false;
        _btnStop.Enabled = true;
        _rbRudpMode.Enabled = false;
        _rbTcpMode.Enabled = false;
        _cardNet.StatusBadge = "● Streaming";
        _cardNet.StatusBadgeColor = AccentCyan;

        var token = _streamCts.Token;
        _ = Task.Run(() => StreamLoopAsync(endpoint, token), token);
    }

    private void StopStreaming()
    {
        if (_streamCts == null) return;

        _streamCts.Cancel();
        _streamCts = null;
        _btnStart.Enabled = true;
        _btnStop.Enabled = false;
        _rbRudpMode.Enabled = true;
        _rbTcpMode.Enabled = true;

        if (_connectedClientEndpoint != null)
        {
            _cardNet.StatusBadge = "● Connected";
            _cardNet.StatusBadgeColor = SuccessGreen;
        }
        else
        {
            _cardNet.StatusBadge = "● Ready";
            _cardNet.StatusBadgeColor = SuccessGreen;
        }

        _tcpStreamSender?.Dispose();
        _tcpStreamSender = null;
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

                if (_isTcpMode && _tcpStreamSender != null)
                    await _tcpStreamSender.SendFrameAsync(frameBytes, ct);
                else
                    await _udpSender.SendFrameAsync(frameBytes, ep, ct);
            }
            catch (OperationCanceledException) { break; }
            catch { }
        }
    }

    private void RefreshStats()
    {
        double mbSent;
        long pktsSent;
        string mode;

        if (_isTcpMode && _tcpStreamSender != null)
        {
            mbSent = _tcpStreamSender.TotalBytesSent / 1_048_576.0;
            pktsSent = _tcpStreamSender.TotalFramesSent;
            mode = "TCP";
        }
        else
        {
            mbSent = _udpSender.TotalBytesSent / 1_048_576.0;
            pktsSent = _udpSender.TotalPacketsSent;
            mode = "RUDP";
        }

        bool streaming = _streamCts != null;
        _lblStats.Text =
            $"Status: {(streaming ? "🔴 Streaming" : "⏸ Idle")}  |  " +
            $"Mode: {mode}  |  " +
            $"Sent: {pktsSent:N0} ({mbSent:F2} MB)  |  " +
            $"NACK: {_udpSender.TotalNacksReceived:N0}  |  " +
            $"Retx: {_udpSender.TotalRetransmittedPackets:N0}  |  " +
            $"Q: {_capturer.JpegQuality}%  |  " +
            $"TCP: {(_tcpServer.IsClientConnected ? "OK" : $":{_numTcpPort.Value}")}";
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _statsTimer.Stop();
        StopStreaming();
        _tcpServer.Dispose();
        _udpSender.Dispose();
        base.OnFormClosing(e);
    }
}
