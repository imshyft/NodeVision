using NodeVision.Core;

namespace NodeVision.Visualisation;

/// <summary>
/// The mapped output of a gesture: what the gesture-to-scene mapper asks the visualisation to do.
/// <see cref="VisualizationEngine"/> applies these on the visualisation loop, in order, at the start of
/// a frame.
/// <para>
/// All positions are in viewport pixels (origin at the top-left of the scene view), never canvas
/// coordinates and never normalised camera coordinates. The engine owns the camera and does the
/// conversion itself. Events that carry a position are ignored while the viewport size is not yet
/// known (zero), so an event sent before the first layout cannot act on a garbage position.
/// </para>
/// <para>
/// Cooldowns and debouncing belong to the mapper. The engine applies every event it is given.
/// </para>
/// </summary>
public abstract record SceneEvent
{
    /// <summary>
    /// <c>Stopwatch.GetTimestamp()</c> taken when the camera frame that caused this event was captured,
    /// or 0 when unknown. Only used to report event-to-applied latency.
    /// </summary>
    public long SourceTimestamp { get; init; }
}

/// <summary>
/// Moves the content on screen by <paramref name="ScreenDelta"/> pixels (drag semantics: a positive X
/// delta moves the content to the right). The engine scales it by the current zoom.
/// </summary>
public sealed record PanSceneEvent(Vector2 ScreenDelta) : SceneEvent;

/// <summary>
/// Multiplies the zoom by <paramref name="Factor"/> (1 = unchanged, above 1 zooms in, below 1 zooms
/// out) while keeping the canvas point under <paramref name="ScreenFocalPoint"/> fixed. Non-finite or
/// non-positive factors are ignored, and the resulting zoom is clamped to the engine's limits.
/// </summary>
public sealed record ZoomSceneEvent(float Factor, Vector2 ScreenFocalPoint) : SceneEvent;

/// <summary>
/// Expands the topmost visible node under <paramref name="ScreenPosition"/> so its children are
/// revealed. Does nothing when there is no node there, the node has no children, or it is already
/// expanded.
/// </summary>
public sealed record ExpandSceneEvent(Vector2 ScreenPosition) : SceneEvent;

/// <summary>
/// Collapses the topmost visible node under <paramref name="ScreenPosition"/> so its children are
/// hidden. Does nothing when there is no node there or it is not expanded.
/// </summary>
public sealed record CollapseSceneEvent(Vector2 ScreenPosition) : SceneEvent;

/// <summary>Returns the camera to the origin at the default zoom.</summary>
public sealed record ResetSceneEvent : SceneEvent;
