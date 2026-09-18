using System.Collections.Generic;
using NodeVision.Core;

namespace NodeVision.Visualisation;

/// <summary>
/// The parent/child structure of a scene, derived from Connection objects once per scene load.
/// Behaviours read it; nothing here animates or writes presentation values.
/// </summary>
public sealed class SceneGraph
{
    private readonly List<Node> _nodes = new();
    private readonly Dictionary<int, Node> _nodesById = new();
    private readonly Dictionary<int, int> _parentByChild = new();
    private readonly HashSet<int> _detached = new();
    private readonly Dictionary<int, Vector2> _authored = new();

    private SceneGraph()
    {
    }

    /// <summary>Nodes in draw order: deepest first, so a parent card covers its children.</summary>
    public IReadOnlyList<Node> Nodes => _nodes;

    public int Count => _nodes.Count;

    public static SceneGraph Build(Scene scene)
    {
        var graph = new SceneGraph();

        foreach (var sceneObject in scene.Objects)
        {
            if (sceneObject is not Node node)
                continue;

            graph._nodes.Add(node);
            graph._nodesById[node.Id] = node;
            graph._authored[node.Id] = node.Position;
        }

        foreach (var sceneObject in scene.Objects)
        {
            if (sceneObject is not Connection link)
                continue;
            if (!graph._nodesById.ContainsKey(link.ParentNodeId) || !graph._nodesById.ContainsKey(link.ChildNodeId))
                continue;
            if (graph._parentByChild.ContainsKey(link.ChildNodeId))
                continue; // first parent wins, and that parent is also the reveal origin

            graph._parentByChild[link.ChildNodeId] = link.ParentNodeId;
        }

        // A node whose parents never reach a root is part of an authoring cycle; treat it as a root so
        // a bad file cannot leave the whole canvas hidden.
        foreach (var node in graph._nodes)
        {
            if (graph._parentByChild.ContainsKey(node.Id) && !graph.ReachesRoot(node.Id))
                graph._detached.Add(node.Id);
        }

        graph.SortDeepestFirst();
        return graph;
    }

    public bool Contains(int nodeId) => _nodesById.ContainsKey(nodeId);

    public bool TryGetNode(int nodeId, out Node node) => _nodesById.TryGetValue(nodeId, out node!);

    public bool TryGetParent(int nodeId, out int parentId) => _parentByChild.TryGetValue(nodeId, out parentId);

    /// <summary>True when the node sits in an authoring cycle and has no real root above it.</summary>
    public bool IsDetached(int nodeId) => _detached.Contains(nodeId);

    /// <summary>The position the scene authored for a node, before any behaviour moved it.</summary>
    public Vector2 AuthoredPosition(int nodeId) => _authored.TryGetValue(nodeId, out var position) ? position : Vector2.Zero;

    /// <summary>
    /// Rewrites the scene so non-node objects stay first (a background, images) and nodes follow deepest
    /// first, which is the order the renderer draws in. Draw order is structural - it does not depend on
    /// expansion state - so this runs once per scene load, not per frame.
    /// </summary>
    public void ApplyDrawOrder(Scene scene)
    {
        var others = new List<SceneObject>();

        foreach (var sceneObject in scene.Objects)
        {
            if (sceneObject is Node node && _nodesById.ContainsKey(node.Id))
                continue;

            others.Add(sceneObject);
        }

        scene.Objects.Clear();
        scene.Objects.AddRange(others);

        foreach (var node in _nodes)
            scene.Objects.Add(node);
    }

    private void SortDeepestFirst()
    {
        var ordered = new List<(Node Node, int Depth, int Index)>();
        var index = 0;

        foreach (var node in _nodes)
            ordered.Add((node, DepthOf(node.Id), index++));

        ordered.Sort((a, b) => a.Depth != b.Depth ? b.Depth.CompareTo(a.Depth) : a.Index.CompareTo(b.Index));

        _nodes.Clear();

        foreach (var entry in ordered)
            _nodes.Add(entry.Node);
    }

    private int DepthOf(int nodeId)
    {
        var depth = 0;
        var visited = new HashSet<int>();
        var current = nodeId;

        while (_parentByChild.TryGetValue(current, out var parentId) && visited.Add(current))
        {
            current = parentId;
            depth++;
        }

        return depth;
    }

    private bool ReachesRoot(int nodeId)
    {
        var visited = new HashSet<int>();
        var current = nodeId;

        while (_parentByChild.TryGetValue(current, out var parentId))
        {
            if (!visited.Add(current))
                return false;

            current = parentId;
        }

        return true;
    }
}
