using System;
using System.Collections.Generic;
using Avalonia.Input;
using NodeVision.Core;
using NodeVision.Inference;

namespace NodeVision.App.Integration;

/// <summary>
/// Development gesture source driven by number keys, using the mouse position as the gesture
/// location. Stands in for HandGestureSource so the gesture-to-scene transport can be exercised
/// without a hand-tracking model: point with the mouse, press a key to emit the gesture there.
/// </summary>
public sealed class KeyboardGestureSource : IGestureSource
{
    private readonly record struct Binding(string Key, GestureKind Kind, GesturePhase Phase, float Magnitude, string Label);

    private static readonly Binding[] Bindings =
    {
        new("1", GestureKind.Point, GesturePhase.Started, 1f, "Pan"),
        new("2", GestureKind.Fist, GesturePhase.Started, 1f, "Collapse"),
        new("3", GestureKind.OpenHand, GesturePhase.Started, 1f, "Expand"),
        new("4", GestureKind.Pinch, GesturePhase.Started, 1f, "Zoom in"),
        new("5", GestureKind.Pinch, GesturePhase.Updated, 1f, "Zoom in"),
        new("6", GestureKind.Pinch, GesturePhase.Ended, -1f, "Zoom out"),
    };

    private string? _last;

    public event Action<GestureEvent>? GestureAvailable;

    public void Start()
    {
    }

    public void Stop()
    {
    }

    /// <summary>
    /// Emits the gesture bound to <paramref name="key"/> at a normalised position, if the key is
    /// mapped. Returns the binding label, or null when the key is not a gesture key.
    /// </summary>
    public string? TryTrigger(Key key, Vector2 normalizedPosition)
    {
        var name = KeyName(key);
        if (name is null)
            return null;

        foreach (var binding in Bindings)
        {
            if (binding.Key != name)
                continue;

            GestureAvailable?.Invoke(new GestureEvent(binding.Kind, binding.Phase, normalizedPosition, binding.Magnitude));
            _last = $"{binding.Label} @ ({normalizedPosition.X:0.00}, {normalizedPosition.Y:0.00})";
            return binding.Label;
        }

        return null;
    }

    public string BuildDebugText()
    {
        var lines = new List<string> { "Gesture test keys (mouse = position):" };
        foreach (var binding in Bindings)
            lines.Add($"  {binding.Key}   {binding.Label}");

        lines.Add(_last is null ? "  last: (none)" : $"  last: {_last}");
        return string.Join(Environment.NewLine, lines);
    }

    private static string? KeyName(Key key) => key switch
    {
        Key.D1 or Key.NumPad1 => "1",
        Key.D2 or Key.NumPad2 => "2",
        Key.D3 or Key.NumPad3 => "3",
        Key.D4 or Key.NumPad4 => "4",
        Key.D5 or Key.NumPad5 => "5",
        Key.D6 or Key.NumPad6 => "6",
        _ => null,
    };
}
