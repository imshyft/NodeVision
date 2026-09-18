using System;
using NodeVision.Core;

namespace NodeVision.Inference;

/// <summary>
/// A pinch reading for one frame: the midpoint between thumb-tip and
/// index-tip, and how pinched they are (0 = fully open, 1 = touching).
/// </summary>
public readonly record struct PinchEvent(Vector2 Position, float Strength);

/// <summary>
/// Whole-hand open/closed classification. Unknown covers both "no hand
/// tracked" and an ambiguous/transitional pose (e.g. some fingers curled,
/// some not) - it's never itself reported as a confirmed state change.
/// </summary>
public enum HandState
{
    Unknown,
    Open,
    Closed,
    Pointing,
}

/// <summary>
/// Fired once when the confirmed hand state actually changes - R05 wants
/// this edge-triggered ("state change on transition, not hold"), not fired
/// repeatedly every frame the state is held.
/// </summary>
public readonly record struct HandStateChangedEvent(HandState PreviousState, HandState NewState);

/// <summary>
/// Raw per-finger classification for one frame, exposed purely for
/// debugging/tuning (e.g. an on-screen overlay) - lets you see which
/// finger's curl reading is disagreeing with what you'd expect, and
/// whether the candidate is flickering rather than holding steady.
/// </summary>
public readonly record struct HandStateDiagnostics(
    bool IndexCurled,
    bool MiddleCurled,
    bool RingCurled,
    bool PinkyCurled,
    bool IndexPointingUp,
    HandState Candidate,
    HandState Confirmed);

/// <summary>
/// Turns smoothed hand landmarks into recognized gestures.
///
/// Pinch is reported both as a continuous per-frame reading (PinchChanged -
/// e.g. for a "charging up" UI indicator while held) and as a discrete
/// one-shot trigger once the pinch has been held past a minimum duration
/// (PinchTriggered - the "R05: held briefly to trigger" gesture). A
/// trigger only fires once per pinch; the hand has to release (strength
/// drop below the lower hysteresis threshold) before another can fire.
///
/// Open/closed hand state (HandStateChanged) comes from per-finger curl on
/// the four non-thumb fingers (thumb excluded - its curl direction is
/// ambiguous enough across natural fist/open poses that it isn't a
/// reliable signal). All four fingers have to agree before a state counts
/// as Open or Closed; anything mixed is treated as a transitional pose and
/// doesn't change the confirmed state. A candidate state also has to hold
/// briefly before it's confirmed, mirroring the pinch trigger's debounce,
/// so one noisy frame near the curl boundary can't flip the state.
/// </summary>
public sealed class GestureProcessor
{
    // Hysteresis gap between on/off thresholds avoids flickering
    // start/stop right at one boundary value from landmark noise.
    private const float PinchOnThreshold = 0.6f;
    private const float PinchOffThreshold = 0.4f;
    private const float TriggerHoldSeconds = 0.25f;

    // Thumb-tip/index-tip distance, normalized by hand size (wrist to
    // middle-finger MCP), at which we consider the pinch fully closed vs
    // fully open. Starting guesses - need tuning against a real hand.
    private const float ClosedDistanceRatio = 0.15f;
    private const float OpenDistanceRatio = 0.6f;

    // How long a candidate Open/Closed/Pointing reading must hold before
    // it's confirmed and reported. Short - this is noise rejection, not a
    // deliberate "hold to trigger" like pinch.
    private const float StateConfirmSeconds = 0.1f;

    // Pointing: how far off true screen-space "up" the index finger's
    // MCP->tip vector is allowed to be and still count as "pointing
    // upward". Starting guess - tune against a real hand.
    private const float PointingUpToleranceDegrees = 40f;

    private readonly LandmarkSmoother _smoother = new(smoothingFactor: 0.4f);

    private bool _isPinching;
    private bool _hasTriggeredThisPinch;
    private long _pinchStartTimestampMs;

    private HandState _confirmedState = HandState.Unknown;
    private HandState _candidateState = HandState.Unknown;
    private long _candidateStartMs;

    public event Action<PinchEvent>? PinchChanged;
    public event Action<PinchEvent>? PinchTriggered;
    public event Action<HandStateChangedEvent>? HandStateChanged;

    /// <summary>Last frame's raw classification - see <see cref="HandStateDiagnostics"/>. Null when no hand was tracked.</summary>
    public HandStateDiagnostics? LastDiagnostics { get; private set; }

    /// <summary>
    /// Feed one frame's raw (unsmoothed) hand landmarks in. Call once per
    /// InferenceEngine.InferenceCompleted, including with an empty array
    /// when no hand is tracked, so pinch state resets correctly.
    /// </summary>
    public void Update(HandLandmark[] rawLandmarks)
    {
        if (rawLandmarks.Length == 0)
        {
            ResetPinchState();
            ResetHandStateCandidate();
            LastDiagnostics = null;
            return;
        }

        var landmarks = _smoother.Smooth(rawLandmarks);

        var thumbTip = landmarks[4];
        var indexTip = landmarks[8];
        var wrist = landmarks[0];
        var middleMcp = landmarks[9];

        var handSize = Distance(wrist, middleMcp);
        if (handSize < 1e-3f)
        {
            ResetPinchState();
            ResetHandStateCandidate();
            LastDiagnostics = null;
            return;
        }

        var normalizedDistance = Distance(thumbTip, indexTip) / handSize;
        var strength = 1f - InverseLerpClamped(ClosedDistanceRatio, OpenDistanceRatio, normalizedDistance);
        var position = new Vector2((thumbTip.X + indexTip.X) / 2f, (thumbTip.Y + indexTip.Y) / 2f);
        var pinch = new PinchEvent(position, strength);

        PinchChanged?.Invoke(pinch);
        UpdateTriggerState(pinch);

        var candidate = ClassifyHandState(landmarks, wrist, out var diagnosticsSoFar);
        UpdateHandState(candidate);
        LastDiagnostics = diagnosticsSoFar with { Candidate = candidate, Confirmed = _confirmedState };
    }

    /// <summary>
    /// A finger is "curled" when its tip has folded back closer to the
    /// wrist than its own PIP (middle) joint is - a simple, orientation-
    /// tolerant heuristic since both distances share the same wrist
    /// reference point.
    /// </summary>
    private static bool IsFingerCurled(HandLandmark wrist, HandLandmark pip, HandLandmark tip)
        => Distance(tip, wrist) < Distance(pip, wrist);

    /// <summary>
    /// True when the vector from a finger's MCP to its tip points close
    /// enough to straight up on screen (toward the top of the camera
    /// frame, not relative to the hand's own tilt).
    /// </summary>
    private static bool IsPointingUp(HandLandmark mcp, HandLandmark tip)
    {
        var dx = tip.X - mcp.X;
        var dy = tip.Y - mcp.Y;
        var length = MathF.Sqrt(dx * dx + dy * dy);
        if (length < 1e-3f)
            return false;

        // Screen-space "up" is -Y. cos(angle) between the finger vector
        // and (0, -1) simplifies to -dy / length.
        var cosAngleFromUp = -dy / length;
        var angleFromUpDegrees = MathF.Acos(Math.Clamp(cosAngleFromUp, -1f, 1f)) * (180f / MathF.PI);
        return angleFromUpDegrees <= PointingUpToleranceDegrees;
    }

    private static HandState ClassifyHandState(HandLandmark[] landmarks, HandLandmark wrist, out HandStateDiagnostics diagnosticsSoFar)
    {
        var indexCurled = IsFingerCurled(wrist, landmarks[6], landmarks[8]);
        var middleCurled = IsFingerCurled(wrist, landmarks[10], landmarks[12]);
        var ringCurled = IsFingerCurled(wrist, landmarks[14], landmarks[16]);
        var pinkyCurled = IsFingerCurled(wrist, landmarks[18], landmarks[20]);
        var indexPointingUp = IsPointingUp(landmarks[5], landmarks[8]);

        diagnosticsSoFar = new HandStateDiagnostics(
            indexCurled, middleCurled, ringCurled, pinkyCurled, indexPointingUp,
            Candidate: HandState.Unknown, Confirmed: HandState.Unknown);

        if (indexCurled && middleCurled && ringCurled && pinkyCurled)
            return HandState.Closed;

        if (!indexCurled && !middleCurled && !ringCurled && !pinkyCurled)
            return HandState.Open;

        if (!indexCurled && middleCurled && ringCurled && pinkyCurled && indexPointingUp)
            return HandState.Pointing;

        return HandState.Unknown;
    }

    private void UpdateHandState(HandState candidate)
    {
        var now = Environment.TickCount64;

        if (candidate != _candidateState)
        {
            _candidateState = candidate;
            _candidateStartMs = now;
        }

        if (candidate == HandState.Unknown || candidate == _confirmedState)
            return;

        var heldSeconds = (now - _candidateStartMs) / 1000f;
        if (heldSeconds < StateConfirmSeconds)
            return;

        var previous = _confirmedState;
        _confirmedState = candidate;
        HandStateChanged?.Invoke(new HandStateChangedEvent(previous, candidate));
    }

    private void ResetHandStateCandidate()
    {
        _candidateState = HandState.Unknown;
    }

    private void UpdateTriggerState(PinchEvent pinch)
    {
        var now = Environment.TickCount64;

        if (!_isPinching && pinch.Strength >= PinchOnThreshold)
        {
            _isPinching = true;
            _hasTriggeredThisPinch = false;
            _pinchStartTimestampMs = now;
        }
        else if (_isPinching && pinch.Strength < PinchOffThreshold)
        {
            _isPinching = false;
            _hasTriggeredThisPinch = false;
        }

        if (_isPinching && !_hasTriggeredThisPinch)
        {
            var heldSeconds = (now - _pinchStartTimestampMs) / 1000f;
            if (heldSeconds >= TriggerHoldSeconds)
            {
                _hasTriggeredThisPinch = true;
                PinchTriggered?.Invoke(pinch);
            }
        }
    }

    private void ResetPinchState()
    {
        _isPinching = false;
        _hasTriggeredThisPinch = false;
    }

    private static float Distance(HandLandmark a, HandLandmark b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static float InverseLerpClamped(float a, float b, float v)
    {
        if (MathF.Abs(b - a) < 1e-6f)
            return 0f;

        var t = (v - a) / (b - a);
        return Math.Clamp(t, 0f, 1f);
    }
}
