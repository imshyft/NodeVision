using System.Collections.Generic;
using NodeVision.Core;

namespace NodeVision.Visualisation;

/// <summary>
/// The presentation values a behaviour asks for on one node. Unset values mean "leave the node alone".
/// </summary>
public sealed class NodePresentation
{
    public bool HasPosition { get; private set; }
    public Vector2 Position { get; private set; }
    public bool HasReveal { get; private set; }
    public float Reveal { get; private set; }

    public void SetPosition(Vector2 position)
    {
        Position = position;
        HasPosition = true;
    }

    public void SetReveal(float reveal)
    {
        Reveal = reveal;
        HasReveal = true;
    }

    internal void Clear()
    {
        HasPosition = false;
        HasReveal = false;
    }
}

/// <summary>
/// What every behaviour in a frame agrees should be presented. Applied onto the scene in one place, so
/// Node.Reveal and a node position each have exactly one writer no matter how many features run.
/// Entries are reused between frames because this is touched every tick.
/// </summary>
public sealed class PresentationState
{
    private readonly Dictionary<int, NodePresentation> _nodes = new();

    public NodePresentation For(int nodeId)
    {
        if (!_nodes.TryGetValue(nodeId, out var state))
            _nodes[nodeId] = state = new NodePresentation();

        return state;
    }

    /// <summary>Drops this frame's intent; the per-node entries are kept and reused.</summary>
    public void Reset()
    {
        foreach (var state in _nodes.Values)
            state.Clear();
    }

    /// <summary>True when nothing hides the node: no reveal was set, or it is fully revealed.</summary>
    public bool IsVisible(int nodeId) => !_nodes.TryGetValue(nodeId, out var state) || !state.HasReveal || state.Reveal >= 1f;

    /// <summary>Where the node is being presented, falling back to where the scene put it.</summary>
    public Vector2 PositionOf(Node node) => _nodes.TryGetValue(node.Id, out var state) && state.HasPosition ? state.Position : node.Position;

    public void Apply(SceneGraph graph)
    {
        foreach (var node in graph.Nodes)
        {
            if (!_nodes.TryGetValue(node.Id, out var state))
                continue; // untouched nodes keep whatever the scene gave them

            if (state.HasReveal)
                node.Reveal = state.Reveal;

            if (state.HasPosition)
                node.Position = state.Position;
        }
    }

    /// <summary>The topmost visible node under a canvas-space point, or null.</summary>
    public int? HitTest(Vector2 canvasPoint, SceneGraph graph)
    {
        // Draw order is deepest first, so the last node in the list is the topmost one.
        for (var i = graph.Nodes.Count - 1; i >= 0; i--)
        {
            var node = graph.Nodes[i];
            if (!IsVisible(node.Id))
                continue;

            var position = PositionOf(node);
            if (canvasPoint.X >= position.X && canvasPoint.X <= position.X + node.Size.X &&
                canvasPoint.Y >= position.Y && canvasPoint.Y <= position.Y + node.Size.Y)
                return node.Id;
        }

        return null;
    }
}
