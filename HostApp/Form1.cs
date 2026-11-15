using System.Net;
using Engine.Capture;
using Engine.Transport;
using Engine.UI;
using Protocol.Control;

namespace HostApp;

public partial class Form1 : Form
{
    private readonly UdpFrameSender _sender = new();
    private readonly ScreenCapturer _capturer = new();
    private readonly TcpControlServer _tcpServer = new();
    private TcpStreamSender? _tcpStreamSender;

    private CancellationTokenSource? _streamCts;
    private IPEndPoint? _connectedClientEndpoint;
    private bool _isTcpMode;
    private bool _updatingQualityFromRemote;
    private bool _updatingModeFromRemote;

    private static readonly Color AppBackground = Color.FromArgb(11, 17, 30);
    private static readonly Color CardBackground = Color.FromArgb(22, 34, 53);
    private static readonly Color InputBackground = Color.FromArgb(14, 23, 38);
    private static readonly Color TextPrimary = Color.FromArgb(226, 232, 240);
    private static readonly Color TextMuted = Color.FromArgb(148, 163, 184);
    private static readonly Color SuccessGreen = Color.FromArgb(34, 197, 94);
    private static readonly Color AccentCyan = Color.FromArgb(56, 189, 248);

    private readonly Panel _pnlContent = new() { Dock = DockStyle.Fill, BackColor = AppBackground, AutoScroll = true };

    private readonly ModernCard _cardNet = new()
    {
        Title = "Network Configuration",
        StatusBadge = "Ready",
        StatusBadgeColor = SuccessGreen
    };
    private readonly ModernCard _cardMode = new()
    {
        Title = "Streaming Mode"
    };
    private readonly ModernCard _cardCtrl = new()
    {
        Title = "Stream Controls",
        PillBadge = "85%"
    };

    private readonly Label _lblIp = new()
    {
        Text = "Host Local IP:",
        AutoSize = true,
        ForeColor = TextPrimary,
        Font = new Font("Segoe UI", 9.5f)
    };
    private readonly Label _lblUdp = new()
    {
        Text = "UDP:",
        AutoSize = true,
        ForeColor = TextPrimary,
        Font = new Font("Segoe UI", 9.5f)
    };
    private readonly Label _lblTcp = new()
    {
        Text = "TCP:",
        AutoSize = true,
        ForeColor = TextPrimary,
        Font = new Font("Segoe UI", 9.5f)
    };

    private readonly TextBox _txtHostIp = new()
    {
        Text = NetworkUtils.GetLocalIpAddress(),
        ReadOnly = true,
        BackColor = InputBackground,
        ForeColor = TextPrimary,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Segoe UI", 9.5f)
    };
    private readonly ModernButton _btnCopyIp = new()
    {
        Text = "Copy",
        NormalColor = Color.FromArgb(30, 41, 59),
        HoverColor = Color.FromArgb(42, 58, 82),
        PressedColor = Color.FromArgb(20, 28, 42),
        BorderRadius = 6,
        Font = new Font("Segoe UI", 9f, FontStyle.Bold),
        Size = new Size(56, 26)
    };
    private readonly Label _lblConnectedViewer = new()
    {
        Text = "",
        AutoSize = true,
        ForeColor = SuccessGreen,
        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
        Visible = false
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
        Text = "Start Streaming",
        NormalColor = Color.FromArgb(24, 119, 242),
        HoverColor = Color.FromArgb(37, 133, 255),
        PressedColor = Color.FromArgb(18, 98, 204),
        BorderRadius = 8,
        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
        Enabled = false
    };
    private readonly ModernButton _btnStop = new()
    {
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
        Text = "Disconnect Client",
        NormalColor = Color.FromArgb(36, 48, 68),
        HoverColor = Color.FromArgb(51, 65, 85),
        PressedColor = Color.FromArgb(28, 37, 52),
        BorderRadius = 8,
        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
        Enabled = false
    };

    public Form1()
    {
        InitializeComponent();
        Text = "RUDP Host App";
        Size = new Size(540, 580);
        MinimumSize = new Size(460, 520);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = AppBackground;

        BuildUi();
        HookEvents();

        _tcpServer.Start((int)_numTcpPort.Value);
    }

    private void BuildUi()
    {
        Controls.Add(_pnlContent);

        _cardNet.Controls.AddRange([_lblIp, _txtHostIp, _btnCopyIp, _lblUdp, _numUdpPort, _lblTcp, _numTcpPort, _lblConnectedViewer]);
        _cardMode.Controls.AddRange([_rbRudpMode, _rbTcpMode]);
        _cardCtrl.Controls.AddRange([_lblQuality, _trkQuality]);
        _pnlActions.Controls.AddRange([_btnStart, _btnStop, _btnDisconnect]);

        _pnlContent.Controls.AddRange([_cardNet, _cardMode, _cardCtrl, _pnlActions]);

        _pnlContent.Resize += (_, _) => UpdateLayout();
        UpdateLayout();
    }

    private void UpdateLayout()
    {
        int clientW = _pnlContent.ClientSize.Width;
        int clientH = _pnlContent.ClientSize.Height;

        int cardW = Math.Clamp(clientW - 36, 360, 480);
        int cardX = Math.Max(16, (clientW - cardW) / 2);

        int totalContentH = 185 + 12 + 100 + 12 + 104 + 16 + 40;
        int startY = Math.Max(20, (clientH - totalContentH) / 2);

        _cardNet.Location = new Point(cardX, startY);
        _cardNet.Size = new Size(cardW, 185);

        int pad = 16;
        int labelW = Math.Max(_lblIp.PreferredWidth, Math.Max(_lblUdp.PreferredWidth, _lblTcp.PreferredWidth)) + 12;
        int inputX = pad + labelW;
        int btnW = 56;
        int totalInputW = cardW - inputX - pad;
        int txtIpW = totalInputW - btnW - 8;

        _lblIp.Location = new Point(pad, 47);
        _txtHostIp.Location = new Point(inputX, 43);
        _txtHostIp.Size = new Size(txtIpW, 26);

        _btnCopyIp.Location = new Point(_txtHostIp.Right + 8, 43);
        _btnCopyIp.Size = new Size(btnW, 26);

        _lblUdp.Location = new Point(pad, 85);
        _numUdpPort.Location = new Point(inputX, 81);
        _numUdpPort.Size = new Size(totalInputW, 26);

        _lblTcp.Location = new Point(pad, 123);
        _numTcpPort.Location = new Point(inputX, 119);
        _numTcpPort.Size = new Size(totalInputW, 26);

        _lblConnectedViewer.Location = new Point(pad, 157);

        _cardMode.Location = new Point(cardX, _cardNet.Bottom + 12);
        _cardMode.Size = new Size(cardW, 100);
        _rbRudpMode.Location = new Point(pad, 40);
        _rbTcpMode.Location = new Point(pad, 68);

        _cardCtrl.Location = new Point(cardX, _cardMode.Bottom + 12);
        _cardCtrl.Size = new Size(cardW, 104);
        _lblQuality.Location = new Point(pad, 40);
        _trkQuality.Location = new Point(pad, 66);
        _trkQuality.Size = new Size(cardW - pad * 2, 24);

        _pnlActions.Location = new Point(cardX, _cardCtrl.Bottom + 16);
        _pnlActions.Size = new Size(cardW, 40);

        int btnGap = 8;
        int totalBtnW = cardW - btnGap * 2;
        int startW = (int)(totalBtnW * 0.42f);
        int stopW = (int)(totalBtnW * 0.20f);
        int discW = totalBtnW - startW - stopW;

        _btnStart.Location = new Point(0, 0);
        _btnStart.Size = new Size(startW, 40);

        _btnStop.Location = new Point(startW + btnGap, 0);
        _btnStop.Size = new Size(stopW, 40);

        _btnDisconnect.Location = new Point(startW + stopW + btnGap * 2, 0);
        _btnDisconnect.Size = new Size(discW, 40);
    }

    private void HookEvents()
    {
        _btnCopyIp.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_txtHostIp.Text))
            {
                try
                {
                    Clipboard.SetText(_txtHostIp.Text.Trim());
                }
                catch { }

                _btnCopyIp.Text = "Copied!";
                Task.Delay(1000).ContinueWith(_ =>
                {
                    if (!IsDisposed && IsHandleCreated)
                        BeginInvoke(() => _btnCopyIp.Text = "Copy");
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

            if (!_updatingQualityFromRemote && _tcpServer.IsClientConnected)
            {
                _ = _tcpServer.SendSetQualityAsync((byte)_trkQuality.Value);
            }
        };

        _rbRudpMode.CheckedChanged += (_, _) =>
        {
            if (!_rbRudpMode.Checked) return;
            bool wasTcp = _isTcpMode;
            _isTcpMode = false;

            if (!_updatingModeFromRemote)
            {
                if (_tcpServer.IsClientConnected)
                {
                    _ = _tcpServer.SendSwitchModeAsync(0x00);
                }
                if (_streamCts != null && wasTcp)
                {
                    StopStreaming(notifyClient: false);
                    StartStreaming(notifyClient: false);
                }
            }
        };

        _rbTcpMode.CheckedChanged += (_, _) =>
        {
            if (!_rbTcpMode.Checked) return;
            bool wasTcp = _isTcpMode;
            _isTcpMode = true;

            if (!_updatingModeFromRemote)
            {
                if (_tcpServer.IsClientConnected)
                {
                    _ = _tcpServer.SendSwitchModeAsync(0x01);
                }
                if (_streamCts != null && !wasTcp)
                {
                    StopStreaming(notifyClient: false);
                    StartStreaming(notifyClient: false);
                }
            }
        };

        _btnStart.Click += (_, _) => StartStreaming(notifyClient: true);
        _btnStop.Click += (_, _) => StopStreaming(notifyClient: true);
        _btnDisconnect.Click += (_, _) => _tcpServer.DisconnectClient();

        _tcpServer.ClientConnected += clientUdpEp => Invoke(() =>
        {
            _connectedClientEndpoint = clientUdpEp;
            _lblConnectedViewer.Text = $"Connected Viewer: {clientUdpEp.Address}";
            _lblConnectedViewer.Visible = true;
            _numUdpPort.Value = clientUdpEp.Port;
            _cardNet.StatusBadge = "Connected";
            _cardNet.StatusBadgeColor = SuccessGreen;

            _numUdpPort.Enabled = false;
            _numTcpPort.Enabled = false;

            _btnStart.Enabled = _streamCts == null;
            _btnStop.Enabled = _streamCts != null;
            _btnDisconnect.Enabled = true;

            // Synchronize host settings to viewer upon connection
            _ = _tcpServer.SendSetQualityAsync((byte)_trkQuality.Value);
            _ = _tcpServer.SendSwitchModeAsync(_isTcpMode ? (byte)0x01 : (byte)0x00);
            if (_streamCts != null)
            {
                _ = _tcpServer.SendStartStreamAsync();
            }
        });

        _tcpServer.ClientDisconnected += () => Invoke(() =>
        {
            _connectedClientEndpoint = null;
            _lblConnectedViewer.Text = string.Empty;
            _lblConnectedViewer.Visible = false;
            _cardNet.StatusBadge = "Ready";
            _cardNet.StatusBadgeColor = SuccessGreen;

            _numUdpPort.Enabled = true;
            _numTcpPort.Enabled = true;

            _btnStart.Enabled = false;
            _btnStop.Enabled = false;
            _btnDisconnect.Enabled = false;
            StopStreaming();
        });

        _tcpServer.StartStreamRequested += () => Invoke(() => StartStreaming(notifyClient: false));
        _tcpServer.StopStreamRequested += () => Invoke(() => StopStreaming(notifyClient: false));
        _tcpServer.QualityChangeRequested += q => Invoke(() =>
        {
            _updatingQualityFromRemote = true;
            try
            {
                _trkQuality.Value = Math.Clamp(q, _trkQuality.Minimum, _trkQuality.Maximum);
                _lblQuality.Text = $"JPEG Quality: {_trkQuality.Value}%";
                _cardCtrl.PillBadge = $"{_trkQuality.Value}%";
                _capturer.JpegQuality = q;
            }
            finally
            {
                _updatingQualityFromRemote = false;
            }
        });

        _tcpServer.SwitchModeRequested += mode => Invoke(() =>
        {
            bool wasTcp = _isTcpMode;
            _isTcpMode = mode == 0x01;
            _updatingModeFromRemote = true;
            try
            {
                if (_isTcpMode)
                    _rbTcpMode.Checked = true;
                else
                    _rbRudpMode.Checked = true;
            }
            finally
            {
                _updatingModeFromRemote = false;
            }

            if (_streamCts != null && wasTcp != _isTcpMode)
            {
                StopStreaming(notifyClient: false);
                StartStreaming(notifyClient: false);
            }
        });
    }

    private void StartStreaming(bool notifyClient = true)
    {
        if (_streamCts != null || _connectedClientEndpoint == null) return;

        IPEndPoint endpoint = _connectedClientEndpoint;

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
        _cardNet.StatusBadge = "Streaming";
        _cardNet.StatusBadgeColor = AccentCyan;

        if (notifyClient && _tcpServer.IsClientConnected)
        {
            _ = _tcpServer.SendStartStreamAsync();
        }

        var token = _streamCts.Token;
        _ = Task.Run(() => StreamLoopAsync(endpoint, token), token);
    }

    private void StopStreaming(bool notifyClient = true)
    {
        if (_streamCts == null) return;

        _streamCts.Cancel();
        _streamCts = null;

        _btnStart.Enabled = _connectedClientEndpoint != null;
        _btnStop.Enabled = false;
        _rbRudpMode.Enabled = true;
        _rbTcpMode.Enabled = true;

        if (_connectedClientEndpoint != null)
        {
            _cardNet.StatusBadge = "Connected";
            _cardNet.StatusBadgeColor = SuccessGreen;
        }
        else
        {
            _cardNet.StatusBadge = "Ready";
            _cardNet.StatusBadgeColor = SuccessGreen;
        }

        if (notifyClient && _tcpServer.IsClientConnected)
        {
            _ = _tcpServer.SendStopStreamAsync();
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
                    await _sender.SendFrameAsync(frameBytes, ep, ct);
            }
            catch (OperationCanceledException) { break; }
            catch { }
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        StopStreaming();
        _tcpServer.Dispose();
        _sender.Dispose();
        base.OnFormClosing(e);
    }
}
