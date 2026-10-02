using System;
using System.Collections.Generic;
using NodeVision.Core;
using NodeVision.Inference;
using NodeVision.Visualisation;

namespace NodeVision.App.Integration;

/// <summary>
/// Turns a GestureEvent into the SceneEvents the visualisation loop executes. Positions arrive
/// normalised to the camera frame (0..1) and are converted to the screen/canvas spaces the engine
/// works in.
/// </summary>
public interface IGestureToSceneMapper
{
    IReadOnlyList<SceneEvent> Map(GestureEvent gesture);
}

/// <summary>
/// The gesture-to-scene mapping:
///   Pinch     -> zoom (in while held, out on release);
///   OpenHand  -> expand the node under the pointer;
///   Fist      -> collapse the node under the pointer;
///   Point     -> pan toward the pointed spot.
/// </summary>
public sealed class GestureToSceneMapper : IGestureToSceneMapper
{
    private const float ZoomStep = 0.12f;
    private const float PanFactor = 0.15f;

    private readonly VisualizationEngine _engine;
    private readonly Func<Vector2> _viewport;

    public GestureToSceneMapper(VisualizationEngine engine, Func<Vector2> viewport)
    {
        _engine = engine;
        _viewport = viewport;
    }

    public IReadOnlyList<SceneEvent> Map(GestureEvent gesture)
    {
        var viewport = _viewport();
        var screen = new Vector2(gesture.Position.X * viewport.X, gesture.Position.Y * viewport.Y);

        switch (gesture.Kind)
        {
            case GestureKind.Pinch:
                // Held pinches zoom in; releasing zooms back out. A negative magnitude (used by the
                // debug source) overrides the phase so a single press can zoom either way.
                var direction = gesture.Phase == GesturePhase.Ended || gesture.Magnitude < 0f ? -1f : 1f;
                return new[] { new ZoomSceneEvent(ZoomStep * direction, screen) };

            case GestureKind.Point:
                var centre = viewport * 0.5f;
                return new[] { new PanSceneEvent((centre - screen) * PanFactor) };

            case GestureKind.OpenHand:
                return new[] { new ExpandSceneEvent(_engine.ScreenToCanvas(screen, viewport)) };

            case GestureKind.Fist:
                return new[] { new CollapseSceneEvent(_engine.ScreenToCanvas(screen, viewport)) };

            default:
                return Array.Empty<SceneEvent>();
        }
    }
}
