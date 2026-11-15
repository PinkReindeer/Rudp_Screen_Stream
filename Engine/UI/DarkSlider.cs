using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Engine.UI;

public class DarkSlider : Control
{
    private int _minimum = 0;
    private int _maximum = 100;
    private int _value = 50;
    private bool _isDragging;

    public event EventHandler? ValueChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Minimum
    {
        get => _minimum;
        set
        {
            _minimum = value;
            if (_value < _minimum) _value = _minimum;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Maximum
    {
        get => _maximum;
        set
        {
            _maximum = Math.Max(_minimum + 1, value);
            if (_value > _maximum) _value = _maximum;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _value;
        set
        {
            int clamped = Math.Clamp(value, _minimum, _maximum);
            if (_value != clamped)
            {
                _value = clamped;
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color TrackColor { get; set; } = Color.FromArgb(34, 50, 73);

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color ProgressColor { get; set; } = Color.FromArgb(37, 99, 235);

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color ThumbColor { get; set; } = Color.FromArgb(56, 189, 248);

    public DarkSlider()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        Size = new Size(130, 24);
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            _isDragging = true;
            Capture = true;
            UpdateFromMouse(e.X);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_isDragging)
        {
            UpdateFromMouse(e.X);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_isDragging)
        {
            _isDragging = false;
            Capture = false;
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        int delta = e.Delta > 0 ? 1 : -1;
        Value += delta;
    }

    private void UpdateFromMouse(int mouseX)
    {
        int thumbRadius = 8;
        int trackLeft = thumbRadius + 2;
        int trackRight = Width - thumbRadius - 2;
        int trackWidth = Math.Max(1, trackRight - trackLeft);

        float ratio = Math.Clamp((float)(mouseX - trackLeft) / trackWidth, 0f, 1f);
        Value = _minimum + (int)Math.Round(ratio * (_maximum - _minimum));
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
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        Color bg = BackColor == Color.Transparent && Parent != null ? Parent.BackColor : BackColor;
        using (var bgBrush = new SolidBrush(bg))
        {
            e.Graphics.FillRectangle(bgBrush, ClientRectangle);
        }

        int thumbRadius = 8;
        int trackHeight = 6;
        int trackLeft = thumbRadius + 2;
        int trackRight = Width - thumbRadius - 2;
        int trackWidth = Math.Max(1, trackRight - trackLeft);
        int trackY = (Height - trackHeight) / 2;

        float ratio = (float)(_value - _minimum) / Math.Max(1, _maximum - _minimum);
        int thumbX = trackLeft + (int)(ratio * trackWidth);
        int thumbY = Height / 2;

        var fullTrackRect = new Rectangle(trackLeft, trackY, trackWidth, trackHeight);
        using (var fullPath = CreateRoundedPath(fullTrackRect, trackHeight / 2))
        using (var trackBrush = new SolidBrush(TrackColor))
        {
            e.Graphics.FillPath(trackBrush, fullPath);
        }

        if (ratio > 0)
        {
            int progressWidth = Math.Max(trackHeight, thumbX - trackLeft);
            var progRect = new Rectangle(trackLeft, trackY, progressWidth, trackHeight);
            using var progPath = CreateRoundedPath(progRect, trackHeight / 2);
            using var progBrush = new SolidBrush(Enabled ? ProgressColor : Color.FromArgb(70, 85, 110));
            e.Graphics.FillPath(progBrush, progPath);
        }

        using (var thumbBrush = new SolidBrush(Enabled ? ThumbColor : Color.FromArgb(120, 130, 145)))
        {
            e.Graphics.FillEllipse(thumbBrush, thumbX - thumbRadius, thumbY - thumbRadius, thumbRadius * 2, thumbRadius * 2);
        }

        if (Enabled)
        {
            int innerRadius = 3;
            using var innerBrush = new SolidBrush(Color.White);
            e.Graphics.FillEllipse(innerBrush, thumbX - innerRadius, thumbY - innerRadius, innerRadius * 2, innerRadius * 2);
        }
    }
}
