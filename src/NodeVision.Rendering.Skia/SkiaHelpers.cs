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
}