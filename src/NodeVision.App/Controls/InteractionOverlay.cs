using System.Collections.Generic;
using NodeVision.Core;

namespace NodeVision.App.Controls;

public enum PointerGesture
{
    None,
    Pointing,
    Pinching,
}

public enum InteractionPulseKind
{
    /// <summary>A gesture was recognised/started.</summary>
    Gesture,

    /// <summary>A node was expanded.</summary>
    Expand,

    /// <summary>A node was collapsed.</summary>
    Collapse,
}

/// <summary>
/// A transient expanding ring drawn at a screen position. <see cref="Progress"/> runs 0..1 over its
/// lifetime; the renderer fades it out as it grows.
/// </summary>
public readonly record struct InteractionPulse(
    Vector2 ScreenCentre,
    float Progress,
    InteractionPulseKind Kind);

/// <summary>
/// Always-on interaction feedback: where the hand cursor is, which expandable node it is over, and
/// any transient pulses. Positions are in viewport pixels (screen space), like the webcam background.
/// </summary>
public sealed record InteractionOverlay(
    Vector2? HandCursor,
    PointerGesture Pointer,
    (Vector2 Min, Vector2 Max)? HoverBounds,
    bool HoverExpanded,
    IReadOnlyList<InteractionPulse> Pulses);
