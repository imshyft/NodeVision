namespace NodeVision.Visualisation;

/// <summary>
/// One visualisation feature: reads the scene structure, writes presentation values. Behaviours run in
/// the order they were added, so a later behaviour wins where two write the same value.
/// </summary>
public interface ISceneBehaviour
{
    /// <summary>Called whenever the scene structure changes, so per-scene state can be rebuilt.</summary>
    void Build(SceneGraph graph);

    void Update(float deltaTime, SceneGraph graph, PresentationState state);
}