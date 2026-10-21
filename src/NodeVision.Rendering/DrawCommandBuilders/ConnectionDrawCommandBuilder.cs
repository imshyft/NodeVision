using System;
using NodeVision.Core;
using NodeVision.Rendering.ObjectRenderInfo;

namespace NodeVision.Rendering.DrawCommandBuilders;

/// <summary>
/// Expands a connection into one curved line from the parent card's centre to the child's. The
/// cards are drawn on top, so the line reads as an edge between their borders.
/// </summary>
public class ConnectionDrawCommandBuilder : DrawCommandBuilder
{
    private const float MaxBow = 70f;
    private const float BowFraction = 0.18f;

    public override void BuildCommand(SceneObject sceneObject, List<RenderCommand> commands, RenderContext context)
    {
        var connection = (Connection)sceneObject;

        if (connection.ParentNode is not { } parent || connection.ChildNode is not { } child)
            return;

        // Links appear only as their child is revealed, matching the progressive disclosure.
        var reveal = Math.Clamp(MathF.Min(parent.Reveal, child.Reveal), 0f, 1f);
        if (reveal <= 0f)
            return;

        var start = parent.Position + parent.Size * 0.5f;
        var end = child.Position + child.Size * 0.5f;
        var delta = end - start;
        var length = delta.Length;
        if (length < 1f)
            return;

        // Bow the two curves that share a parent apart so parallel links stay distinguishable.
        var perp = new Vector2(-delta.Y / length, delta.X / length);
        var bow = MathF.Min(MaxBow, length * BowFraction);

        commands.Add(new ConnectionRenderCommand
        {
            Start = start,
            End = end,
            Control1 = start + delta * 0.33f + perp * bow,
            Control2 = start + delta * 0.67f + perp * bow,
            Colour = new Colour(0.36f, 0.55f, 0.80f, 0.55f * reveal),
            Width = 2.5f,
        });
    }
}
