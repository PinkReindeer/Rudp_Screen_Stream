using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Engine.UI;

public class DarkCard : Panel
{
    private string _title = "";
    private string _statusBadge = "";
    private Color _statusBadgeColor = Color.FromArgb(22, 163, 106);

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Title
    {
        get => _title;
        set { _title = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string StatusBadge
    {
        get => _statusBadge;
        set { _statusBadge = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color StatusBadgeColor
    {
        get => _statusBadgeColor;
        set { _statusBadgeColor = value; Invalidate(); }
    }

    public DarkCard()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(37, 51, 77);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using (var borderPen = new Pen(Color.FromArgb(53, 68, 95), 1))
        {
            e.Graphics.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
        }

        if (!string.IsNullOrEmpty(_title))
        {
            using var font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            using var brush = new SolidBrush(Color.FromArgb(234, 241, 255));
            e.Graphics.DrawString(_title, font, brush, new PointF(14, 12));
        }

        if (!string.IsNullOrEmpty(_statusBadge))
        {
            using var font = new Font("Segoe UI", 9f, FontStyle.Bold);
            using var brush = new SolidBrush(_statusBadgeColor);
            var size = e.Graphics.MeasureString(_statusBadge, font);
            e.Graphics.DrawString(_statusBadge, font, brush, new PointF(Width - size.Width - 14, 13));
        }
    }
}
