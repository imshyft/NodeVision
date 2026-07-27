namespace NodeVision.Rendering;

public abstract class Renderer
{
    public abstract void Render(IReadOnlyList<RenderCommand> commands, RenderContext context);
}