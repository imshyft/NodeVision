using System;
using System.Collections.Generic;

namespace NodeVision.Visualisation;

/// <summary>
/// The expansion feature: a node's children stay hidden until it is expanded, then they emerge from
/// the parent card. Structure comes from <see cref="SceneGraph"/>, output goes to
/// <see cref="PresentationState"/>; the scene itself is never touched.
/// </summary>
public sealed class NodeExpansion : ISceneBehaviour
{
    private const float RevealDuration = 0.22f;

    private readonly HashSet<int> _expanded = new();
    private readonly Dictionary<int, float> _reveal = new();

    public bool IsExpanded(int nodeId) => _expanded.Contains(nodeId);

    public void SetExpanded(int nodeId, bool expanded)
    {
        if (expanded)
            _expanded.Add(nodeId);
        else
            _expanded.Remove(nodeId);
    }

    public void ToggleExpanded(int nodeId) => SetExpanded(nodeId, !IsExpanded(nodeId));

    public void Build(SceneGraph graph)
    {
        _expanded.Clear();
        _reveal.Clear();

        // Seed the values, so a freshly built scene presents the right thing on the first tick
        // instead of animating everything in at once.
        foreach (var node in graph.Nodes)
            _reveal[node.Id] = TargetOf(graph, node.Id);
    }

    public void Update(float deltaTime, SceneGraph graph, PresentationState state)
    {
        var step = deltaTime / RevealDuration;

        foreach (var node in graph.Nodes)
        {
            _reveal.TryGetValue(node.Id, out var current);
            current = Approach(current, TargetOf(graph, node.Id), step);
            _reveal[node.Id] = current;
            state.For(node.Id).SetReveal(Ease(current));
        }

        // Shallowest first (the reverse of draw order), so a child interpolates from the position its
        // parent was given this frame instead of a stale one.
        for (var i = graph.Nodes.Count - 1; i >= 0; i--)
        {
            var node = graph.Nodes[i];
            if (!graph.TryGetParent(node.Id, out var parentId) || !graph.TryGetNode(parentId, out var parent))
                continue; // no parent: the node keeps the position the scene authored

            var origin = state.PositionOf(parent) + parent.Size * 0.5f;
            var authored = graph.AuthoredPosition(node.Id);
            state.For(node.Id).SetPosition(origin + (authored - origin) * Ease(_reveal[node.Id]));
        }
    }

    /// <summary>
    /// A node is revealed only while every node above it is expanded, so collapsing an ancestor hides
    /// the whole branch below it rather than just its immediate children.
    /// </summary>
    private float TargetOf(SceneGraph graph, int nodeId)
    {
        if (graph.IsDetached(nodeId))
            return 1f;

        var current = nodeId;

        for (var depth = 0; depth <= graph.Count; depth++)
        {
            if (!graph.TryGetParent(current, out var parentId))
                return 1f; // reached the top of the chain

            if (!_expanded.Contains(parentId))
                return 0f;

            current = parentId;
        }

        return 1f; // a cycle: keep it visible rather than hiding it forever
    }

    private static float Approach(float current, float target, float step) =>
        current < target ? MathF.Min(target, current + step) : MathF.Max(target, current - step);

    private static float Ease(float t) => 1f - MathF.Pow(1f - t, 3f);
}
