using NodeVision.Core;

namespace NodeVision.Rendering.ObjectRenderInfo;

/// <summary>
/// A curved link between a parent and child node. Drawn behind the cards, so only the segment
/// between their edges is visible.
/// </summary>
public sealed class ConnectionRenderCommand : RenderCommand
{
    public Vector2 Start { get; init; }
    public Vector2 End { get; init; }
    public Vector2 Control1 { get; init; }
    public Vector2 Control2 { get; init; }
    public Colour Colour { get; init; }
    public float Width { get; init; } = 2f;
}
