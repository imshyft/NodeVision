using NodeVision.Core;

namespace NodeVision.Inference;

/// <summary>
/// Feeds the hand-tracking pipeline (InferenceEngine -> GestureProcessor) into the gesture-event
/// transport.
/// </summary>
public sealed class HandGestureSource : IGestureSource
{
    private readonly InferenceEngine _inferenceEngine;
    private readonly GestureProcessor _gestureProcessor = new();

    private int _frameWidth = 1;
    private int _frameHeight = 1;
    private Vector2 _lastPinchPosition = new(0.5f, 0.5f);
    private Vector2 _lastPointPosition = new(0.5f, 0.5f);
    private bool _wasPointing;
    private bool _wasPinching;

    public HandGestureSource(InferenceEngine inferenceEngine)
    {
        _inferenceEngine = inferenceEngine;

        _gestureProcessor.PinchChanged += OnPinchChanged;
        _gestureProcessor.PinchTriggered += OnPinchTriggered;
        _gestureProcessor.HandStateChanged += OnHandStateChanged;
    }

    public event Action<GestureEvent>? GestureAvailable;

    public int FrameWidth => _frameWidth;
    public int FrameHeight => _frameHeight;

    /// <summary>Last frame's smoothed landmarks, in original frame pixels. Empty when no hand.</summary>
    public HandLandmark[] LastLandmarks => _gestureProcessor.LastSmoothedLandmarks;

    /// <summary>Last frame's palm-centre position in frame pixels - the anchor the pan is driven from.</summary>
    public Vector2 LastPalmPosition => _gestureProcessor.LastPalmPosition;

    /// <summary>Last frame's per-finger classification. Null when no hand.</summary>
    public HandStateDiagnostics? Diagnostics => _gestureProcessor.LastDiagnostics;

    /// <summary>Last frame's pinch reading.</summary>
    public PinchEvent LastPinch => _gestureProcessor.LastPinch;

    public bool IsPinching => _gestureProcessor.IsPinching;

    /// <summary>Last frame's hand roll in radians (the twist-to-zoom control).</summary>
    public float LastHandRoll => _gestureProcessor.LastHandRoll;

    /// <summary>Last palm detection (box plus keypoints), or null when nothing was detected.</summary>
    public PalmDetection? LastPalm { get; private set; }

    public float LastPresence { get; private set; }

    public float LastHandedness { get; private set; }

    /// <summary>Most recently emitted gesture, for debug overlays.</summary>
    public GestureEvent? LastGesture { get; private set; }

    public void Start()
    {
        _inferenceEngine.InferenceCompleted += OnInferenceCompleted;
    }

    public void Stop()
    {
        _inferenceEngine.InferenceCompleted -= OnInferenceCompleted;
    }

    private void OnInferenceCompleted(InferenceResult result)
    {
        _frameWidth = result.FrameWidth;
        _frameHeight = result.FrameHeight;
        LastPalm = result.Palm;
        LastPresence = result.HandPresence;
        LastHandedness = result.HandednessScore;

        _gestureProcessor.Update(result.HandLandmarks);

        // Panning follows the palm centre rather than the index tip, so the scene stays steady while
        // the pointing finger wobbles. Set after Update so it reflects this frame's smoothed pose.
        if (_gestureProcessor.LastSmoothedLandmarks.Length >= 21)
            _lastPointPosition = Normalize(_gestureProcessor.LastPalmPosition);

        var isPointing = result.HandLandmarks.Length > 8 &&
                         _gestureProcessor.LastDiagnostics?.Confirmed == HandState.Pointing;

        if (isPointing)
            Emit(new GestureEvent(GestureKind.Point, GesturePhase.Updated, _lastPointPosition, 1f));
        else if (_wasPointing)
            Emit(new GestureEvent(GestureKind.Point, GesturePhase.Ended, _lastPointPosition, 0f));

        _wasPointing = isPointing;

        // A held pinch is the "grab" that arms zoom; the hand's roll then drives it relative.
        var isPinching = _gestureProcessor.IsPinching;
        if (isPinching)
            Emit(new GestureEvent(
                GestureKind.Twist,
                _wasPinching ? GesturePhase.Updated : GesturePhase.Started,
                _lastPinchPosition,
                _gestureProcessor.LastHandRoll));
        else if (_wasPinching)
            Emit(new GestureEvent(GestureKind.Twist, GesturePhase.Ended, _lastPinchPosition, _gestureProcessor.LastHandRoll));

        _wasPinching = isPinching;
    }

    private void OnPinchChanged(PinchEvent pinch)
    {
        _lastPinchPosition = Normalize(pinch.Position);
        Emit(new GestureEvent(GestureKind.Pinch, GesturePhase.Updated, _lastPinchPosition, pinch.Strength));
    }

    private void OnPinchTriggered(PinchEvent pinch)
    {
        var position = Normalize(pinch.Position);
        Emit(new GestureEvent(GestureKind.Pinch, GesturePhase.Started, position, pinch.Strength));
    }

    private void OnHandStateChanged(HandStateChangedEvent change)
    {
        var kind = change.NewState switch
        {
            HandState.Open => GestureKind.OpenHand,
            HandState.Closed => GestureKind.Fist,
            HandState.Pointing => GestureKind.Point,
            _ => (GestureKind?)null,
        };

        if (kind is { } gestureKind)
        {
            var position = gestureKind is GestureKind.Point or GestureKind.OpenHand
                ? _lastPointPosition
                : _lastPinchPosition;
            Emit(new GestureEvent(gestureKind, GesturePhase.Started, position, 1f));
        }
    }

    private void Emit(GestureEvent gesture)
    {
        LastGesture = gesture;
        GestureAvailable?.Invoke(gesture);
    }

    private Vector2 Normalize(Vector2 pixel) => new(pixel.X / _frameWidth, pixel.Y / _frameHeight);
}
