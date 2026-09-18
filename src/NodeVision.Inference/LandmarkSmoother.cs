using System;

namespace NodeVision.Inference;

/// <summary>
/// Reduces frame-to-frame jitter in hand landmarks with a simple
/// exponential moving average per landmark. Stateful across calls - create
/// one instance per tracked hand and feed it consecutive frames.
/// </summary>
public sealed class LandmarkSmoother
{
    private readonly float _smoothingFactor;
    private HandLandmark[]? _previous;

    /// <param name="smoothingFactor">
    /// How much each new frame moves the smoothed value toward the raw
    /// reading, in (0, 1]. Lower = smoother but laggier; higher = more
    /// responsive but jitterier. 1 disables smoothing entirely.
    /// </param>
    public LandmarkSmoother(float smoothingFactor = 0.5f)
    {
        if (smoothingFactor <= 0f || smoothingFactor > 1f)
            throw new ArgumentOutOfRangeException(nameof(smoothingFactor), "Must be in (0, 1].");

        _smoothingFactor = smoothingFactor;
    }

    /// <summary>
    /// Smooths a frame of landmarks against the previous frame's smoothed
    /// result. An empty array (no hand detected) resets internal state, so
    /// a hand reappearing later doesn't smooth in from a stale position.
    /// </summary>
    public HandLandmark[] Smooth(HandLandmark[] raw)
    {
        if (raw.Length == 0)
        {
            _previous = null;
            return raw;
        }

        if (_previous is not { } previous || previous.Length != raw.Length)
        {
            _previous = raw;
            return raw;
        }

        var smoothed = new HandLandmark[raw.Length];
        for (var i = 0; i < raw.Length; i++)
        {
            var r = raw[i];
            var p = previous[i];
            smoothed[i] = new HandLandmark(
                Lerp(p.X, r.X, _smoothingFactor),
                Lerp(p.Y, r.Y, _smoothingFactor),
                Lerp(p.Z, r.Z, _smoothingFactor),
                r.Name);
        }

        _previous = smoothed;
        return smoothed;
    }

    private static float Lerp(float previous, float current, float alpha) => previous + (current - previous) * alpha;
}
