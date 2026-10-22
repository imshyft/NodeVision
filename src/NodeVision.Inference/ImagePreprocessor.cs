using System;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime.Tensors;
using NodeVision.Core;
using OpenCvSharp;

namespace NodeVision.Inference;

/// <summary>
/// Result of letterbox-resizing a frame into a square model input tensor.
/// PadLeft/PadTop are in ORIGINAL frame pixel coordinates, so decoded
/// model-space boxes can be mapped back onto the source frame.
/// </summary>
public readonly record struct LetterboxResult(
    DenseTensor<float> Tensor,
    float PadLeft,
    float PadTop,
    float Ratio);

/// <summary>
/// Converts webcam frames into the NHWC RGB [0,1]-normalized tensors MediaPipe's hand
/// models expect, using the same aspect-ratio-preserving letterbox resize as
/// MediaPipe's ImageToTensorCalculator (resize so the longer side fits, pad
/// the shorter side with black, centered).
/// </summary>
public static class ImagePreprocessor
{
    public static LetterboxResult LetterboxToTensor(in WebcamFrame frame, int targetSize)
    {
        using var bgr = new Mat();
        WithBgraMat(frame, bgra => Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR));

        var ratio = Math.Min((float)targetSize / frame.Height, (float)targetSize / frame.Width);
        var resizedWidth = (int)(frame.Width * ratio);
        var resizedHeight = (int)(frame.Height * ratio);

        using var resized = new Mat();
        Cv2.Resize(bgr, resized, new Size(resizedWidth, resizedHeight));

        var padW = targetSize - resizedWidth;
        var padH = targetSize - resizedHeight;
        var left = padW / 2;
        var top = padH / 2;
        var right = padW - left;
        var bottom = padH - top;

        using var padded = new Mat();
        Cv2.CopyMakeBorder(resized, padded, top, bottom, left, right, BorderTypes.Constant, Scalar.Black);

        using var rgb = new Mat();
        Cv2.CvtColor(padded, rgb, ColorConversionCodes.BGR2RGB);

        var tensor = RgbMatToTensor(rgb, targetSize);

        return new LetterboxResult(tensor, left / ratio, top / ratio, ratio);
    }

    /// <summary>
    /// Runs <paramref name="action"/> with a Mat over the frame's pixels without copying the frame
    /// (the ring buffer hands out borrowed memory). The Mat is only valid for the duration of the
    /// action because the pinned memory is released afterwards.
    /// </summary>
    public static void WithBgraMat(WebcamFrame frame, Action<Mat> action)
    {
        if (MemoryMarshal.TryGetArray(frame.BgraData, out var segment) && segment.Array is not null)
        {
            var handle = GCHandle.Alloc(segment.Array, GCHandleType.Pinned);
            try
            {
                var data = IntPtr.Add(handle.AddrOfPinnedObject(), segment.Offset);
                using var mat = Mat.FromPixelData(frame.Height, frame.Width, MatType.CV_8UC4, data, frame.Stride);
                action(mat);
            }
            finally
            {
                handle.Free();
            }

            return;
        }

        // Non-array-backed memory (rare): copy so the Mat stays valid.
        var copy = frame.BgraData.ToArray();
        var copyHandle = GCHandle.Alloc(copy, GCHandleType.Pinned);
        try
        {
            using var mat = Mat.FromPixelData(frame.Height, frame.Width, MatType.CV_8UC4, copyHandle.AddrOfPinnedObject(), frame.Stride);
            action(mat);
        }
        finally
        {
            copyHandle.Free();
        }
    }

    /// <summary>
    /// Converts an already-square RGB Mat of the given size into a NHWC
    /// [0,1]-normalized tensor. Copies each row once and normalizes in a tight loop, which is far
    /// cheaper than reading every pixel through the Mat indexer.
    /// </summary>
    public static DenseTensor<float> RgbMatToTensor(Mat rgb, int size)
    {
        var tensor = new DenseTensor<float>(new[] { 1, size, size, 3 });
        var step = (int)rgb.Step();
        var row = new byte[size * 3];

        for (var y = 0; y < size; y++)
        {
            Marshal.Copy(IntPtr.Add(rgb.Data, y * step), row, 0, row.Length);

            var index = 0;
            for (var x = 0; x < size; x++)
            {
                tensor[0, y, x, 0] = row[index++] / 255f;
                tensor[0, y, x, 1] = row[index++] / 255f;
                tensor[0, y, x, 2] = row[index++] / 255f;
            }
        }

        return tensor;
    }
}
