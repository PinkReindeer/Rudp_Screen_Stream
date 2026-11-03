using Engine.Transport;

namespace ViewerApp;

public partial class Form1 : Form
{
    private readonly VideoCanvas _canvas = new() { Dock = DockStyle.Fill };
    private readonly Label _lblTelemetry = new()
    {
        Dock = DockStyle.Top,
        Height = 40,
        BackColor = Color.FromArgb(15, 18, 25),
        ForeColor = Color.LimeGreen,
        Font = new Font("Consolas", 10, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(10, 0, 0, 0)
    };

    private readonly UdpFrameReceiver _receiver;
    private readonly System.Windows.Forms.Timer _telemetryTimer = new() { Interval = 250 };
    private uint _lastFrameId;
    private uint _lastLatencyMs;
    private int _lastFrameBytes;

    public Form1()
    {
        InitializeComponent();
        Text = "RUDP Viewer App - Milestone 1 (Receiver - Port 9000)";
        Size = new Size(960, 600);

        Controls.Add(_canvas);
        Controls.Add(_lblTelemetry);

        _receiver = new UdpFrameReceiver(listenPort: 9000);
        _receiver.StartReceiving();

        // CompletedFrame thread reading and Bitmap decoding on a Background Thread
        _ = Task.Run(DecodeAndRenderLoopAsync);

        _telemetryTimer.Tick += (_, _) => UpdateTelemetryUi();
        _telemetryTimer.Start();
    }

    private async Task DecodeAndRenderLoopAsync()
    {
        await foreach (var completedFrame in _receiver.CompletedFrames.ReadAllAsync())
        {
            using (completedFrame)
            {
                try
                {
                    using var ms = new MemoryStream(completedFrame.PooledBuffer, 0, completedFrame.Length, writable: false);
                    var bmp = new Bitmap(ms);

                    _lastFrameId = completedFrame.FrameID;
                    _lastFrameBytes = completedFrame.Length;
                    _lastLatencyMs = (uint)Environment.TickCount64 - completedFrame.TimestampMs;

                    _canvas.UpdateFrame(bmp);
                }
                catch
                {
                    // Skip if the JPEG is corrupted or cannot be decoded
                }
            }
        }
    }

    private void UpdateTelemetryUi()
    {
        var r = _receiver.Reassembler;
        _lblTelemetry.Text =
            $"Frame ID: #{_lastFrameId} ({_lastFrameBytes / 1024.0:F1} KB) | " +
            $"UDP Frags Recv: {r.TotalFragmentsReceived} | " +
            $"Frames OK: {r.TotalFramesCompleted} | " +
            $"Dropped: {r.TotalFramesDroppedIncomplete} | " +
            $"One-way Latency: {_lastLatencyMs} ms";
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _telemetryTimer.Stop();
        _receiver.Dispose();
        base.OnFormClosing(e);
    }
}