using System.Drawing.Imaging;
using System.Net;
using Engine.Transport;

namespace HostApp;

public partial class Form1 : Form
{
    private readonly UdpFrameSender _sender = new();
    private readonly TextBox _txtIp = new() { Text = "127.0.0.1", Width = 110 };
    private readonly NumericUpDown _numPort = new() { Minimum = 1000, Maximum = 65535, Value = 9000, Width = 75 };
    private readonly Button _btnSendSingle = new() { Text = "1. Send 1 Static Frame (~70KB)", AutoSize = true, BackColor = Color.LightSkyBlue };
    private readonly Button _btnToggleStream = new() { Text = "2. Start 30 FPS Stream ", AutoSize = true, BackColor = Color.LightGreen };
    private readonly Label _lblStats = new() { AutoSize = true, Font = new Font("Consolas", 10, FontStyle.Bold) };

    private CancellationTokenSource? _streamCts;
    private int _testFrameTick;

    public Form1()
    {
        InitializeComponent();
        Text = "RUDP Host App - Milestone 1 (Sender)";
        Size = new Size(620, 220);

        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(15), FlowDirection = FlowDirection.TopDown };
        var row1 = new FlowLayoutPanel { AutoSize = true };
        row1.Controls.AddRange([new Label { Text = "Target IP:", AutoSize = true, Padding = new Padding(0, 5, 0, 0) }, _txtIp,
                                new Label { Text = "UDP Port:", AutoSize = true, Padding = new Padding(10, 5, 0, 0) }, _numPort]);

        var row2 = new FlowLayoutPanel { AutoSize = true, Padding = new Padding(0, 10, 0, 10) };
        row2.Controls.AddRange([_btnSendSingle, _btnToggleStream]);

        flow.Controls.AddRange([row1, row2, _lblStats]);
        Controls.Add(flow);

        _btnSendSingle.Click += async (_, _) => await SendOneSyntheticFrameAsync();
        _btnToggleStream.Click += (_, _) => ToggleContinuousStream();
    }

    private async Task SendOneSyntheticFrameAsync()
    {
        var ep = new IPEndPoint(IPAddress.Parse(_txtIp.Text), (int)_numPort.Value);
        byte[] jpegBytes = GenerateSyntheticJpegFrame(++_testFrameTick);
        ushort fragments = await _sender.SendFrameAsync(jpegBytes, ep);

        _lblStats.Text = $"[Sent Frame #{_testFrameTick}] Size: {jpegBytes.Length / 1024.0:F1} KB | " +
                         $"Fragments: {fragments} UDP packets | Total Packets: {_sender.TotalPacketsSent}";
    }

    private void ToggleContinuousStream()
    {
        if (_streamCts != null)
        {
            _streamCts.Cancel();
            _streamCts = null;
            _btnToggleStream.Text = "2. Start 30 FPS Stream";
            _btnToggleStream.BackColor = Color.LightGreen;
            return;
        }

        _streamCts = new CancellationTokenSource();
        _btnToggleStream.Text = "Stop 30 FPS Stream";
        _btnToggleStream.BackColor = Color.Salmon;

        var token = _streamCts.Token;
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(33)); // ~30 FPS
            while (await timer.WaitForNextTickAsync(token))
            {
                await Invoke(async () => await SendOneSyntheticFrameAsync());
            }
        }, token);
    }

    /// <summary>
    /// Creates a synthetic 1280x720 image with moving patterns + a clock to compress into a JPEG of approximately 60-85KB.
    /// </summary>
    private static byte[] GenerateSyntheticJpegFrame(int tick)
    {
        using var bmp = new Bitmap(1280, 720);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.FromArgb(20, 24, 35));

            // Draw moving sine-wave patterns to create a large byte footprint (~70KB after JPEG compression)
            for (int i = 0; i < 60; i++)
            {
                int x = (i * 35 + tick * 8) % 1280;
                int y = (int)(Math.Sin((i + tick * 0.1)) * 250 + 360);
                using var brush = new SolidBrush(Color.FromArgb(180, (i * 40) % 255, (i * 85) % 255, 220));
                g.FillEllipse(brush, x, y - 60, 120, 120);
            }

            using var fontTitle = new Font("Consolas", 32, FontStyle.Bold);
            using var fontSub = new Font("Consolas", 18, FontStyle.Regular);
            g.DrawString($"RUDP MILESTONE 1 - FRAME #{tick}", fontTitle, Brushes.White, 40, 40);
            g.DrawString($"Timestamp: {DateTime.Now:HH:mm:ss.fff}", fontSub, Brushes.Cyan, 40, 105);
            g.DrawString("Zero-Allocation Fragmenter -> UDP Socket -> FrameReassembler", fontSub, Brushes.LightGreen, 40, 145);
        }

        using var ms = new MemoryStream();
        var encoder = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        var encParams = new EncoderParameters(1);
        encParams.Param[0] = new EncoderParameter(Encoder.Quality, 85L); // 85% quality for ~70KB JPEG
        bmp.Save(ms, encoder, encParams);
        return ms.ToArray();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _streamCts?.Cancel();
        _sender.Dispose();
        base.OnFormClosing(e);
    }
}