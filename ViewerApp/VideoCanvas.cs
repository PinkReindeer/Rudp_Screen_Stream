using System.Drawing.Drawing2D;

namespace ViewerApp;

public sealed class VideoCanvas : Control
{
    private Bitmap? _currentFrame;
    private readonly object _sync = new();

    public VideoCanvas()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Black;
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

    protected override void OnPaint(PaintEventArgs e)
    {
        lock (_sync)
        {
            if (_currentFrame != null)
            {
                e.Graphics.InterpolationMode = InterpolationMode.Bilinear;
                e.Graphics.DrawImage(_currentFrame, ClientRectangle);
            }
            else
            {
                TextRenderer.DrawText(e.Graphics, "Waiting for UDP packets on port 9000...", new Font("Segoe UI", 14),
                    ClientRectangle, Color.Gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }
}