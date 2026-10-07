using System;
using System.Collections.Generic;
using Avalonia.Input;
using NodeVision.Core;
using NodeVision.Inference;

namespace NodeVision.App.Integration;

/// <summary>
/// Development gesture source driven by number keys, using the mouse position as the gesture
/// location. Point and pinch emit the sequences the GestureToSceneMapper expects; holding the pan key
/// streams a steady pan each frame.
/// </summary>
public sealed class KeyboardGestureSource : IGestureSource
{
    private const float ZoomStep = 0.25f;

    // How far a single pan tap moves, and how much a held pan advances each frame.
    private const float PanPressStep = 0.15f;
    private const float PanHoldStep = 0.02f;

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

    private bool _panHeld;
    private Vector2 _panPointer;
    private Vector2 _panPoint;

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

    /// <summary>
    /// Called every frame with the current pointer. While the pan key is held this streams a steady
    /// pan step toward the pointer, so the scene keeps moving without the OS key-repeat jumping.
    /// </summary>
    public void Update(Vector2 pointer)
    {
        if (!_panHeld)
            return;

        _panPointer = pointer;
        _panPoint += (_panPointer - new Vector2(0.5f, 0.5f)) * PanHoldStep;
        GestureAvailable?.Invoke(new GestureEvent(GestureKind.Point, GesturePhase.Updated, _panPoint, 1f));
    }

    /// <summary>Ends a hold started by <paramref name="key"/> (e.g. the pan key on release).</summary>
    public void Release(Key key)
    {
        if (KeyName(key) != "1")
            return;

        _panHeld = false;
        GestureAvailable?.Invoke(new GestureEvent(GestureKind.Point, GesturePhase.Ended, _panPoint, 0f));
    }

    private void Emit(Binding binding, Vector2 position)
    {
        switch (binding.Kind)
        {
            case GestureKind.Point:
                // Anchor at the centre, then move part-way toward the pointer for an immediate step;
                // Update() keeps advancing from there while the key is held.
                var centre = new Vector2(0.5f, 0.5f);
                _panPoint = centre + (position - centre) * PanPressStep;
                _panPointer = position;
                _panHeld = true;
                GestureAvailable?.Invoke(new GestureEvent(GestureKind.Point, GesturePhase.Started, centre, 1f));
                GestureAvailable?.Invoke(new GestureEvent(GestureKind.Point, GesturePhase.Updated, _panPoint, 1f));
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
