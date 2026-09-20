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
    }

    public void Update(float deltaTime, SceneGraph graph, PresentationState state)
    {
        // TODO: drive the reveal every frame.
        // find each nodes target reveal from its ancestor chain (graph.TryGetParent,
        // graph.IsDetached), advance the stored reveal toward it over RevealDuration, and write it with
        // state.For(node.Id).SetReveal(...). Then write the node position with
        // state.For(node.Id).SetPosition(...), interpolating from the parent's presented position
        // (state.PositionOf) toward graph.AuthoredPosition(node.Id).
    }
}
