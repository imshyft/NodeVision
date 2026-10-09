using System;
using System.Collections.Generic;
using NodeVision.Core;

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

    // Linear 0..1 progress of each node's reveal, advanced at a constant rate. What is presented is an
    // eased version of it, so the card is already clearly visible on the first frames.
    private readonly Dictionary<int, float> _progress = new();

    // Whether each node should currently be shown (1) or hidden (0). Recomputed every frame, parents
    // before children, and kept to avoid allocating.
    private readonly Dictionary<int, float> _target = new();

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
        _progress.Clear();
        _target.Clear();

        // Roots start shown and everything else starts hidden, so the first frame does not animate.
        // Every key is created here so Update never has to add one.
        foreach (var node in graph.Nodes)
        {
            var shown = IsRoot(graph, node.Id) ? 1f : 0f;
            _progress[node.Id] = shown;
            _target[node.Id] = shown;
        }
    }

    public void Update(float deltaTime, SceneGraph graph, PresentationState state)
    {
        var step = deltaTime / RevealDuration;
        var nodes = graph.Nodes;

        // Nodes are stored deepest first, so walking backwards visits every parent before its children
        // and a child can read its parent's target and presented position from this same frame.
        for (var i = nodes.Count - 1; i >= 0; i--)
        {
            var node = nodes[i];
            var id = node.Id;

            if (IsRoot(graph, id))
                continue; // roots are always shown and stay where the scene put them

            graph.TryGetParent(id, out var parentId);

            // A child is shown only while its parent is shown and expanded, so a collapsed ancestor
            // hides the whole subtree while each descendant keeps its own expanded flag.
            var shown = _target.TryGetValue(parentId, out var parentTarget) && parentTarget >= 1f && IsExpanded(parentId);
            var target = shown ? 1f : 0f;
            _target[id] = target;

            var progress = _progress[id];
            progress = target > progress
                ? MathF.Min(target, progress + step)
                : MathF.Max(target, progress - step);
            _progress[id] = progress;

            var eased = EaseOut(progress);
            state.For(id).SetReveal(eased);

            // The card grows out of its parent: it travels from where the parent is presented to
            // where the scene authored it.
            if (graph.TryGetNode(parentId, out var parent))
            {
                var from = state.PositionOf(parent);
                var to = graph.AuthoredPosition(id);
                state.For(id).SetPosition(from + (to - from) * eased);
            }
        }
    }

    // A root, or a node in an authoring cycle that SceneGraph treats as one.
    private static bool IsRoot(SceneGraph graph, int nodeId) => !graph.TryGetParent(nodeId, out _) || graph.IsDetached(nodeId);

    // Ends exactly at 0 and 1, so "fully revealed" (reveal >= 1) is only true once the animation is done.
    private static float EaseOut(float t)
    {
        var inverse = 1f - t;
        return 1f - inverse * inverse * inverse;
    }
}
