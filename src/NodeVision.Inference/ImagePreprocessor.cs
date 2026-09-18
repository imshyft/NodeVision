using System;
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
/// Converts webcam frames into the NHWC RGB [0,1] tensors MediaPipe's hand
/// models expect, using the same aspect-ratio-preserving letterbox resize as
/// MediaPipe's ImageToTensorCalculator (resize so the longer side fits, pad
/// the shorter side with black, centered).
/// </summary>
public static class ImagePreprocessor
{
    public static LetterboxResult LetterboxToTensor(in WebcamFrame frame, int targetSize)
    {
        using var bgra = Mat.FromPixelData(frame.Height, frame.Width, MatType.CV_8UC4, frame.BgraData.ToArray(), frame.Stride);
        using var bgr = new Mat();
        Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);

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
    /// Converts an already-square RGB Mat of the given size into a NHWC
    /// [0,1]-normalized tensor. Shared by <see cref="LetterboxToTensor"/> and
    /// the hand-landmark crop/rotate pipeline, which produces its own RGB
    /// crop before resizing.
    /// </summary>
    public static DenseTensor<float> RgbMatToTensor(Mat rgb, int size)
    {
        var tensor = new DenseTensor<float>(new[] { 1, size, size, 3 });
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var pixel = rgb.At<Vec3b>(y, x);
                tensor[0, y, x, 0] = pixel.Item0 / 255f;
                tensor[0, y, x, 1] = pixel.Item1 / 255f;
                tensor[0, y, x, 2] = pixel.Item2 / 255f;
            }
        }

        return tensor;
    }
}
