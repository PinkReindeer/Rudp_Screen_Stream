using System.Drawing;
using System.Drawing.Imaging;

namespace Engine.Capture;

public sealed class ScreenCapturer
{
    private static readonly ImageCodecInfo JpegCodec =
        ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);

    private volatile int _jpegQuality = 85;

    public int JpegQuality
    {
        get => _jpegQuality;
        set => _jpegQuality = Math.Clamp(value, 1, 100);
    }

    public MemoryStream CaptureJpeg()
    {
        var bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);

        using var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
        }

        var ms = new MemoryStream(bounds.Width * bounds.Height / 4);
        var encParams = new EncoderParameters(1);
        encParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)_jpegQuality);
        bmp.Save(ms, JpegCodec, encParams);
        ms.Position = 0;
        return ms;
    }
}
