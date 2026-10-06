using System;
using System.Collections.Generic;
using Avalonia.Input;
using NodeVision.Core;
using NodeVision.Inference;

namespace NodeVision.App.Integration;

/// <summary>
/// Development gesture source driven by number keys, using the mouse position as the gesture
/// location. It emits the sequences the GestureToSceneMapper expects: a point press anchors at the
/// viewport centre and moves to the pointer, and a pinch press runs a small signed strength step.
/// </summary>
public sealed class KeyboardGestureSource : IGestureSource
{
    private const float ZoomStep = 0.25f;

    private readonly record struct Binding(string Key, GestureKind Kind, float Amount, string Label);

    private static readonly Binding[] Bindings =
    {
        new("1", GestureKind.Point, 0f, "Pan"),
        new("2", GestureKind.Fist, 0f, "Collapse"),
        new("3", GestureKind.OpenHand, 0f, "Expand"),
        new("4", GestureKind.Pinch, ZoomStep, "Zoom in"),
        new("5", GestureKind.Pinch, -ZoomStep, "Zoom out"),
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

            Emit(binding, normalizedPosition);
            _last = $"{binding.Label} @ ({normalizedPosition.X:0.00}, {normalizedPosition.Y:0.00})";
            return binding.Label;
        }

        return null;
    }

    private void Emit(Binding binding, Vector2 position)
    {
        switch (binding.Kind)
        {
            case GestureKind.Point:
                // Anchor at the centre, then move to the pointer, so the mapper pans by that offset.
                GestureAvailable?.Invoke(new GestureEvent(GestureKind.Point, GesturePhase.Started, new Vector2(0.5f, 0.5f), 1f));
                GestureAvailable?.Invoke(new GestureEvent(GestureKind.Point, GesturePhase.Updated, position, 1f));
                break;

            case GestureKind.Pinch:
                // Run a small signed strength step so the mapper reads a zoom delta.
                var previous = binding.Amount > 0f ? 0f : -binding.Amount;
                GestureAvailable?.Invoke(new GestureEvent(GestureKind.Pinch, GesturePhase.Started, position, previous));
                GestureAvailable?.Invoke(new GestureEvent(GestureKind.Pinch, GesturePhase.Updated, position, previous + binding.Amount));
                break;

            default:
                GestureAvailable?.Invoke(new GestureEvent(binding.Kind, GesturePhase.Started, position, 1f));
                break;
        }
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
        _ => null,
    };
}
