namespace NodeVision.Inference;

/// <summary>
/// Produces <see cref="GestureEvent"/>s from raw sensor/ML output.
/// </summary>
public interface IGestureSource
{
    event Action<GestureEvent>? GestureAvailable;

    void Start();

    void Stop();
}
