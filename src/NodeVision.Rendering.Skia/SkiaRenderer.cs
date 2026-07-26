using NodeVision.Rendering.ObjectRenderInfo;
using NodeVision.Rendering.Skia.ObjectRenderers;
using SkiaSharp;

namespace NodeVision.Rendering.Skia;

public class SkiaRenderer : Renderer
{
    private SKCanvas? _canvas;
    
    private Dictionary<Type, SkiaObjectRenderer> _objectRenderers = new()
    {
        { typeof(DrawRectangleCommand), new RectangleRenderer() }
    };

    public void BeginRender(SKCanvas canvas)
    {
        _canvas = canvas;
    }
    
    public override void Render(List<DrawCommand> commands)
    {
        if (_canvas == null)
        {
            throw new Exception("Failed to initialise canvas!");
        }
        
        foreach (var drawCommand in commands)
        {
            if (_objectRenderers.TryGetValue(drawCommand.GetType(), out var renderer))
            {
                renderer.DrawObject(drawCommand, _canvas);
            }
        }
    }

    public void EndRender()
    {
        _canvas = null;
    }
}