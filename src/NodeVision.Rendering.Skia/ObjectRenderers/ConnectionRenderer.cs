using NodeVision.Rendering.ObjectRenderInfo;
using SkiaSharp;

namespace NodeVision.Rendering.Skia.ObjectRenderers;

public class ConnectionRenderer : SkiaObjectRenderer
{
    public override void DrawObject(RenderCommand command, SKCanvas canvas)
    {
        var connection = (ConnectionRenderCommand)command;

        using var paint = new SKPaint
        {
            Color = SkiaHelpers.ConvertColour(connection.Colour),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = connection.Width,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round,
        };

        using var pathBuilder = new SKPathBuilder();
        pathBuilder.MoveTo(connection.Start.X, connection.Start.Y);
        pathBuilder.CubicTo(
            connection.Control1.X, connection.Control1.Y,
            connection.Control2.X, connection.Control2.Y,
            connection.End.X, connection.End.Y);

        using var path = pathBuilder.Detach();
        canvas.DrawPath(path, paint);
    }
}
