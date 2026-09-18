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

        var lines = WrapLines(textCommand.Text, textCommand.MaxWidth, font);
        var lineHeight = fontSize * textCommand.LineSpacing;
        var baseline = textCommand.Anchor == TextAnchor.Top
            ? textCommand.Position.Y - font.Metrics.Ascent
            : textCommand.Position.Y;

        for (var i = 0; i < lines.Count; i++)
        {
            canvas.DrawText(lines[i], textCommand.Position.X, baseline + i * lineHeight, SKTextAlign.Left, font, paint);
        }
    }

    private static List<string> WrapLines(string text, float maxWidth, SKFont font)
    {
        if (maxWidth <= 0f)
        {
            return new List<string> { text };
        }

        var lines = new List<string>();
        var line = string.Empty;

        foreach (var word in text.Split(' '))
        {
            var candidate = line.Length == 0 ? word : line + " " + word;

            if (line.Length > 0 && font.MeasureText(candidate) > maxWidth)
            {
                lines.Add(line);
                line = word;
            }
            else
            {
                line = candidate;
            }
        }

        lines.Add(line);
        return lines;
    }
}