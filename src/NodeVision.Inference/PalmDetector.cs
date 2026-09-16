using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using NodeVision.Core;
using OpenCvSharp;
using OpenCvSharp.Dnn;

namespace NodeVision.Inference;

/// <summary>
/// A detected hand's bounding box, in ORIGINAL frame pixel coordinates.
/// </summary>
public readonly record struct HandBoundingBox(float X1, float Y1, float X2, float Y2, float Score)
{
    public float Width => X2 - X1;
    public float Height => Y2 - Y1;
    public float CenterX => (X1 + X2) / 2f;
    public float CenterY => (Y1 + Y2) / 2f;
}

/// <summary>
/// Runs the BlazePalm hand detector (hand_detector.onnx) and decodes its raw
/// SSD anchor output into a single best hand bounding box.
/// Decode/NMS logic mirrors opencv_zoo's mp_palmdet.py reference
/// implementation, which is the only place the exact anchor-to-output
/// mapping and postprocessing constants for this model are documented.
/// </summary>
public sealed class PalmDetector
{
    private const int InputSize = 192;
    private const float ScoreThreshold = 0.5f;
    private const float NmsThreshold = 0.3f;
    private const int TopK = 5000;

    private readonly InferenceSession _session;

    public PalmDetector(InferenceSession session)
    {
        _session = session;
    }

    public HandBoundingBox? Detect(in WebcamFrame frame)
    {
        var preprocessed = ImagePreprocessor.LetterboxToTensor(frame, InputSize);

        using var results = _session.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor("input_1", preprocessed.Tensor)
        });

        var boxAndLandmarkDeltas = results.First(r => r.Name == "Identity").AsTensor<float>();
        var scoresRaw = results.First(r => r.Name == "Identity_1").AsTensor<float>();

        var scale = Math.Max(frame.Width, frame.Height);

        var boxes = new List<Rect2d>();
        var scores = new List<float>();

        for (var i = 0; i < PalmAnchors.Count; i++)
        {
            var score = 1f / (1f + MathF.Exp(-scoresRaw[0, i, 0]));
            if (score < ScoreThreshold)
                continue;

            var (anchorX, anchorY) = PalmAnchors.Values[i];

            var cxDelta = boxAndLandmarkDeltas[0, i, 0] / InputSize;
            var cyDelta = boxAndLandmarkDeltas[0, i, 1] / InputSize;
            var wDelta = boxAndLandmarkDeltas[0, i, 2] / InputSize;
            var hDelta = boxAndLandmarkDeltas[0, i, 3] / InputSize;

            var x1 = (cxDelta - wDelta / 2f + anchorX) * scale - preprocessed.PadLeft;
            var y1 = (cyDelta - hDelta / 2f + anchorY) * scale - preprocessed.PadTop;
            var x2 = (cxDelta + wDelta / 2f + anchorX) * scale - preprocessed.PadLeft;
            var y2 = (cyDelta + hDelta / 2f + anchorY) * scale - preprocessed.PadTop;

            boxes.Add(new Rect2d(x1, y1, x2 - x1, y2 - y1));
            scores.Add(score);
        }

        if (boxes.Count == 0)
            return null;

        CvDnn.NMSBoxes(boxes, scores, ScoreThreshold, NmsThreshold, out var keepIndices, eta: 1f, topK: TopK);

        if (keepIndices.Length == 0)
            return null;

        var bestIndex = keepIndices.OrderByDescending(idx => scores[idx]).First();
        var best = boxes[bestIndex];

        return new HandBoundingBox(
            (float)best.X, (float)best.Y,
            (float)(best.X + best.Width), (float)(best.Y + best.Height),
            scores[bestIndex]);
    }
}
