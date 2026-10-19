using System.Collections.Generic;
using NodeVision.Core;

namespace NodeVision.App.Controls;

public readonly record struct DebugLandmark(Vector2 Position, string Name);

/// <summary>
/// Per-frame hand geometry for the debug overlay, normalised to 0..1 against the camera frame so
/// the view can map it straight onto the (stretched) webcam background.
/// </summary>
public sealed record HandDebugSnapshot(
    IReadOnlyList<DebugLandmark> Landmarks,
    IReadOnlyList<Vector2> PalmKeypoints,
    (Vector2 Min, Vector2 Max)? PalmBox,
    bool IsPinching,
    float PinchStrength,
    Vector2? PinchPosition,
    Vector2? PointPosition,
    Vector2? PalmCenter);
