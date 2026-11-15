using System.Drawing.Drawing2D;

namespace ViewerApp;

public sealed class VideoCanvas : Control
{
    private Bitmap? _currentFrame;
    private readonly object _sync = new();
    private string _statusSubText = "UDP packets on port 9000...";

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string StatusSubText
    {
        get => _statusSubText;
        set { _statusSubText = value; Invalidate(); }
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string StatusText
    {
        get => _statusSubText;
        set { _statusSubText = value; Invalidate(); }
    }

    public VideoCanvas()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(7, 11, 20);
    }

    public void UpdateFrame(Bitmap newFrame)
    {
        Bitmap? old;
        lock (_sync)
        {
            old = _currentFrame;
            _currentFrame = newFrame;
        }
        old?.Dispose();
        Invalidate();
    }

    public void ClearFrame(string? statusMessage = null)
    {
        Bitmap? old;
        lock (_sync)
        {
            old = _currentFrame;
            _currentFrame = null;
            if (statusMessage != null) _statusSubText = statusMessage;
        }
        old?.Dispose();
        Invalidate();
    }

    private static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        if (d > rect.Width) d = rect.Width;
        if (d > rect.Height) d = rect.Height;

        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        lock (_sync)
        {
            using (var bgBrush = new SolidBrush(BackColor))
            {
                e.Graphics.FillRectangle(bgBrush, ClientRectangle);
            }

            if (_currentFrame != null)
            {
                e.Graphics.InterpolationMode = InterpolationMode.Bilinear;
                float imageAspect = (float)_currentFrame.Width / Math.Max(1, _currentFrame.Height);
                float clientAspect = (float)ClientRectangle.Width / Math.Max(1, ClientRectangle.Height);
                Rectangle destRect;
                if (clientAspect > imageAspect)
                {
                    int w = (int)(ClientRectangle.Height * imageAspect);
                    int x = (ClientRectangle.Width - w) / 2;
                    destRect = new Rectangle(x, 0, w, ClientRectangle.Height);
                }
                else
                {
                    int h = (int)(ClientRectangle.Width / imageAspect);
                    int y = (ClientRectangle.Height - h) / 2;
                    destRect = new Rectangle(0, y, ClientRectangle.Width, h);
                }
                e.Graphics.DrawImage(_currentFrame, destRect);
            }
            else
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

                int cx = Width / 2;
                int cy = Height / 2 - 30;

                int monW = 96;
                int monH = 62;
                int monX = cx - monW / 2;
                int monY = cy - 40;

                var monRect = new Rectangle(monX, monY, monW, monH);
                using (var monPath = CreateRoundedPath(monRect, 10))
                {
                    using var monFill = new SolidBrush(Color.FromArgb(14, 23, 38));
                    e.Graphics.FillPath(monFill, monPath);

                    using var monPen = new Pen(Color.FromArgb(148, 163, 184), 4.5f);
                    monPen.LineJoin = LineJoin.Round;
                    e.Graphics.DrawPath(monPen, monPath);
                }

                int standW = 10;
                int standH = 14;
                int standX = cx - standW / 2;
                int standY = monY + monH;
                using (var standBrush = new SolidBrush(Color.FromArgb(148, 163, 184)))
                {
                    e.Graphics.FillRectangle(standBrush, standX, standY, standW, standH);

                    int baseW = 46;
                    int baseH = 6;
                    int baseX = cx - baseW / 2;
                    int baseY = standY + standH - 2;
                    var baseRect = new Rectangle(baseX, baseY, baseW, baseH);
                    using var basePath = CreateRoundedPath(baseRect, 3);
                    e.Graphics.FillPath(standBrush, basePath);
                }

                using (var arrowFont = new Font("Segoe UI", 26, FontStyle.Bold))
                using (var arrowBrush = new SolidBrush(Color.FromArgb(148, 163, 184)))
                {
                    var aSize = e.Graphics.MeasureString("↔", arrowFont);
                    e.Graphics.DrawString("↔", arrowFont, arrowBrush, cx - aSize.Width / 2, monY + (monH - aSize.Height) / 2 - 2);
                }

                using (var titleFont = new Font("Segoe UI", 15f, FontStyle.Bold))
                using (var titleBrush = new SolidBrush(Color.FromArgb(226, 232, 240)))
                {
                    string titleText = "Waiting for connection";
                    var tSize = e.Graphics.MeasureString(titleText, titleFont);
                    e.Graphics.DrawString(titleText, titleFont, titleBrush, cx - tSize.Width / 2, monY + monH + 34);
                }

                using (var subFont = new Font("Segoe UI", 10.5f))
                using (var subBrush = new SolidBrush(Color.FromArgb(148, 163, 184)))
                {
                    var sSize = e.Graphics.MeasureString(_statusSubText, subFont);
                    e.Graphics.DrawString(_statusSubText, subFont, subBrush, cx - sSize.Width / 2, monY + monH + 64);
                }
            }
        }
    }
}
