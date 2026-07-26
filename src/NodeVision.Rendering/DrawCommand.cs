using NodeVision.Core;

namespace NodeVision.Rendering;

public abstract class DrawCommand
{
    public Vector2 Position { get; init; }
    public Vector2 Size { get; init; }
    public Colour Colour { get; init; }
}