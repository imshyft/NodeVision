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

    public HandGestureSource(InferenceEngine inferenceEngine)
    {
        _inferenceEngine = inferenceEngine;

        _gestureProcessor.PinchChanged += OnPinchChanged;
        _gestureProcessor.PinchTriggered += OnPinchTriggered;
        _gestureProcessor.HandStateChanged += OnHandStateChanged;
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
        GestureAvailable?.Invoke(new GestureEvent(GestureKind.Pinch, GesturePhase.Updated, _lastPinchPosition, pinch.Strength));
    }

    private void OnPinchTriggered(PinchEvent pinch)
    {
        var position = Normalize(pinch.Position);
        GestureAvailable?.Invoke(new GestureEvent(GestureKind.Pinch, GesturePhase.Started, position, pinch.Strength));
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
            GestureAvailable?.Invoke(new GestureEvent(gestureKind, GesturePhase.Started, _lastPinchPosition, 1f));
    }

    private Vector2 Normalize(Vector2 pixel) => new(pixel.X / _frameWidth, pixel.Y / _frameHeight);
}
