using NodeVision.Core;

namespace NodeVision.Inference;

/// <summary>
/// Feeds the hand-tracking pipeline (InferenceEngine -> GestureProcessor) into the gesture-event
/// transport.
/// </summary>
public sealed class HandGestureSource : IGestureSource
{
    // Open/fist only fire after being held this long, so relaxing the pointing pose to stop a pan
    // is not immediately interpreted as an expand/collapse.
    private const long FistHoldMs = 450;

    private readonly InferenceEngine _inferenceEngine;
    private readonly GestureProcessor _gestureProcessor = new();

    private int _frameWidth = 1;
    private int _frameHeight = 1;
    private Vector2 _lastPinchPosition = new(0.5f, 0.5f);
    private Vector2 _lastPointPosition = new(0.5f, 0.5f);
    private bool _wasPointing;
    private bool _wasPinching;

    private long _fistSinceMs = -1;
    private bool _fistTriggered;

    // Expand is only armed by a Closed -> Open transition, so releasing a point (Pointing -> Open)
    // never expands.
    private bool _expandArmed;

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

    /// <summary>The pan grab is held: index out, other fingers curled, middle touching the thumb.</summary>
    public bool IsPanActive => _gestureProcessor.IsPanActive;

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

        // Pan uses a per-frame grab gate rather than the confirmed hand state, so the moment the
        // middle finger leaves the thumb the pan ends (no state-confirmation lag).
        var isPanning = _gestureProcessor.IsPanActive;
        if (isPanning)
            Emit(new GestureEvent(
                GestureKind.Point,
                _wasPointing ? GesturePhase.Updated : GesturePhase.Started,
                _lastPointPosition,
                1f));
        else if (_wasPointing)
            Emit(new GestureEvent(GestureKind.Point, GesturePhase.Ended, _lastPointPosition, 0f));

        _wasPointing = isPanning;

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

        UpdateHeldGestures();
    }

    /// <summary>
    /// Emits the expand armed by a Closed -> Open transition, and fires collapse once a fist has been
    /// held deliberately. A fist held long enough to collapse does not also arm an expand, so opening
    /// afterwards does not immediately re-expand the node.
    /// </summary>
    private void UpdateHeldGestures()
    {
        if (_expandArmed)
        {
            _expandArmed = false;
            Emit(new GestureEvent(GestureKind.OpenHand, GesturePhase.Started, _lastPointPosition, 1f));
        }

        var diagnostics = _gestureProcessor.LastDiagnostics;
        var confirmed = diagnostics?.Confirmed ?? HandState.Unknown;
        var now = Environment.TickCount64;

        if (confirmed == HandState.Closed)
        {
            if (_fistSinceMs < 0)
                _fistSinceMs = now;

            if (!_fistTriggered && now - _fistSinceMs >= FistHoldMs)
            {
                _fistTriggered = true;
                Emit(new GestureEvent(GestureKind.Fist, GesturePhase.Started, _lastPointPosition, 1f));
            }
        }
        else
        {
            _fistSinceMs = -1;

            // On the frame the hand opens, OnHandStateChanged has already read this to decide whether
            // the fist collapsed the node; only clear it after that.
            _fistTriggered = false;
        }
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
        // The pan edge (Point) is driven per-frame by IsPanActive, not by this confirmed transition.
        // Expand only when the palm opens after being closed. Releasing a point (Pointing -> Open)
        // must not expand, and neither must opening after a fist that already collapsed the node.
        if (change.PreviousState == HandState.Closed && change.NewState == HandState.Open && !_fistTriggered)
            _expandArmed = true;
    }

    private void Emit(GestureEvent gesture)
    {
        LastGesture = gesture;
        GestureAvailable?.Invoke(gesture);
    }

    private Vector2 Normalize(Vector2 pixel) => new(pixel.X / _frameWidth, pixel.Y / _frameHeight);
}
