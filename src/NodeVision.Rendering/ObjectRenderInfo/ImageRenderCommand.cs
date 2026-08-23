using NodeVision.Core;

namespace NodeVision.Rendering.ObjectRenderInfo;

public sealed class ImageRenderCommand : RenderCommand, IRenderable
{
    public string FilePath { get; init; } = "";
    public Vector2 Position { get; init; }
    public Vector2 Size { get; init; }
}
