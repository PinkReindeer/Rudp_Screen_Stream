using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Engine.UI;

public class ModernButton : Button
{
    private bool _isHovered;
    private bool _isPressed;
    private int _borderRadius = 8;
    private Color _normalColor = Color.FromArgb(24, 119, 242);
    private Color _hoverColor = Color.FromArgb(37, 133, 255);
    private Color _pressedColor = Color.FromArgb(18, 98, 204);
    private string _customIcon = "";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int BorderRadius
    {
        get => _borderRadius;
        set { _borderRadius = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color NormalColor
    {
        get => _normalColor;
        set { _normalColor = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color HoverColor
    {
        get => _hoverColor;
        set { _hoverColor = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color PressedColor
    {
        get => _pressedColor;
        set { _pressedColor = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string CustomIcon
    {
        get => _customIcon;
        set { _customIcon = value; Invalidate(); }
    }

    public ModernButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        Cursor = Cursors.Hand;
        Size = new Size(120, 36);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _isHovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _isHovered = false;
        _isPressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            _isPressed = true;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _isPressed = false;
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
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        Color parentBg = Parent != null ? Parent.BackColor : Color.FromArgb(17, 24, 39);
        using (var pBrush = new SolidBrush(parentBg))
        {
            e.Graphics.FillRectangle(pBrush, ClientRectangle);
        }

        Color bg = _normalColor;
        if (!Enabled)
        {
            bg = Color.FromArgb(38, 48, 65);
        }
        else if (_isPressed)
        {
            bg = _pressedColor;
        }
        else if (_isHovered)
        {
            bg = _hoverColor;
        }

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = CreateRoundedPath(rect, _borderRadius);
        using var brush = new SolidBrush(bg);
        e.Graphics.FillPath(brush, path);

        Color textCol = Enabled ? ForeColor : Color.FromArgb(115, 128, 145);
        string display = string.IsNullOrEmpty(_customIcon) ? Text : $"{_customIcon}  {Text}";
        var size = e.Graphics.MeasureString(display, Font);
        float tx = (Width - size.Width) / 2;
        float ty = (Height - size.Height) / 2;

        using var textBrush = new SolidBrush(textCol);
        e.Graphics.DrawString(display, Font, textBrush, tx, ty);
    }
}
