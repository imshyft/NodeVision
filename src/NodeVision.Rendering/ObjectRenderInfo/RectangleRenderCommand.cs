using NodeVision.Core;

namespace NodeVision.Rendering.ObjectRenderInfo
{
    public sealed class RectangleRenderCommand : RenderCommand, IRenderable
    {
        public Colour Colour { get; init; }
        public Vector2 Position { get; init; }
        public Vector2 Size { get; init; }
    }
}