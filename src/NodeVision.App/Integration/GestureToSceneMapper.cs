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
/// State is retained only for continuous gestures: a pinch's hand rotation becomes a relative zoom
/// and point movement becomes a pan delta. Open-hand and fist are edge-triggered expand and collapse
/// requests. The engine owns the camera and converts screen positions to canvas itself, so this only
/// scales normalised positions by the viewport.
/// </summary>
public sealed class GestureToSceneMapper : IGestureToSceneMapper
{
    private static readonly IReadOnlyList<SceneEvent> NoEvents = Array.Empty<SceneEvent>();

    // How much the zoom multiplies per radian of hand roll. Above the deadband, a small twist gives
    // a small factor and the response is multiplicative, so the whole zoom range is reachable by
    // continuing to rotate. Starting guess - tune against a real hand.
    private const float TwistZoomGain = 1.2f;

    // Ignore roll changes below this (radians) so a nearly-still hand does not jitter the zoom.
    private const float TwistDeadbandRadians = 0.01f;

    // Hand movement is multiplied by this before it becomes a pan. Without it the scene moves 1:1
    // with the palm's on-screen travel, so you can only pan as far as your hand physically reaches;
    // the gain lets a small hand movement carry the scene much further. Starting guess - tune.
    private const float PanGain = 3f;

    private readonly Func<Vector2> _viewportSize;

    private bool _twistActive;
    private float _lastRoll;
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
            GestureKind.Twist => MapTwist(gesture),
            GestureKind.Point => MapPoint(gesture),
            GestureKind.OpenHand when gesture.Phase == GesturePhase.Started =>
                new SceneEvent[] { new ExpandSceneEvent(ToScreen(gesture.Position)) },
            GestureKind.Fist when gesture.Phase == GesturePhase.Started =>
                new SceneEvent[] { new CollapseSceneEvent(ToScreen(gesture.Position)) },
            _ => NoEvents,
        };
    }

    private IReadOnlyList<SceneEvent> MapTwist(GestureEvent gesture)
    {
        if (gesture.Phase == GesturePhase.Ended)
        {
            _twistActive = false;
            return NoEvents;
        }

        var roll = gesture.Magnitude;
        if (gesture.Phase == GesturePhase.Started || !_twistActive)
        {
            _twistActive = true;
            _lastRoll = roll;
            return NoEvents;
        }

        var delta = NormalizeAngle(roll - _lastRoll);
        _lastRoll = roll;
        if (MathF.Abs(delta) < TwistDeadbandRadians)
            return NoEvents;

        // Multiplicative so rotation keeps scaling the current zoom rather than adding a fixed step.
        var factor = MathF.Exp(TwistZoomGain * delta);
        return new SceneEvent[] { new ZoomSceneEvent(factor, ToScreen(gesture.Position)) };
    }

    private static float NormalizeAngle(float radians)
    {
        while (radians > MathF.PI)
            radians -= 2f * MathF.PI;
        while (radians < -MathF.PI)
            radians += 2f * MathF.PI;
        return radians;
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
            (gesture.Position.X - previous.X) * viewport.X * PanGain,
            (gesture.Position.Y - previous.Y) * viewport.Y * PanGain);

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
