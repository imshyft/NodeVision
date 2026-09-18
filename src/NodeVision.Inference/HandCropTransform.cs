using System;
using NodeVision.Core;
using OpenCvSharp;

namespace NodeVision.Inference;

internal readonly record struct CropResult(Mat Image, Vector2 BoxMin, Vector2 BoxMax, Vector2 Bias);

/// <summary>
/// Shift/enlarge/crop/pad a box region out of an image, mirroring
/// opencv_zoo's mp_handpose.py _cropAndPadFromPalm exactly. Used twice by
/// the hand-landmark pipeline: once (forRotation=true) to get a padded
/// square wide enough that rotating it can't clip any content, and once
/// (forRotation=false) after rotation to get the final tight square crop
/// that gets resized to the landmark model's input.
/// </summary>
internal static class HandCropTransform
{
    private static readonly Vector2 PreRotationShift = new(0f, 0f);
    private const float PreRotationEnlarge = 4f;
    private static readonly Vector2 PostRotationShift = new(0f, -0.4f);
    private const float PostRotationEnlarge = 3f;

    public static CropResult? CropAndPadFromPalm(Mat image, Vector2 boxMin, Vector2 boxMax, bool forRotation)
    {
        var wh = boxMax - boxMin;
        var shiftVector = forRotation ? PreRotationShift : PostRotationShift;
        var shift = new Vector2(shiftVector.X * wh.X, shiftVector.Y * wh.Y);
        boxMin += shift;
        boxMax += shift;

        var center = (boxMin + boxMax) / 2f;
        wh = boxMax - boxMin;
        var enlarge = forRotation ? PreRotationEnlarge : PostRotationEnlarge;
        var halfSize = wh * (enlarge / 2f);
        boxMin = center - halfSize;
        boxMax = center + halfSize;

        var x0 = Math.Clamp((int)boxMin.X, 0, image.Width);
        var x1 = Math.Clamp((int)boxMax.X, 0, image.Width);
        var y0 = Math.Clamp((int)boxMin.Y, 0, image.Height);
        var y1 = Math.Clamp((int)boxMax.Y, 0, image.Height);

        var cropWidth = x1 - x0;
        var cropHeight = y1 - y0;
        if (cropWidth <= 0 || cropHeight <= 0)
            return null;

        using var cropView = new Mat(image, new Rect(x0, y0, cropWidth, cropHeight));

        var sideLen = forRotation
            ? (int)Math.Sqrt((double)cropHeight * cropHeight + (double)cropWidth * cropWidth)
            : Math.Max(cropHeight, cropWidth);

        var padH = sideLen - cropHeight;
        var padW = sideLen - cropWidth;
        var left = padW / 2;
        var top = padH / 2;
        var right = padW - left;
        var bottom = padH - top;

        var padded = new Mat();
        Cv2.CopyMakeBorder(cropView, padded, top, bottom, left, right, BorderTypes.Constant, Scalar.Black);

        var bias = new Vector2(x0 - left, y0 - top);
        return new CropResult(padded, new Vector2(x0, y0), new Vector2(x1, y1), bias);
    }
}
