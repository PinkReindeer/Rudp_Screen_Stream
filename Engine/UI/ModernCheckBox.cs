using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Engine.UI;

public class ModernCheckBox : Control
{
    private bool _checked = true;
    private Color _checkColor = Color.FromArgb(24, 119, 242);
    private Color _boxBorderColor = Color.FromArgb(71, 85, 105);

    public event EventHandler? CheckedChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked != value)
            {
                _checked = value;
                Invalidate();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color CheckColor
    {
        get => _checkColor;
        set { _checkColor = value; Invalidate(); }
    }

    public ModernCheckBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        Size = new Size(80, 24);
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
        ForeColor = Color.FromArgb(226, 232, 240);
        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Checked = !Checked;
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

        int boxSize = 16;
        int boxX = 2;
        int boxY = (Height - boxSize) / 2;
        var boxRect = new Rectangle(boxX, boxY, boxSize, boxSize);

        using (var path = CreateRoundedPath(boxRect, 4))
        {
            if (_checked)
            {
                using var fillBrush = new SolidBrush(Enabled ? _checkColor : Color.FromArgb(70, 85, 110));
                e.Graphics.FillPath(fillBrush, path);

                using var pen = new Pen(Color.White, 2f);
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                var pt1 = new PointF(boxX + 3.5f, boxY + 8f);
                var pt2 = new PointF(boxX + 6.5f, boxY + 12f);
                var pt3 = new PointF(boxX + 12.5f, boxY + 4.5f);
                e.Graphics.DrawLines(pen, new[] { pt1, pt2, pt3 });
            }
            else
            {
                using var borderPen = new Pen(_boxBorderColor, 1.5f);
                e.Graphics.DrawPath(borderPen, path);
            }
        }

        Color textCol = Enabled ? ForeColor : Color.FromArgb(115, 128, 145);
        using var textBrush = new SolidBrush(textCol);
        float tx = boxX + boxSize + 6;
        float ty = (Height - Font.Height) / 2f;
        e.Graphics.DrawString(Text, Font, textBrush, tx, ty);
    }
}
