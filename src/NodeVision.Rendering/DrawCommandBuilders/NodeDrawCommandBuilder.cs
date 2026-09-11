using NodeVision.Core;
using NodeVision.Rendering.ObjectRenderInfo;

namespace NodeVision.Rendering.DrawCommandBuilders;

public class NodeDrawCommandBuilder : DrawCommandBuilder
{
    public override void BuildCommand(SceneObject sceneObject, List<RenderCommand> commands)
    {
        var node = (Node)sceneObject;
        var position = node.Transform.Position;
        var size = node.Size;

        commands.Add(new RectangleRenderCommand
        {
            Colour = NodeStyle.Border,
            Position = new Vector2(position.X - 1f, position.Y - 1f),
            Size = new Vector2(size.X + 2f, size.Y + 2f),
            CornerRadius = NodeStyle.CornerRadius + 1f
        });

        commands.Add(new RectangleRenderCommand
        {
            Colour = NodeStyle.Background,
            Position = position,
            Size = size,
            CornerRadius = NodeStyle.CornerRadius
        });

        commands.Add(new RectangleRenderCommand
        {
            Colour = NodeStyle.Accent,
            Position = new Vector2(position.X + NodeStyle.AccentInset, position.Y + NodeStyle.Padding),
            Size = new Vector2(NodeStyle.AccentWidth, size.Y - NodeStyle.Padding * 2f),
            CornerRadius = NodeStyle.AccentWidth / 2f
        });

        var textWidth = size.X - NodeStyle.TextInset - NodeStyle.Padding;

        commands.Add(new TextRenderCommand
        {
            Text = node.Header,
            Colour = NodeStyle.HeaderText,
            Position = new Vector2(position.X + NodeStyle.TextInset, position.Y + NodeStyle.Padding),
            Size = new Vector2(textWidth, NodeStyle.HeaderSize),
            MaxWidth = textWidth,
            Anchor = TextAnchor.Top
        });

        commands.Add(new TextRenderCommand
        {
            Text = node.Body,
            Colour = NodeStyle.BodyText,
            Position = new Vector2(position.X + NodeStyle.TextInset, position.Y + NodeStyle.Padding + NodeStyle.HeaderSize + NodeStyle.HeaderGap),
            Size = new Vector2(textWidth, NodeStyle.BodySize),
            MaxWidth = textWidth,
            LineSpacing = NodeStyle.LineSpacing,
            Anchor = TextAnchor.Top
        });
    }
}