using NodeVision.Core;

namespace NodeVision.Visualisation;

/// <summary>
/// The mapped output of a gesture.
/// gesture-to-scene mapper fills these in, VisualizationEngine.ApplySceneEvent executes them.
/// </summary>
public abstract record SceneEvent;
// TODO: change them to how they actually should be this was just slopped together so it could compile :/
public sealed record PanSceneEvent(Vector2 ScreenDelta) : SceneEvent;

public sealed record ZoomSceneEvent(float Amount, Vector2 ScreenFocalPoint) : SceneEvent;

public sealed record ExpandSceneEvent(Vector2 CanvasPosition) : SceneEvent;

public sealed record ResetSceneEvent : SceneEvent;
