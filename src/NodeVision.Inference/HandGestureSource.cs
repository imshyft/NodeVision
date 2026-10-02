using NodeVision.Core;

namespace NodeVision.Inference;

/// <summary>
/// Feeds the hand-tracking pipeline (InferenceEngine -> GestureProcessor) into the gesture-event
/// transport. Pinch is reported as the frame-to-frame change in pinch strength, so closing the pinch
/// zooms one way and opening it zooms the other; holding a steady pinch emits nothing.
/// </summary>
public sealed class HandGestureSource : IGestureSource
{
    // Below this strength change the pinch counts as steady, so a hand resting at a constant pinch
    // does not keep emitting.
    private const float PinchDeltaThreshold = 0.02f;

    private readonly InferenceEngine _inferenceEngine;
    private readonly GestureProcessor _gestureProcessor = new();

    private int _frameWidth = 1;
    private int _frameHeight = 1;
    private float _lastPinchStrength;
    private Vector2 _lastPinchPosition = new(0.5f, 0.5f);
    private HandState _handState = HandState.Unknown;

    public HandGestureSource(InferenceEngine inferenceEngine)
    {
        _inferenceEngine = inferenceEngine;

        // _gestureProcessor.PinchChanged += OnPinchChanged;
        _gestureProcessor.HandStateChanged += OnHandStateChanged;
        _gestureProcessor.HandPositionChanged += OnHandPositionChanged;
    }

    public event Action<GestureEvent>? GestureAvailable;

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
        _gestureProcessor.Update(result.HandLandmarks);
    }

    private void OnPinchChanged(PinchEvent pinch)
    {
        _lastPinchPosition = Normalize(pinch.Position);

        var delta = pinch.Strength - _lastPinchStrength;
        _lastPinchStrength = pinch.Strength;

        if (MathF.Abs(delta) < PinchDeltaThreshold)
            return;

        GestureAvailable?.Invoke(new GestureEvent(GestureKind.Pinch, GesturePhase.Updated, _lastPinchPosition, delta));
    }

    private void OnHandStateChanged(HandStateChangedEvent change)
    {
        _handState = change.NewState;

        var kind = change.NewState switch
        {
            HandState.Open => GestureKind.OpenHand,
            HandState.Closed => GestureKind.Fist,
            _ => (GestureKind?)null,
        };

        if (kind is { } gestureKind)
            GestureAvailable?.Invoke(new GestureEvent(gestureKind, GesturePhase.Started, _lastPinchPosition, 1f));
    }

    private void OnHandPositionChanged(Vector2 pixel)
    {
        // Only the pointing finger pans, and it does so continuously with the tracked hand position.
        if (_handState != HandState.Pointing)
            return;

        GestureAvailable?.Invoke(new GestureEvent(GestureKind.Point, GesturePhase.Updated, Normalize(pixel), 1f));
    }

    private Vector2 Normalize(Vector2 pixel) => new(pixel.X / _frameWidth, pixel.Y / _frameHeight);
}
