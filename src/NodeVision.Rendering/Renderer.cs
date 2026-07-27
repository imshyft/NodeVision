namespace NodeVision.Rendering;

public abstract class Renderer
{
    public abstract void Render(IReadOnlyList<DrawCommand> commands);
}