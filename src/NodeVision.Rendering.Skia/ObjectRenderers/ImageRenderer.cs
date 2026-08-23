using NodeVision.Rendering.ObjectRenderInfo;
using SkiaSharp;

namespace NodeVision.Rendering.Skia.ObjectRenderers;

public class ImageRenderer : SkiaObjectRenderer
{
    public override void DrawObject(RenderCommand command, SKCanvas canvas)
    {
        var imageCommand = (ImageRenderCommand)command;

        var path = Path.IsPathRooted(imageCommand.FilePath)
            ? imageCommand.FilePath
            : Path.Combine(AppContext.BaseDirectory, imageCommand.FilePath);

        if (!File.Exists(path))
            return;

        using var bitmap = SKBitmap.Decode(path);
        if (bitmap == null)
            return;

        using var image = SKImage.FromBitmap(bitmap);

        var dest = SKRect.Create(
            imageCommand.Position.X,
            imageCommand.Position.Y,
            imageCommand.Size.X,
            imageCommand.Size.Y);

        canvas.DrawImage(image, dest);
    }
}
