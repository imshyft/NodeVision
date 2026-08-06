namespace NodeVision.Core;

/// <summary>
/// immutable webcam frame
/// </summary>
public readonly record struct WebcamFrame(
    long Timestamp,
    int Width,
    int Height,
    int Stride,
    ReadOnlyMemory<byte> BgraData)
{
    public int BufferSize => BgraData.Length;

    /// <summary>
    /// creates frame from buffer (copies).
    /// </summary>
    public static WebcamFrame Create(long timestamp, int width, int height, ReadOnlySpan<byte> bgraData)
    {
        var buffer = new byte[bgraData.Length];
        bgraData.CopyTo(buffer);
        var stride = width * 4;
        return new WebcamFrame(timestamp, width, height, stride, buffer);
    }

    /// <summary>
    /// creates frame wrapping existing buffer without copy
    /// </summary>
    public static WebcamFrame Wrap(long timestamp, int width, int height, int stride, Memory<byte> bgraData)
    {
        return new WebcamFrame(timestamp, width, height, stride, bgraData);
    }
}