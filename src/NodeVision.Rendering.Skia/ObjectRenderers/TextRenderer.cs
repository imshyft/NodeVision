using NodeVision.Rendering.ObjectRenderInfo;
using SkiaSharp;

namespace NodeVision.Rendering.Skia.ObjectRenderers;

public class TextRenderer : SkiaObjectRenderer
{
    public override void DrawObject(RenderCommand command, SKCanvas canvas)
    {
        var textCommand = (TextRenderCommand)command;

        using var paint = new SKPaint
        {
            Color = SkiaHelpers.ConvertColour(textCommand.Colour),
            IsAntialias = true
        };

        var fontSize = textCommand.Size.Y > 0 ? textCommand.Size.Y : 24f;

        using var font = new SKFont
        {
            Size = fontSize
        };

        canvas.DrawText(textCommand.Text, textCommand.Position.X, textCommand.Position.Y, font, paint);
    }
}
