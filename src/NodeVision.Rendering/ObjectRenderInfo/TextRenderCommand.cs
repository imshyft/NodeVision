using NodeVision.Core;

namespace NodeVision.Rendering.ObjectRenderInfo;

public sealed class TextRenderCommand : RenderCommand, IRenderable
{
    public string Text { get; init; } = "";
    public Colour Colour { get; init; }
    public Vector2 Position { get; init; }
    public Vector2 Size { get; init; }
}
