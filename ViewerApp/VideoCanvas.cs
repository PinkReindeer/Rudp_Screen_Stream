using System.Drawing.Drawing2D;

namespace ViewerApp;

public sealed class VideoCanvas : Control
{
    private Bitmap? _currentFrame;
    private readonly object _sync = new();
    private string _statusText = "Waiting for connection / UDP packets on port 9000...";

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; Invalidate(); }
    }

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

    public void ClearFrame(string? statusMessage = null)
    {
        Bitmap? old;
        lock (_sync)
        {
            old = _currentFrame;
            _currentFrame = null;
            if (statusMessage != null) _statusText = statusMessage;
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
                TextRenderer.DrawText(e.Graphics, _statusText, new Font("Segoe UI", 13, FontStyle.Regular),
                    ClientRectangle, Color.Gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }
}
