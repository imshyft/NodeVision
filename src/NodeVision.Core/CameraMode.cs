namespace NodeVision.Core;

/// <summary>
/// One capture format a camera advertises (a row in the resolution / frame-rate menu).
/// </summary>
public sealed record CameraMode(
    int Width,
    int Height,
    double Fps,
    string PixelFormat)
{
    public override string ToString()
    {
        var fps = Fps > 0 ? Fps.ToString("0.##") : "?";
        return string.IsNullOrEmpty(PixelFormat)
            ? $"{Width} x {Height} @ {fps} fps"
            : $"{Width} x {Height} @ {fps} fps ({PixelFormat})";
    }
}
