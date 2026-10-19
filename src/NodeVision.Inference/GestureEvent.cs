using NodeVision.Core;

namespace NodeVision.Inference;

public enum GesturePhase
{
    Started,
    Updated,
    Ended,
}

public enum GestureKind
{
    Pinch,
    Point,
    Fist,
    OpenHand,

    /// <summary>
    /// Hand-roll gesture. Emitted while a pinch is held; Magnitude carries the hand's roll angle in
    /// radians, and a consumer derives a relative (change-based) control from it, e.g. zoom.
    /// </summary>
    Twist,
}

/// <summary>
/// One gesture reading emitted by the ML side. Position is normalised to the camera frame (0..1);
/// </summary>
public readonly record struct GestureEvent(
    GestureKind Kind,
    GesturePhase Phase,
    Vector2 Position,
    float Magnitude);
