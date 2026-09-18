using System;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace NodeVision.Inference;

/// <summary>
/// Precomputed anchor centers for the BlazePalm (hand_detector.onnx) SSD output.
/// Extracted verbatim from the reference implementation (opencv_zoo's
/// mp_palmdet.py) rather than regenerated, since the anchor order must match
/// the model's output tensor exactly - a mismatch here silently corrupts
/// every decoded box.
/// </summary>
public static class PalmAnchors
{
    public const int Count = 2016;

    public static readonly (float X, float Y)[] Values = Load();

    private static (float X, float Y)[] Load()
    {
        const string resourceName = "NodeVision.Inference.Resources.PalmAnchors.csv";
        var assembly = Assembly.GetExecutingAssembly();

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream);

        var anchors = new (float X, float Y)[Count];
        for (var i = 0; i < Count; i++)
        {
            var line = reader.ReadLine()
                ?? throw new InvalidOperationException($"Anchor resource has fewer than {Count} entries.");
            var parts = line.Split(',', StringSplitOptions.TrimEntries);
            anchors[i] = (
                float.Parse(parts[0], CultureInfo.InvariantCulture),
                float.Parse(parts[1], CultureInfo.InvariantCulture));
        }

        return anchors;
    }
}
