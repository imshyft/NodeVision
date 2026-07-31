using NodeVision.Core;

namespace NodeVision.Rendering;

public interface IRenderable
{
    public Vector2 Position { get; init; }
    public Vector2 Size { get; init; }
}