using System.Collections.Concurrent;

namespace NodeVision.Inference;

/// <summary>
/// Thread-safe hand-off from the inference thread to the visualisation frame loop. Only the queue is
/// shared; the scene is still mutated exclusively on the UI thread.
/// </summary>
public sealed class GestureEventQueue
{
    private readonly ConcurrentQueue<GestureEvent> _events = new();

    public void Enqueue(GestureEvent gestureEvent) => _events.Enqueue(gestureEvent);

    /// <summary>Appends every pending event to <paramref name="destination"/> and returns how many there were.</summary>
    public int Drain(List<GestureEvent> destination)
    {
        var count = 0;
        while (_events.TryDequeue(out var gestureEvent))
        {
            destination.Add(gestureEvent);
            count++;
        }

        return count;
    }
}
