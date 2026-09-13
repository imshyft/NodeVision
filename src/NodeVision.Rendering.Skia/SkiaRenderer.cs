using NodeVision.Core;
using NodeVision.Rendering.ObjectRenderInfo;
using NodeVision.Rendering.Skia.ObjectRenderers;
using SkiaSharp;

namespace NodeVision.Rendering.Skia;

public class SkiaRenderer : Renderer
{
    private SKCanvas? _canvas;
    
    private Dictionary<Type, SkiaObjectRenderer> _objectRenderers = new()
    {
        { typeof(RectangleRenderCommand), new RectangleRenderer() },
        { typeof(TextRenderCommand), new TextRenderer() },
        { typeof(ImageRenderCommand), new ImageRenderer() }
    };

    public void BeginRender(SKCanvas canvas)
    {
        _canvas = canvas;
    }
    
    public override void Render(IReadOnlyList<RenderCommand> commands, RenderContext context)
    {
        if (_canvas == null)
        {
            throw new Exception("Failed to initialise canvas!");
        }

        _canvas.Save();

        var clipRect = new SKRect(0, 0, context.RenderTargetSize.X, context.RenderTargetSize.Y);
        _canvas.ClipRect(clipRect);

        float cx = context.RenderTargetSize.X / 2f;
        float cy = context.RenderTargetSize.Y / 2f;

        _canvas.Translate(cx, cy);
        _canvas.Scale(context.CameraZoom, context.CameraZoom);
        _canvas.Translate(-context.CameraTranslation.X, -context.CameraTranslation.Y);

        foreach (var drawCommand in commands)
        {
            if (_objectRenderers.TryGetValue(drawCommand.GetType(), out var renderer))
            {
                renderer.DrawObject(drawCommand, _canvas);
            }
        }

        _canvas.Restore();
    }

    public void EndRender()
    {
        _canvas = null;
    }
}