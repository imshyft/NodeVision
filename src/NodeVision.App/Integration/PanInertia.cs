using System;
using NodeVision.Core;

namespace NodeVision.App.Integration;

/// <summary>
/// Trackpad-style momentum for gesture panning: releasing during a fast move keeps the scene
/// travelling in that direction and then decays quickly. The caller feeds each frame's mapped pan
/// delta and the gesture's start/end edges, and applies whatever <see cref="Update"/> returns.
/// </summary>
public sealed class PanInertia
{
    // A release faster than this starts a fling; below it the pan simply stops. This is in gained
    // screen pixels per second (the mapper already applies PanGain), so a deliberate flick clears it
    // while an ordinary pan does not.
    private const float MinFlingSpeed = 800f;

    // The fling ends once it has slowed below this.
    private const float StopSpeed = 30f;

    // Exponential decay rate. Higher stops sooner; ~8/s has the scene settle in about half a second.
    private const float Friction = 8f;

    // If the hand held still for longer than this before the pan ended, do not fling on stale speed.
    private const double IdleTimeoutSeconds = 0.15;

    private bool _panning;
    private bool _active;
    private bool _ending;
    private bool _cancel;

    private Vector2 _velocity;
    private Vector2 _inertiaVelocity;
    private double _lastMoveTime;

    /// <summary>Call when a pan gesture starts. Cancels any fling still in progress.</summary>
    public void Begin(double now)
    {
        _panning = true;
        _active = false;
        _ending = false;
        _velocity = default;
        _inertiaVelocity = default;
        _lastMoveTime = now;
    }

    /// <summary>Call when the pan gesture ends; a fast enough release becomes a fling.</summary>
    public void End()
    {
        if (!_panning)
            return;

        _panning = false;
        _ending = true;
    }

    /// <summary>Stops any pan/fling, e.g. when a different gesture takes over.</summary>
    public void Cancel()
    {
        _cancel = true;
    }

    /// <summary>
    /// Advances one frame. <paramref name="mappedPan"/> is the total pan the gesture produced this
    /// frame, already in screen pixels. The returned delta is the extra motion the fling adds.
    /// </summary>
    public Vector2 Update(float deltaTime, double now, Vector2 mappedPan)
    {
        if (_cancel)
        {
            _cancel = false;
            _panning = false;
            _active = false;
            _ending = false;
            _velocity = default;
            _inertiaVelocity = default;
            return default;
        }

        if (mappedPan.LengthSquared > 1e-4f)
        {
            var elapsed = now - _lastMoveTime;
            if (elapsed > 1e-4)
                _velocity = mappedPan / (float)elapsed;
            _lastMoveTime = now;
        }
        else if (_panning && now - _lastMoveTime > IdleTimeoutSeconds)
        {
            // The hand stopped before the gesture ended, so there is no flick to carry.
            _velocity = default;
        }

        if (_ending)
        {
            _ending = false;
            if (_velocity.Length >= MinFlingSpeed)
            {
                _inertiaVelocity = _velocity;
                _active = true;
            }
        }

        if (!_active || _panning)
            return default;

        var delta = _inertiaVelocity * deltaTime;
        _inertiaVelocity *= MathF.Exp(-Friction * deltaTime);

        if (_inertiaVelocity.Length < StopSpeed)
            _active = false;

        return delta;
    }
}
