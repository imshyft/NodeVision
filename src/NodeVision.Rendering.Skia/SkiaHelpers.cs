using NodeVision.Core;
using SkiaSharp;

namespace NodeVision.Rendering.Skia;

public static class SkiaHelpers
{
    public static SKColor ConvertColour(Colour colour)
    {
        return new SKColor(
            (byte)(colour.R * 255),
            (byte)(colour.G * 255),
            (byte)(colour.B * 255),
            (byte)(colour.A * 255));
    }

    public static SKImage CreateImage(WebcamFrame frame)
    {
        var info = new SKImageInfo(frame.Width, frame.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        frame.BgraData.Span.CopyTo(bitmap.GetPixelSpan());
        return SKImage.FromBitmap(bitmap);
    }
}