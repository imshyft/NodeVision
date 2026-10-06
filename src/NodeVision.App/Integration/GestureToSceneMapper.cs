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
///   Pinch     -> zoom, direction and amount from the (signed) magnitude;
///   OpenHand  -> expand the node under the pointer;
///   Fist      -> collapse the node under the pointer;
///   Point     -> pan toward the pointed spot.
/// </summary>
public sealed class GestureToSceneMapper : IGestureToSceneMapper
{
    private const float ZoomStep = 0.25f;
    private const float ZoomGain = 3f;
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
                // Magnitude carries both direction and size: the hand source sends the change in
                // pinch strength, the keyboard sends +/-1 for a whole step.
                var amount = Math.Clamp(gesture.Magnitude * ZoomGain, -ZoomStep, ZoomStep);
                return new[] { new ZoomSceneEvent(amount, screen) };

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
