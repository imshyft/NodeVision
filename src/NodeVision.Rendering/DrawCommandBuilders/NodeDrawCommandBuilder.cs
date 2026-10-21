using System;
using NodeVision.Core;
using NodeVision.Rendering.ObjectRenderInfo;

namespace NodeVision.Rendering.DrawCommandBuilders;

public class NodeDrawCommandBuilder : DrawCommandBuilder
{
    public override void BuildCommand(SceneObject sceneObject, List<RenderCommand> commands)
    {
        var node = (Node)sceneObject;
        var reveal = Math.Clamp(node.Reveal, 0f, 1f);

        if (reveal <= 0f)
            return; // hidden until its parent reveals it

        // A card that is still revealing grows out of its own centre, so the metrics scale with it.
        var metrics = 0.85f + 0.15f * reveal;
        var size = node.Size * metrics;
        var position = node.Position + node.Size * ((1f - metrics) * 0.5f);

        var cornerRadius = NodeStyle.CornerRadius * metrics;
        var padding = NodeStyle.Padding * metrics;
        var accentInset = NodeStyle.AccentInset * metrics;
        var accentWidth = NodeStyle.AccentWidth * metrics;
        var textInset = NodeStyle.TextInset * metrics;
        var headerSize = NodeStyle.HeaderSize * metrics;
        var headerGap = NodeStyle.HeaderGap * metrics;
        var bodySize = NodeStyle.BodySize * metrics;

        // Content is clipped to the card, so a long body or an image cannot spill past it.
        var clip = new ClipRect(position, size);

        commands.Add(new RectangleRenderCommand
        {
            Colour = Fade(NodeStyle.Border, reveal),
            Position = new Vector2(position.X - metrics, position.Y - metrics),
            Size = new Vector2(size.X + metrics * 2f, size.Y + metrics * 2f),
            CornerRadius = cornerRadius + metrics
        });

        commands.Add(new RectangleRenderCommand
        {
            Colour = Fade(NodeStyle.Background, reveal),
            Position = position,
            Size = size,
            CornerRadius = cornerRadius
        });

        commands.Add(new RectangleRenderCommand
        {
            Colour = Fade(NodeStyle.Accent, reveal),
            Position = new Vector2(position.X + accentInset, position.Y + padding),
            Size = new Vector2(accentWidth, size.Y - padding * 2f),
            CornerRadius = accentWidth / 2f
        });

        var textWidth = size.X - textInset - padding;

        commands.Add(new TextRenderCommand
        {
            Text = node.NodeName,
            Colour = Fade(NodeStyle.HeaderText, reveal),
            Position = new Vector2(position.X + textInset, position.Y + padding),
            Size = new Vector2(textWidth, headerSize),
            MaxWidth = textWidth,
            Anchor = TextAnchor.Top,
            Clip = clip
        });

        var detailTop = position.Y + padding + headerSize + headerGap;
        var detailWidth = size.X - textInset - padding;
        var detailHeight = MathF.Max(0f, size.Y - padding - (padding + headerSize + headerGap));

        switch (node.Content)
        {
            case TextContent text:
                commands.Add(new TextRenderCommand
                {
                    Text = text.Value,
                    Colour = Fade(NodeStyle.BodyText, reveal),
                    Position = new Vector2(position.X + textInset, detailTop),
                    Size = new Vector2(detailWidth, bodySize),
                    MaxWidth = detailWidth,
                    LineSpacing = NodeStyle.LineSpacing,
                    Anchor = TextAnchor.Top,
                    Clip = clip
                });
                break;

            case ImageContent image:
                commands.Add(new ImageRenderCommand
                {
                    FilePath = image.FilePath,
                    Position = new Vector2(position.X + textInset, detailTop),
                    Size = new Vector2(detailWidth, detailHeight),
                    Opacity = reveal,
                    Clip = clip
                });
                break;
        }
    }

    // The reveal fades the whole card; alpha reaches Skia through Colour.A.
    private static Colour Fade(Colour colour, float reveal)
        => new Colour(colour.R, colour.G, colour.B, colour.A * reveal);
}