using NodeVision.Core;

namespace NodeVision.Rendering;

public abstract class RenderCommand
{
    /// <summary>Optional canvas-space bounds the backend clips this command to.</summary>
    public ClipRect? Clip { get; init; }
}