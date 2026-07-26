namespace NodeVision.Rendering;

public abstract class Renderer
{
    public abstract void Render(List<DrawCommand> commands);
}