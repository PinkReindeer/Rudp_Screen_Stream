using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Engine.UI;

public class ModernCard : Panel
{
    private string _cardIcon = "";
    private string _title = "";
    private string _statusBadge = "";
    private Color _statusBadgeColor = Color.FromArgb(34, 197, 94);
    private string _pillBadge = "";
    private int _borderRadius = 10;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string CardIcon
    {
        get => _cardIcon;
        set { _cardIcon = value; Invalidate(); }
    }

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

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string PillBadge
    {
        get => _pillBadge;
        set { _pillBadge = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int BorderRadius
    {
        get => _borderRadius;
        set { _borderRadius = value; Invalidate(); }
    }

    public ModernCard()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.FromArgb(22, 34, 53);
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

        Color parentBg = Parent != null ? Parent.BackColor : Color.FromArgb(11, 17, 30);
        using (var pBrush = new SolidBrush(parentBg))
        {
            e.Graphics.FillRectangle(pBrush, ClientRectangle);
        }

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = CreateRoundedPath(rect, _borderRadius))
        {
            using var cardBrush = new SolidBrush(BackColor);
            e.Graphics.FillPath(cardBrush, path);

            using var borderPen = new Pen(Color.FromArgb(37, 53, 77), 1.2f);
            e.Graphics.DrawPath(borderPen, path);
        }

        float textX = 14;
        if (!string.IsNullOrEmpty(_cardIcon))
        {
            using var iconFont = new Font("Segoe UI", 11f);
            using var iconBrush = new SolidBrush(Color.FromArgb(56, 189, 248));
            e.Graphics.DrawString(_cardIcon, iconFont, iconBrush, new PointF(14, 11));
            var iconSize = e.Graphics.MeasureString(_cardIcon, iconFont);
            textX += iconSize.Width + 4;
        }

        if (!string.IsNullOrEmpty(_title))
        {
            using var font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            using var brush = new SolidBrush(Color.White);
            e.Graphics.DrawString(_title, font, brush, new PointF(textX, 11));
        }

        if (!string.IsNullOrEmpty(_statusBadge))
        {
            using var font = new Font("Segoe UI", 9f, FontStyle.Bold);
            using var brush = new SolidBrush(_statusBadgeColor);
            var size = e.Graphics.MeasureString(_statusBadge, font);
            e.Graphics.DrawString(_statusBadge, font, brush, new PointF(Width - size.Width - 14, 13));
        }

        if (!string.IsNullOrEmpty(_pillBadge))
        {
            using var font = new Font("Segoe UI", 9f, FontStyle.Bold);
            var size = e.Graphics.MeasureString(_pillBadge, font);
            int pillW = (int)size.Width + 16;
            int pillH = 22;
            int pillX = Width - pillW - 14;
            int pillY = 11;

            var pillRect = new Rectangle(pillX, pillY, pillW, pillH);
            using var pillPath = CreateRoundedPath(pillRect, 6);
            using var pillBgBrush = new SolidBrush(Color.FromArgb(31, 46, 69));
            e.Graphics.FillPath(pillBgBrush, pillPath);

            using var textBrush = new SolidBrush(Color.FromArgb(56, 189, 248));
            e.Graphics.DrawString(_pillBadge, font, textBrush, new PointF(pillX + 8, pillY + 3));
        }
    }
}
