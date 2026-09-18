using System;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using NodeVision.Core;
using OpenCvSharp;

namespace NodeVision.Inference;

/// <summary>
/// Result of running the hand-landmark model on a detected palm.
/// Landmarks are in ORIGINAL frame pixel coordinates (X, Y); Z is relative
/// depth (scaled the same as X/Y, not an absolute unit).
/// </summary>
public readonly record struct HandLandmarkResult(
    HandLandmark[] Landmarks,
    float Presence,
    float HandednessScore);

/// <summary>
/// Standard MediaPipe 21-point hand landmark names, in model output order.
/// </summary>
public static class HandLandmarkNames
{
    public static readonly string[] Values =
    {
        "wrist",
        "thumb_cmc", "thumb_mcp", "thumb_ip", "thumb_tip",
        "index_finger_mcp", "index_finger_pip", "index_finger_dip", "index_finger_tip",
        "middle_finger_mcp", "middle_finger_pip", "middle_finger_dip", "middle_finger_tip",
        "ring_finger_mcp", "ring_finger_pip", "ring_finger_dip", "ring_finger_tip",
        "pinky_mcp", "pinky_pip", "pinky_dip", "pinky_tip",
    };
}

/// <summary>
/// Runs the hand-landmark model (hand_landmarks_detector.onnx) on the region
/// around a detected palm and decodes its output into 21 landmarks in
/// original-frame pixel coordinates.
///
/// The crop/rotate/decode logic mirrors opencv_zoo's mp_handpose.py
/// reference implementation: MediaPipe doesn't feed the palm crop straight
/// into the landmark model, it first rotates the crop so the hand is
/// roughly vertical (using 2 of the palm detector's own keypoints to compute
/// the angle), since the landmark model was trained on upright hands.
/// Output tensor order (Identity=landmarks, Identity_1=presence,
/// Identity_2=handedness, Identity_3=world landmarks) follows the
/// well-documented MediaPipe hand_landmark.tflite signature; world
/// landmarks are decoded from but currently not exposed.
/// </summary>
public sealed class HandLandmarkDetector
{
    private const int InputSize = 224;

    // Matches the reference implementation's default conf_threshold.
    private const float PresenceThreshold = 0.8f;

    private readonly InferenceSession _session;

    public HandLandmarkDetector(InferenceSession session)
    {
        _session = session;
    }

    public HandLandmarkResult? Detect(in WebcamFrame frame, PalmDetection palm)
    {
        using var bgra = Mat.FromPixelData(frame.Height, frame.Width, MatType.CV_8UC4, frame.BgraData.ToArray(), frame.Stride);
        using var bgr = new Mat();
        Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);

        var palmBoxMin = new Vector2(palm.Box.X1, palm.Box.Y1);
        var palmBoxMax = new Vector2(palm.Box.X2, palm.Box.Y2);

        // Crop 1: wide, diagonal-padded region around the palm, big enough
        // that rotating it later can't clip any content out of frame.
        var crop1 = HandCropTransform.CropAndPadFromPalm(bgr, palmBoxMin, palmBoxMax, forRotation: true);
        if (crop1 is not { } c1)
            return null;
        using var crop1Image = c1.Image;

        using var crop1Rgb = new Mat();
        Cv2.CvtColor(crop1Image, crop1Rgb, ColorConversionCodes.BGR2RGB);

        var padBias = c1.Bias;

        // Palm keypoints/box, moved into crop1's local pixel coordinates.
        var localPalmBoxMin = palmBoxMin - padBias;
        var localPalmBoxMax = palmBoxMax - padBias;
        var localPalmLandmarks = palm.PalmLandmarks.Select(p => p - padBias).ToArray();

        // Rotation angle: align the vector from wrist (index 0) to
        // middle-finger MCP (index 2) to point straight up.
        var p1 = localPalmLandmarks[0];
        var p2 = localPalmLandmarks[2];
        var radians = MathF.PI / 2f - MathF.Atan2(-(p2.Y - p1.Y), p2.X - p1.X);
        radians -= 2f * MathF.PI * MathF.Floor((radians + MathF.PI) / (2f * MathF.PI));
        var angleDegrees = radians * (180f / MathF.PI);

        var rotationCenter = (localPalmBoxMin + localPalmBoxMax) / 2f;
        using var rotationMatrix = Cv2.GetRotationMatrix2D(new Point2f(rotationCenter.X, rotationCenter.Y), angleDegrees, 1.0);

        using var rotatedImage = new Mat();
        Cv2.WarpAffine(crop1Rgb, rotatedImage, rotationMatrix, crop1Rgb.Size());

        var rotatedPalmLandmarks = localPalmLandmarks
            .Select(p => ApplyAffine(rotationMatrix, p))
            .ToArray();
        var rotatedBoxMin = new Vector2(
            rotatedPalmLandmarks.Min(p => p.X),
            rotatedPalmLandmarks.Min(p => p.Y));
        var rotatedBoxMax = new Vector2(
            rotatedPalmLandmarks.Max(p => p.X),
            rotatedPalmLandmarks.Max(p => p.Y));

        // Crop 2: tight square crop around the now-upright hand.
        var crop2 = HandCropTransform.CropAndPadFromPalm(rotatedImage, rotatedBoxMin, rotatedBoxMax, forRotation: false);
        if (crop2 is not { } c2)
            return null;
        using var crop2Image = c2.Image;

        using var resized = new Mat();
        Cv2.Resize(crop2Image, resized, new Size(InputSize, InputSize), interpolation: InterpolationFlags.Area);

        var tensor = ImagePreprocessor.RgbMatToTensor(resized, InputSize);

        using var results = _session.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor("input_1", tensor)
        });

        var rawLandmarks = results.First(r => r.Name == "Identity").AsTensor<float>();
        var presence = results.First(r => r.Name == "Identity_1").AsTensor<float>()[0, 0];
        var handedness = results.First(r => r.Name == "Identity_2").AsTensor<float>()[0, 0];

        // The palm detector can produce a weak false-positive box (e.g. on
        // hair, furniture edges) even when no hand is actually present; the
        // landmark model's own presence score catches those, so reject here
        // rather than returning landmarks decoded from a non-hand crop.
        if (presence < PresenceThreshold)
            return null;

        // Scale factor from model-space (0..224) back to crop2's pixel space.
        var cropWh = c2.BoxMax - c2.BoxMin;
        var scaleFactor = MathF.Max(cropWh.X / InputSize, cropWh.Y / InputSize);

        using var coordsRotationMatrix = Cv2.GetRotationMatrix2D(new Point2f(0, 0), angleDegrees, 1.0);
        var inverseBigRotation = InvertRotation(rotationMatrix);
        var crop2Center = (c2.BoxMin + c2.BoxMax) / 2f;
        var originalCenter = ApplyAffine(inverseBigRotation, crop2Center);

        var landmarks = new HandLandmark[HandLandmarkNames.Values.Length];
        for (var i = 0; i < landmarks.Length; i++)
        {
            var rawX = rawLandmarks[0, i * 3];
            var rawY = rawLandmarks[0, i * 3 + 1];
            var rawZ = rawLandmarks[0, i * 3 + 2];

            var centeredX = (rawX - InputSize / 2f) * scaleFactor;
            var centeredY = (rawY - InputSize / 2f) * scaleFactor;
            var scaledZ = rawZ * scaleFactor;

            var rotated = ApplyTransposedRotation(coordsRotationMatrix, new Vector2(centeredX, centeredY));

            var finalX = rotated.X + originalCenter.X + padBias.X;
            var finalY = rotated.Y + originalCenter.Y + padBias.Y;

            landmarks[i] = new HandLandmark(finalX, finalY, scaledZ, HandLandmarkNames.Values[i]);
        }

        return new HandLandmarkResult(landmarks, presence, handedness);
    }

    private static Vector2 ApplyAffine(Mat affine2X3, Vector2 point)
    {
        var m00 = affine2X3.At<double>(0, 0);
        var m01 = affine2X3.At<double>(0, 1);
        var m02 = affine2X3.At<double>(0, 2);
        var m10 = affine2X3.At<double>(1, 0);
        var m11 = affine2X3.At<double>(1, 1);
        var m12 = affine2X3.At<double>(1, 2);

        var x = (float)(m00 * point.X + m01 * point.Y + m02);
        var y = (float)(m10 * point.X + m11 * point.Y + m12);
        return new Vector2(x, y);
    }

    /// <summary>
    /// Applies the rotation part of a 2x3 affine matrix using row-vector
    /// convention (v * M), i.e. the TRANSPOSE of the standard M * v used by
    /// <see cref="ApplyAffine"/>. The reference implementation applies the
    /// final landmark de-rotation this way (np.dot(landmarks, matrix[:, :2])),
    /// which for a rotation matrix is equivalent to rotating by the negated
    /// angle - undoing the earlier forward rotation of the crop.
    /// </summary>
    private static Vector2 ApplyTransposedRotation(Mat affine2X3, Vector2 vector)
    {
        var m00 = affine2X3.At<double>(0, 0);
        var m01 = affine2X3.At<double>(0, 1);
        var m10 = affine2X3.At<double>(1, 0);
        var m11 = affine2X3.At<double>(1, 1);

        var x = (float)(m00 * vector.X + m10 * vector.Y);
        var y = (float)(m01 * vector.X + m11 * vector.Y);
        return new Vector2(x, y);
    }

    private static Mat InvertRotation(Mat affine2X3)
    {
        var m00 = affine2X3.At<double>(0, 0);
        var m01 = affine2X3.At<double>(0, 1);
        var m02 = affine2X3.At<double>(0, 2);
        var m10 = affine2X3.At<double>(1, 0);
        var m11 = affine2X3.At<double>(1, 1);
        var m12 = affine2X3.At<double>(1, 2);

        // For a pure-rotation affine (no scale), the rotation part inverts
        // via transpose; the translation inverts as -R^T * t.
        var invM00 = m00;
        var invM01 = m10;
        var invM10 = m01;
        var invM11 = m11;
        var invTx = -(invM00 * m02 + invM01 * m12);
        var invTy = -(invM10 * m02 + invM11 * m12);

        var inverse = new Mat(2, 3, MatType.CV_64F);
        inverse.Set(0, 0, invM00);
        inverse.Set(0, 1, invM01);
        inverse.Set(0, 2, invTx);
        inverse.Set(1, 0, invM10);
        inverse.Set(1, 1, invM11);
        inverse.Set(1, 2, invTy);
        return inverse;
    }
}
