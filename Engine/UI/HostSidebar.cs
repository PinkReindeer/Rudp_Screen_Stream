using System.Drawing.Drawing2D;

namespace Engine.UI;

public class HostSidebar : Panel
{
    public HostSidebar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(13, 21, 39);
        Width = 66;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using (var bgBrush = new SolidBrush(BackColor))
        {
            e.Graphics.FillRectangle(bgBrush, ClientRectangle);
        }

        using (var path1 = new GraphicsPath())
        {
            var p1 = new PointF(0, Height - 150);
            var c1 = new PointF(Width * 0.7f, Height - 100);
            var c2 = new PointF(Width, Height - 40);
            var p2 = new PointF(Width, Height);
            path1.AddLine(0, Height, 0, Height - 150);
            path1.AddBezier(p1, c1, c2, p2);
            path1.AddLine(Width, Height, 0, Height);
            path1.CloseFigure();
            using var brush1 = new SolidBrush(Color.FromArgb(29, 78, 216));
            e.Graphics.FillPath(brush1, path1);
        }

        using (var path2 = new GraphicsPath())
        {
            var p1 = new PointF(0, Height - 95);
            var c1 = new PointF(Width * 0.5f, Height - 60);
            var c2 = new PointF(Width * 0.85f, Height - 10);
            var p2 = new PointF(Width * 0.85f, Height);
            path2.AddLine(0, Height, 0, Height - 95);
            path2.AddBezier(p1, c1, c2, p2);
            path2.AddLine(Width * 0.85f, Height, 0, Height);
            path2.CloseFigure();
            using var brush2 = new SolidBrush(Color.FromArgb(37, 99, 235));
            e.Graphics.FillPath(brush2, path2);
        }
    }
}
