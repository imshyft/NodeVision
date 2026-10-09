using System;
using System.Collections.Generic;
using NodeVision.Core;
using NodeVision.Inference;
using NodeVision.Visualisation;

namespace NodeVision.App.Integration;

/// <summary>
/// Turns a GestureEvent into the SceneEvents the visualisation loop executes.
/// </summary>
public interface IGestureToSceneMapper
{
    IReadOnlyList<SceneEvent> Map(GestureEvent gesture);
}

/// <summary>
/// Maps gesture readings onto scene events. The constructor dependencies are what the conversion
/// needs: viewportSize returns the current scene viewport in screen pixels, and screenToCanvas turns
/// a screen-pixel point into the current canvas point.
/// </summary>
public sealed class GestureToSceneMapper : IGestureToSceneMapper
{
    public GestureToSceneMapper(
        Func<Vector2> viewportSize,
        Func<Vector2, Vector2> screenToCanvas)
    {
    }

    // TODO: turn a GestureEvent into the scene events the engine runs. A GestureEvent carries a Kind
    // (Pinch, Point, OpenHand, Fist), a Phase, a normalised Position (0..1) and a Magnitude; produce
    // the matching SceneEvent - PanSceneEvent, ZoomSceneEvent, ExpandSceneEvent or
    // CollapseSceneEvent - converting the position with viewportSize (to screen pixels) and
    // screenToCanvas (to canvas).
    public IReadOnlyList<SceneEvent> Map(GestureEvent gesture) => Array.Empty<SceneEvent>();
}
