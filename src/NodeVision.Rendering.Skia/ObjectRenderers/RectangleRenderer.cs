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
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        var x = rectangleCommand.Position.X;
        var y = rectangleCommand.Position.Y;
        var width = rectangleCommand.Size.X;
        var height = rectangleCommand.Size.Y;

        if (rectangleCommand.CornerRadius > 0f)
        {
            canvas.DrawRoundRect(x, y, width, height, rectangleCommand.CornerRadius, rectangleCommand.CornerRadius, paint);
        }
        else
        {
            canvas.DrawRect(x, y, width, height, paint);
        }
    }
}