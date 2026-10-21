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
    bool IndexExtended,
    float IndexStraightness,
    bool MiddleCurled,
    bool RingCurled,
    bool PinkyCurled,
    bool IndexPointingUp,
    bool MiddleFolded,
    bool RingFolded,
    bool PinkyFolded,
    float MiddlePalmRatio,
    float RingPalmRatio,
    float PinkyPalmRatio,
    float MiddleThumbRatio,
    float PalmWidthRatio,
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

    // A pinch needs the index finger reaching out toward the thumb. In a closed fist the thumb and
    // index tips also sit close together, which would otherwise read as a pinch and start zooming,
    // so require the index to be at least this straight (direct MCP->tip over segment length).
    // Starting guess - tune against a real hand.
    private const float PinchMinIndexStraightness = 0.5f;

    // Zoom requires the palm turned away from the camera, like gripping a knob (the reference pose).
    // When the palm faces the camera the knuckle span (index MCP -> pinky MCP) is wide; turning the
    // hand edge-on foreshortens it. Requiring that foreshortening stops a straight-on pinch during an
    // expand/collapse from briefly starting a zoom. Starting guess - tune against a real hand.
    private const float PinchMaxPalmWidthRatio = 0.6f;

    // How long a candidate Open/Closed/Pointing reading must hold before
    // it's confirmed and reported. Short - this is noise rejection, not a
    // deliberate "hold to trigger" like pinch.
    private const float StateConfirmSeconds = 0.1f;

    // Pointing: how far off true screen-space "up" the index finger's
    // MCP->tip vector is allowed to be and still count as "pointing
    // upward". Starting guess - tune against a real hand.
    private const float PointingUpToleranceDegrees = 40f;

    // Pointing requires the three non-index fingers to be curled up into the
    // palm, not merely bent. Distance from the fingertip to the palm centre,
    // as a fraction of the hand size (wrist -> middle MCP); a fully tucked
    // finger sits well under this. Lower values are stricter. Starting guess
    // - tune against a real hand.
    private const float CurledIntoPalmRatio = 0.6f;

    // Panning is a "grab": the middle fingertip must be touching the thumb. Distance is measured
    // relative to hand size. Checked every frame with no confirmation delay, so separating the two
    // fingers ends the pan immediately instead of latching. Starting guess - tune against a hand.
    private const float PanMiddleThumbContactRatio = 0.55f;

    // Pointing requires the index finger to be almost fully extended. Measured as the ratio of the
    // direct MCP->tip distance to the sum of the three finger segments: a straight finger is ~1.0
    // regardless of the direction it points, so this still holds when the finger points at the
    // camera and its on-screen length foreshortens. A merely uncurled-but-bent finger sits well
    // below. Starting guess - tune against a real hand.
    private const float ExtendedMinStraightnessRatio = 0.9f;

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

    /// <summary>
    /// Palm-centre position in original frame pixels, emitted every tracked frame. Used as the
    /// cursor for pointing/panning because it stays far steadier than any fingertip.
    /// </summary>
    public event Action<Vector2>? HandPositionChanged;

    /// <summary>Last frame's raw classification - see <see cref="HandStateDiagnostics"/>. Null when no hand was tracked.</summary>
    public HandStateDiagnostics? LastDiagnostics { get; private set; }

    /// <summary>Last frame's smoothed landmarks - what the classifiers ran on. Empty when no hand was tracked.</summary>
    public HandLandmark[] LastSmoothedLandmarks { get; private set; } = Array.Empty<HandLandmark>();

    /// <summary>Last frame's palm centre (mean of the wrist and the four MCP joints), in frame pixels.</summary>
    public Vector2 LastPalmPosition { get; private set; }

    /// <summary>
    /// Last frame's hand roll in radians: the screen angle of the knuckle line (index MCP -> pinky
    /// MCP). Rotating the hand about the camera axis changes it, which is what drives twist-to-zoom.
    /// </summary>
    public float LastHandRoll { get; private set; }

    /// <summary>Last frame's pinch reading, for debug overlays.</summary>
    public PinchEvent LastPinch { get; private set; }

    /// <summary>Whether the pinch is currently within its on/off hysteresis band (debug/tuning aid).</summary>
    public bool IsPinching => _isPinching;

    /// <summary>
    /// True on frames where the hand is making the pan "grab": index extended, ring and pinky curled
    /// into the palm, and the middle fingertip touching the thumb. Evaluated every frame with no
    /// confirmation delay, so separating the middle finger from the thumb ends the pan immediately.
    /// </summary>
    public bool IsPanActive { get; private set; }

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
            LastSmoothedLandmarks = Array.Empty<HandLandmark>();
            IsPanActive = false;
            return;
        }

        var landmarks = _smoother.Smooth(rawLandmarks);
        LastSmoothedLandmarks = landmarks;

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
            IsPanActive = false;
            return;
        }

        var normalizedDistance = Distance(thumbTip, indexTip) / handSize;

        // Pinch only counts while the other three fingers are curled: with an open hand the thumb and
        // index are naturally close enough to read as a pinch, which would zoom constantly.
        var otherFingersCurled =
            IsFingerCurled(wrist, landmarks[10], landmarks[12]) &&
            IsFingerCurled(wrist, landmarks[14], landmarks[16]) &&
            IsFingerCurled(wrist, landmarks[18], landmarks[20]);

        // The index also has to be reaching toward the thumb rather than tucked into the palm, or a
        // closed fist (where thumb and index tips are close) would register as a pinch.
        var indexStraightness = FingerStraightness(landmarks[5], landmarks[6], landmarks[7], landmarks[8]);
        var indexReaching = indexStraightness >= PinchMinIndexStraightness;

        // And the palm has to be turned edge-on. A straight-on pinch (palm facing the camera) is what
        // briefly appears during open/fist transitions, so it must not start a zoom.
        var palmWidthRatio = Distance(landmarks[5], landmarks[17]) / handSize;
        var palmTurnedAway = palmWidthRatio <= PinchMaxPalmWidthRatio;

        var strength = otherFingersCurled && indexReaching && palmTurnedAway
            ? 1f - InverseLerpClamped(ClosedDistanceRatio, OpenDistanceRatio, normalizedDistance)
            : 0f;
        var position = new Vector2((thumbTip.X + indexTip.X) / 2f, (thumbTip.Y + indexTip.Y) / 2f);
        var pinch = new PinchEvent(position, strength);
        LastPinch = pinch;

        PinchChanged?.Invoke(pinch);
        UpdateTriggerState(pinch);

        LastPalmPosition = PalmCentre(landmarks);
        LastHandRoll = HandRoll(landmarks);
        HandPositionChanged?.Invoke(LastPalmPosition);

        var candidate = ClassifyHandState(landmarks, wrist, out var diagnosticsSoFar);
        UpdateHandState(candidate);
        LastDiagnostics = diagnosticsSoFar with { Candidate = candidate, Confirmed = _confirmedState };

        // Read from this frame's geometry, not the confirmed state, so separating the middle finger
        // from the thumb ends the pan on the next frame instead of after the confirmation delay.
        // Pan yields to pinch so a zoom grab can never also drag the canvas.
        IsPanActive = !_isPinching &&
                      diagnosticsSoFar.IndexExtended &&
                      diagnosticsSoFar.MiddleFolded &&
                      diagnosticsSoFar.RingFolded &&
                      diagnosticsSoFar.PinkyFolded &&
                      diagnosticsSoFar.MiddleThumbRatio <= PanMiddleThumbContactRatio;
    }

    /// <summary>
    /// Mean of the wrist and the four MCP joints - a stable anchor that does not swing with the
    /// finger poses the way a fingertip does, so it makes a far calmer pan cursor.
    /// </summary>
    private static Vector2 PalmCentre(HandLandmark[] landmarks)
    {
        var x = landmarks[0].X + landmarks[5].X + landmarks[9].X + landmarks[13].X + landmarks[17].X;
        var y = landmarks[0].Y + landmarks[5].Y + landmarks[9].Y + landmarks[13].Y + landmarks[17].Y;
        return new Vector2(x / 5f, y / 5f);
    }

    /// <summary>
    /// Roll of the hand about the camera axis, from the angle of the knuckle line (index MCP ->
    /// pinky MCP) in the frame. Uses two stable knuckle landmarks, so it is unaffected by how the
    /// fingers are curled.
    /// </summary>
    private static float HandRoll(HandLandmark[] landmarks)
        => MathF.Atan2(landmarks[17].Y - landmarks[5].Y, landmarks[17].X - landmarks[5].X);

    /// <summary>
    /// A finger is "curled" when its tip has folded back closer to the
    /// wrist than its own PIP (middle) joint is - a simple, orientation-
    /// tolerant heuristic since both distances share the same wrist
    /// reference point.
    /// </summary>
    private static bool IsFingerCurled(HandLandmark wrist, HandLandmark pip, HandLandmark tip)
        => Distance(tip, wrist) < Distance(pip, wrist);

    /// <summary>
    /// How far a fingertip sits from the palm centre, as a fraction of hand size. Low values mean the
    /// finger is tucked into the palm; used to require a real curl for the pointing gesture.
    /// </summary>
    private static float PalmRatio(HandLandmark tip, Vector2 palmCentre, float handSize)
    {
        if (handSize < 1e-3f)
            return float.MaxValue;

        var dx = tip.X - palmCentre.X;
        var dy = tip.Y - palmCentre.Y;
        return MathF.Sqrt(dx * dx + dy * dy) / handSize;
    }

    /// <summary>
    /// Straightness of a finger in [0, 1]: the direct MCP->tip distance over the sum of the three
    /// segment lengths. 1 is perfectly straight; a folded finger is much lower. Unlike a joint angle
    /// this reads the same whether the finger points up, sideways or straight at the camera, because
    /// foreshortening shrinks the direct distance and every segment together.
    /// </summary>
    private static float FingerStraightness(HandLandmark mcp, HandLandmark pip, HandLandmark dip, HandLandmark tip)
    {
        var path = Distance(mcp, pip) + Distance(pip, dip) + Distance(dip, tip);
        if (path < 1e-6f)
            return 0f;

        return Distance(mcp, tip) / path;
    }

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

        // Pointing needs the index almost straight (direction-independent) and the three non-index
        // fingers fully curled into the palm, both stricter than the loose curl test used for
        // Open/Closed.
        var indexStraightness = FingerStraightness(landmarks[5], landmarks[6], landmarks[7], landmarks[8]);
        var indexExtended = indexStraightness >= ExtendedMinStraightnessRatio;

        var palmCentre = PalmCentre(landmarks);
        var handSize = Distance(wrist, landmarks[9]);
        var middlePalm = PalmRatio(landmarks[12], palmCentre, handSize);
        var ringPalm = PalmRatio(landmarks[16], palmCentre, handSize);
        var pinkyPalm = PalmRatio(landmarks[20], palmCentre, handSize);

        var middleFolded = middlePalm < CurledIntoPalmRatio;
        var ringFolded = ringPalm < CurledIntoPalmRatio;
        var pinkyFolded = pinkyPalm < CurledIntoPalmRatio;

        var middleThumbRatio = Distance(landmarks[4], landmarks[12]) / handSize;
        var palmWidthRatio = Distance(landmarks[5], landmarks[17]) / handSize;

        diagnosticsSoFar = new HandStateDiagnostics(
            indexCurled, indexExtended, indexStraightness, middleCurled, ringCurled, pinkyCurled, indexPointingUp,
            middleFolded, ringFolded, pinkyFolded, middlePalm, ringPalm, pinkyPalm, middleThumbRatio, palmWidthRatio,
            Candidate: HandState.Unknown, Confirmed: HandState.Unknown);

        if (indexCurled && middleCurled && ringCurled && pinkyCurled)
            return HandState.Closed;

        if (!indexCurled && !middleCurled && !ringCurled && !pinkyCurled)
            return HandState.Open;

        if (indexExtended && middleFolded && ringFolded && pinkyFolded)
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
