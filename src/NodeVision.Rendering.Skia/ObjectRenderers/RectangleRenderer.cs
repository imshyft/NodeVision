using NodeVision.Rendering.ObjectRenderInfo;
using SkiaSharp;

namespace NodeVision.Rendering.Skia.ObjectRenderers;

public class RectangleRenderer : SkiaObjectRenderer
{
    public override void DrawObject(RenderCommand command, SKCanvas canvas)
    {
        var rectangleCommand = (RectangleRenderCommand)command;
        using var paint = new SKPaint
        {
            Color = SkiaHelpers.ConvertColour(rectangleCommand.Colour),
            Style = SKPaintStyle.Fill
        };

        canvas.DrawRect(
            rectangleCommand.Position.X,
            rectangleCommand.Position.Y,
            rectangleCommand.Size.X,
            rectangleCommand.Size.Y,
            paint);
    }
}