using NodeVision.Rendering.ObjectRenderInfo;
using SkiaSharp;

namespace NodeVision.Rendering.Skia.ObjectRenderers;

public class RectangleRenderer : SkiaObjectRenderer
{
    public override void DrawObject(DrawCommand command, SKCanvas canvas)
    {
        var rectangleCommand = (DrawRectangleCommand)command;
        using var paint = new SKPaint
        {
            Color = SkiaHelpers.ConvertColour(command.Colour),
            Style = SKPaintStyle.Fill
        };

        canvas.DrawRect(
            command.Position.X,
            command.Position.Y,
            command.Size.X,
            command.Size.Y,
            paint);
    }
}