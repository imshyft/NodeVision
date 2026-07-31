using SkiaSharp;

namespace NodeVision.Rendering.Skia;

public abstract class SkiaObjectRenderer
{
    public abstract void DrawObject(RenderCommand command, SKCanvas canvas);
}