using NodeVision.Rendering.ObjectRenderInfo;
using SkiaSharp;

namespace NodeVision.Rendering.Skia.ObjectRenderers;

public class ImageRenderer : SkiaObjectRenderer
{
    private static readonly SKSamplingOptions Sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);

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

        var target = SKRect.Create(
            imageCommand.Position.X,
            imageCommand.Position.Y,
            imageCommand.Size.X,
            imageCommand.Size.Y);

        var dest = FitWithin(target, bitmap.Width, bitmap.Height);

        using var paint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, (byte)(Math.Clamp(imageCommand.Opacity, 0f, 1f) * 255f))
        };

        canvas.DrawImage(image, dest, Sampling, paint);
    }

    /// <summary>
    /// Scales the image to fit inside <paramref name="target"/> without distorting its aspect ratio,
    /// centred on the target box.
    /// </summary>
    private static SKRect FitWithin(SKRect target, int imageWidth, int imageHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0 || target.Width <= 0f || target.Height <= 0f)
            return target;

        var scale = Math.Min(target.Width / imageWidth, target.Height / imageHeight);
        var width = imageWidth * scale;
        var height = imageHeight * scale;

        return SKRect.Create(
            target.Left + (target.Width - width) / 2f,
            target.Top + (target.Height - height) / 2f,
            width,
            height);
    }
}