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
/// Converts camera-normalised gesture readings into the viewport-pixel events the engine expects.
/// State is retained only for continuous gestures: pinch strength becomes a zoom factor and point
/// movement becomes a pan delta. Open-hand and fist are edge-triggered expand and collapse requests.
/// The engine owns the camera and converts screen positions to canvas itself, so this only scales
/// normalised positions by the viewport.
/// </summary>
public sealed class GestureToSceneMapper : IGestureToSceneMapper
{
    private static readonly IReadOnlyList<SceneEvent> NoEvents = Array.Empty<SceneEvent>();

    private readonly Func<Vector2> _viewportSize;

    private bool _pinchActive;
    private float _lastPinchStrength;
    private Vector2? _lastPointPosition;

    /// <param name="viewportSize">Returns the current scene viewport in screen pixels.</param>
    public GestureToSceneMapper(Func<Vector2> viewportSize)
    {
        _viewportSize = viewportSize ?? throw new ArgumentNullException(nameof(viewportSize));
    }

    public IReadOnlyList<SceneEvent> Map(GestureEvent gesture)
    {
        return gesture.Kind switch
        {
            GestureKind.Pinch => MapPinch(gesture),
            GestureKind.Point => MapPoint(gesture),
            GestureKind.OpenHand when gesture.Phase == GesturePhase.Started =>
                new SceneEvent[] { new ExpandSceneEvent(ToScreen(gesture.Position)) },
            GestureKind.Fist when gesture.Phase == GesturePhase.Started =>
                new SceneEvent[] { new CollapseSceneEvent(ToScreen(gesture.Position)) },
            _ => NoEvents,
        };
    }

    private IReadOnlyList<SceneEvent> MapPinch(GestureEvent gesture)
    {
        if (gesture.Phase == GesturePhase.Ended)
        {
            _pinchActive = false;
            return NoEvents;
        }

        var strength = Math.Clamp(gesture.Magnitude, 0f, 1f);
        if (gesture.Phase == GesturePhase.Started || !_pinchActive)
        {
            _pinchActive = true;
            _lastPinchStrength = strength;
            return NoEvents;
        }

        var zoomDelta = strength - _lastPinchStrength;
        _lastPinchStrength = strength;
        if (MathF.Abs(zoomDelta) < 0.001f)
            return NoEvents;

        // ZoomSceneEvent carries a multiplier, not a delta, so a small change in pinch strength maps
        // to a small factor around 1 (pinching in zooms in, relaxing zooms out).
        var factor = 1f + zoomDelta;
        if (factor <= 0f)
            return NoEvents;

        return new SceneEvent[] { new ZoomSceneEvent(factor, ToScreen(gesture.Position)) };
    }

    private IReadOnlyList<SceneEvent> MapPoint(GestureEvent gesture)
    {
        if (gesture.Phase == GesturePhase.Ended)
        {
            _lastPointPosition = null;
            return NoEvents;
        }

        if (gesture.Phase == GesturePhase.Started || _lastPointPosition is null)
        {
            _lastPointPosition = gesture.Position;
            return NoEvents;
        }

        var previous = _lastPointPosition.Value;
        _lastPointPosition = gesture.Position;

        var viewport = _viewportSize();
        if (viewport.X <= 0f || viewport.Y <= 0f)
            return NoEvents;

        var screenDelta = new Vector2(
            (gesture.Position.X - previous.X) * viewport.X,
            (gesture.Position.Y - previous.Y) * viewport.Y);

        if (screenDelta.LengthSquared < 0.01f)
            return NoEvents;

        return new SceneEvent[] { new PanSceneEvent(screenDelta) };
    }

    private Vector2 ToScreen(Vector2 normalizedPosition)
    {
        var viewport = _viewportSize();
        return new Vector2(
            Math.Clamp(normalizedPosition.X, 0f, 1f) * viewport.X,
            Math.Clamp(normalizedPosition.Y, 0f, 1f) * viewport.Y);
    }
}
