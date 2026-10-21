using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices.Swift;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Rendering;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using NodeVision.Core;
using NodeVision.Rendering;
using NodeVision.Rendering.Skia;
using SkiaSharp;

namespace NodeVision.App.Controls;

public class SceneView : Control, ICustomHitTest
{
    private const float ZoomStep = 0.1f;

    private readonly RenderBuilder _renderBuilder = new();
    private readonly SkiaRenderer _renderer = new();

    private WebcamFrame? _latestFrame;
    private readonly object _frameLock = new();
    private int _webcamConsumerId = -1;
    private WebcamFrameRingBuffer? _ringBuffer;

    private const float ClickThreshold = 4f;

    private bool _isPanning;
    private bool _movedWhilePressed;
    private Point _pressPosition;
    private Point _lastPointerPosition;

    public event Action<Vector2>? PanRequested;
    public event Action<float, Vector2>? ZoomRequested;

    /// <summary>
    /// Raised with the control-space position of a press that stayed still, i.e. a click rather
    /// than the start of a pan.
    /// </summary>
    public event Action<Vector2>? Clicked;

    public Scene? Scene { get; set; }
    public Vector2 CameraTranslation { get; set; }
    public float CameraZoom { get; set; } = 1f;

    /// <summary>When true and <see cref="HandDebug"/> is set, the hand-tracking overlay is drawn on top.</summary>
    public bool DebugOverlayEnabled { get; set; }

    /// <summary>Latest hand geometry to draw when <see cref="DebugOverlayEnabled"/> is on.</summary>
    public HandDebugSnapshot? HandDebug { get; set; }

    /// <summary>Always-on interaction feedback (cursor, hover target, expand/collapse pulses).</summary>
    public InteractionOverlay? Interaction { get; set; }

    public Vector2 ViewportSize => new Vector2((float)Bounds.Width, (float)Bounds.Height);

    // A control whose only content is custom draw operations is not hit tested unless it opts in,
    // so without this no pointer event ever reaches the control.
    public bool HitTest(Point point) => new Rect(0, 0, Bounds.Width, Bounds.Height).Contains(point);

    /// <summary>
    /// Registers this view as a consumer of the buffer
    /// </summary>
    public void SetWebcamSource(WebcamFrameRingBuffer ringBuffer)
    {
        _ringBuffer = ringBuffer;
        _webcamConsumerId = ringBuffer.RegisterConsumer();
    }

    /// <summary>
    /// Polls the buffer for the latest frame.
    /// </summary>
    public void UpdateWebcamFrame()
    {
        if (_ringBuffer == null || _webcamConsumerId < 0)
            return;

        if (_ringBuffer.TryRead(_webcamConsumerId, out var frame))
        {
            lock (_frameLock)
            {
                _latestFrame = frame;
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        _isPanning = true;
        _movedWhilePressed = false;
        _pressPosition = e.GetPosition(this);
        _lastPointerPosition = _pressPosition;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_isPanning)
            return;

        var position = e.GetPosition(this);
        var delta = position - _lastPointerPosition;
        _lastPointerPosition = position;

        if (Math.Abs(position.X - _pressPosition.X) > ClickThreshold || Math.Abs(position.Y - _pressPosition.Y) > ClickThreshold)
            _movedWhilePressed = true;

        PanRequested?.Invoke(new Vector2((float)delta.X, (float)delta.Y));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!_isPanning)
            return;

        _isPanning = false;
        e.Pointer.Capture(null);

        if (_movedWhilePressed)
            return;

        var clickPosition = e.GetPosition(this);
        Clicked?.Invoke(new Vector2((float)clickPosition.X, (float)clickPosition.Y));
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        var focalPoint = e.GetPosition(this);
        ZoomRequested?.Invoke((float)e.Delta.Y * ZoomStep, new Vector2((float)focalPoint.X, (float)focalPoint.Y));
        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        DrawWebcamBackground(context);

        if (Scene != null)
        {
            var renderContext = new RenderContext
            {
                CameraTranslation = CameraTranslation,
                CameraZoom = CameraZoom,
                RenderTargetSize = new Vector2((float)Bounds.Width, (float)Bounds.Height)
            };

            var commands = _renderBuilder.BuildScene(Scene, renderContext);

            context.Custom(
                new SceneDrawOperation(
                    new Rect(0, 0, (float)Bounds.Width, (float)Bounds.Height),
                    _renderer,
                    commands,
                    renderContext));
        }

        if (Interaction is { } interaction)
        {
            context.Custom(
                new InteractionDrawOperation(
                    new Rect(0, 0, (float)Bounds.Width, (float)Bounds.Height),
                    interaction));
        }

        if (DebugOverlayEnabled && HandDebug is { } handDebug)
        {
            context.Custom(
                new HandDebugDrawOperation(
                    new Rect(0, 0, (float)Bounds.Width, (float)Bounds.Height),
                    handDebug));
        }
    }

    private void DrawWebcamBackground(DrawingContext context)
    {
        WebcamFrame frame;

        lock (_frameLock)
        {
            if (_latestFrame == null)
                return;

            frame = _latestFrame.Value;
        }

        var rect = new Rect(0, 0, Bounds.Width, Bounds.Height);
        context.Custom(new WebcamDrawOperation(rect, frame));
    }
}

internal sealed class WebcamDrawOperation : ICustomDrawOperation
{
    private static readonly SKSamplingOptions Sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);

    private readonly WebcamFrame _frame;
    private readonly Rect _bounds;

    public WebcamDrawOperation(Rect bounds, WebcamFrame frame)
    {
        _bounds = bounds;
        _frame = frame;
    }

    public Rect Bounds => _bounds;

    public bool HitTest(Point p) => false;

    public bool Equals(ICustomDrawOperation? other)
        => ReferenceEquals(this, other);

    public void Dispose()
    {
    }

    public void Render(ImmediateDrawingContext context)
    {
        if (context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature))
            is not ISkiaSharpApiLeaseFeature leaseFeature)
            return;

        using var lease = leaseFeature.Lease();
        var canvas = lease.SkCanvas;

        // Owned entirely by this operation: never shared with or disposed by another thread.
        using var image = SkiaHelpers.CreateImage(_frame);

        using (context.PushClip(_bounds))
        {
            var dstRect = new SKRect(
                (float)_bounds.X,
                (float)_bounds.Y,
                (float)_bounds.Right,
                (float)_bounds.Bottom);

            canvas.DrawImage(image, dstRect, Sampling);
        }
    }
}

internal sealed class SceneDrawOperation : ICustomDrawOperation
{
    private readonly SkiaRenderer _renderer;
    private readonly IReadOnlyList<RenderCommand> _commands;
    private readonly RenderContext _renderContext;

    public SceneDrawOperation(
        Rect bounds,
        SkiaRenderer renderer,
        IReadOnlyList<RenderCommand> commands,
        RenderContext renderContext)
    {
        Bounds = bounds;
        _renderer = renderer;
        _commands = commands;
        _renderContext = renderContext;
    }

    public Rect Bounds { get; }

    public bool HitTest(Point p) => false;

    public bool Equals(ICustomDrawOperation? other)
        => ReferenceEquals(this, other);

    public void Dispose()
    {
    }

    public void Render(ImmediateDrawingContext context)
    {
        if (context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature))
            is not ISkiaSharpApiLeaseFeature leaseFeature)
            return;

        using var lease = leaseFeature.Lease();
        var canvas = lease.SkCanvas;

        using (context.PushClip(Bounds))
        {
            _renderer.BeginRender(canvas);
            _renderer.Render(_commands, _renderContext);
            _renderer.EndRender();
        }
    }
}

/// <summary>
/// Draws the hand-tracking debug geometry: palm box and keypoints, the 21-landmark skeleton,
/// numbered joints, the pinch marker (sized by strength) and the pointing crosshair. All points
/// arrive normalised to the camera frame, which is what the background image is stretched to.
/// </summary>
internal sealed class HandDebugDrawOperation : ICustomDrawOperation
{
    // MediaPipe hand skeleton: wrist/palm, then each finger chain, then the palm close.
    private static readonly (int A, int B)[] Connections =
    {
        (0, 1), (1, 2), (2, 3), (3, 4),
        (0, 5), (5, 6), (6, 7), (7, 8),
        (5, 9), (9, 10), (10, 11), (11, 12),
        (9, 13), (13, 14), (14, 15), (15, 16),
        (13, 17), (17, 18), (18, 19), (19, 20),
        (0, 17),
    };

    private static readonly HashSet<int> Fingertips = new() { 4, 8, 12, 16, 20 };

    private readonly Rect _bounds;
    private readonly HandDebugSnapshot _hand;

    public HandDebugDrawOperation(Rect bounds, HandDebugSnapshot hand)
    {
        _bounds = bounds;
        _hand = hand;
    }

    public Rect Bounds => _bounds;

    public bool HitTest(Point p) => false;

    public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);

    public void Dispose()
    {
    }

    public void Render(ImmediateDrawingContext context)
    {
        if (context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature))
            is not ISkiaSharpApiLeaseFeature leaseFeature)
            return;

        using var lease = leaseFeature.Lease();
        var canvas = lease.SkCanvas;

        using (context.PushClip(_bounds))
        {
            if (_hand.Occluded)
                DrawOcclusion(canvas);

            DrawPalm(canvas);
            DrawSkeleton(canvas);
            DrawJoints(canvas);
            DrawPinch(canvas);
            DrawPoint(canvas);
            DrawPalmCentre(canvas);
        }
    }

    /// <summary>
    /// A translucent hand-shaped glow drawn over whatever scene content covers the hand, so the
    /// presenter can still see roughly where it is. The regular skeleton is drawn on top of it.
    /// </summary>
    private void DrawOcclusion(SKCanvas canvas)
    {
        if (_hand.Landmarks.Count == 0)
            return;

        using var glow = new SKPaint
        {
            Color = new SKColor(255, 190, 60, 70),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 18f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };
        using var fill = new SKPaint
        {
            Color = new SKColor(255, 190, 60, 45),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };

        foreach (var (a, b) in Connections)
        {
            if (a >= _hand.Landmarks.Count || b >= _hand.Landmarks.Count)
                continue;

            canvas.DrawLine(Map(_hand.Landmarks[a].Position), Map(_hand.Landmarks[b].Position), glow);
        }

        if (_hand.PalmCenter is { } palm)
        {
            var point = Map(palm);
            canvas.DrawCircle(point, 34f, fill);

            using var font = new SKFont { Size = 12f };
            using var outline = new SKPaint { Color = new SKColor(0, 0, 0, 220), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3f };
            using var text = new SKPaint { Color = new SKColor(255, 214, 120), IsAntialias = true, Style = SKPaintStyle.Fill };

            canvas.DrawText("hand behind content", point.X + 42f, point.Y - 24f, SKTextAlign.Left, font, outline);
            canvas.DrawText("hand behind content", point.X + 42f, point.Y - 24f, SKTextAlign.Left, font, text);
        }
    }

    private SKPoint Map(Vector2 normalized) => new(
        (float)(_bounds.X + normalized.X * _bounds.Width),
        (float)(_bounds.Y + normalized.Y * _bounds.Height));

    private void DrawPalm(SKCanvas canvas)
    {
        using var boxPaint = new SKPaint
        {
            Color = new SKColor(255, 200, 0, 220),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
        };
        using var pointPaint = new SKPaint
        {
            Color = new SKColor(255, 0, 200, 220),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };

        if (_hand.PalmBox is { } box)
        {
            var min = Map(box.Min);
            var max = Map(box.Max);
            canvas.DrawRect(new SKRect(min.X, min.Y, max.X, max.Y), boxPaint);
        }

        foreach (var keypoint in _hand.PalmKeypoints)
            canvas.DrawCircle(Map(keypoint), 3.5f, pointPaint);
    }

    private void DrawSkeleton(SKCanvas canvas)
    {
        if (_hand.Landmarks.Count == 0)
            return;

        using var paint = new SKPaint
        {
            Color = new SKColor(0, 220, 255, 220),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3f,
        };

        foreach (var (a, b) in Connections)
        {
            if (a >= _hand.Landmarks.Count || b >= _hand.Landmarks.Count)
                continue;

            canvas.DrawLine(Map(_hand.Landmarks[a].Position), Map(_hand.Landmarks[b].Position), paint);
        }
    }

    private void DrawJoints(SKCanvas canvas)
    {
        if (_hand.Landmarks.Count == 0)
            return;

        using var jointPaint = new SKPaint { Color = new SKColor(255, 255, 255, 230), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var tipPaint = new SKPaint { Color = new SKColor(80, 255, 120, 255), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var wristPaint = new SKPaint { Color = new SKColor(255, 160, 40, 255), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var font = new SKFont { Size = 10f };
        using var outline = new SKPaint { Color = new SKColor(0, 0, 0, 220), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.5f };
        using var textPaint = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Fill };

        for (var i = 0; i < _hand.Landmarks.Count; i++)
        {
            var point = Map(_hand.Landmarks[i].Position);
            var isTip = Fingertips.Contains(i);

            canvas.DrawCircle(point, i == 0 ? 6f : isTip ? 5f : 3.5f, i == 0 ? wristPaint : isTip ? tipPaint : jointPaint);

            var label = i.ToString();
            var x = point.X - font.MeasureText(label) / 2f;
            var y = point.Y - 8f;
            canvas.DrawText(label, x, y, SKTextAlign.Left, font, outline);
            canvas.DrawText(label, x, y, SKTextAlign.Left, font, textPaint);
        }
    }

    private void DrawPinch(SKCanvas canvas)
    {
        if (_hand.PinchPosition is not { } position)
            return;

        var point = Map(position);
        var radius = 4f + _hand.PinchStrength * 16f;
        var color = _hand.IsPinching ? new SKColor(255, 70, 70, 255) : new SKColor(255, 180, 60, 220);

        using var fill = new SKPaint { Color = color.WithAlpha(90), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var stroke = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };

        canvas.DrawCircle(point, radius, fill);
        canvas.DrawCircle(point, radius, stroke);
    }

    private void DrawPoint(SKCanvas canvas)
    {
        if (_hand.PointPosition is not { } position)
            return;

        var point = Map(position);
        const float arm = 10f;

        using var paint = new SKPaint
        {
            Color = new SKColor(255, 255, 0, 220),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
        };

        canvas.DrawLine(point.X - arm, point.Y, point.X + arm, point.Y, paint);
        canvas.DrawLine(point.X, point.Y - arm, point.X, point.Y + arm, paint);
        canvas.DrawCircle(point, arm * 0.5f, paint);
    }

    private void DrawPalmCentre(SKCanvas canvas)
    {
        if (_hand.PalmCenter is not { } position)
            return;

        var point = Map(position);

        using var fill = new SKPaint { Color = new SKColor(0, 120, 255, 200), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var ring = new SKPaint { Color = new SKColor(150, 210, 255, 255), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };

        canvas.DrawCircle(point, 4f, fill);
        canvas.DrawCircle(point, 9f, ring);
    }
}

/// <summary>
/// Draws the always-on interaction feedback in screen space: the hand cursor ring, a highlight around
/// the expandable node under the cursor, and transient rings for gesture / expand / collapse events.
/// </summary>
internal sealed class InteractionDrawOperation : ICustomDrawOperation
{
    private static readonly SKColor CursorPointing = new(120, 220, 255);
    private static readonly SKColor CursorPinching = new(255, 130, 90);
    private static readonly SKColor ExpandColour = new(90, 230, 130);
    private static readonly SKColor CollapseColour = new(255, 160, 70);
    private static readonly SKColor HoverExpandable = new(110, 205, 255);
    private static readonly SKColor HoverExpanded = new(110, 225, 150);

    private readonly Rect _bounds;
    private readonly InteractionOverlay _overlay;

    public InteractionDrawOperation(Rect bounds, InteractionOverlay overlay)
    {
        _bounds = bounds;
        _overlay = overlay;
    }

    public Rect Bounds => _bounds;

    public bool HitTest(Point p) => false;

    public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);

    public void Dispose()
    {
    }

    public void Render(ImmediateDrawingContext context)
    {
        if (context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature))
            is not ISkiaSharpApiLeaseFeature leaseFeature)
            return;

        using var lease = leaseFeature.Lease();
        var canvas = lease.SkCanvas;

        using (context.PushClip(_bounds))
        {
            DrawHover(canvas);
            DrawPulses(canvas);
            DrawCursor(canvas);
        }
    }

    private void DrawHover(SKCanvas canvas)
    {
        if (_overlay.HoverBounds is not { } bounds)
            return;

        var colour = _overlay.HoverExpanded ? HoverExpanded : HoverExpandable;
        var rect = new SKRect(
            (float)bounds.Min.X - 7f,
            (float)bounds.Min.Y - 7f,
            (float)bounds.Max.X + 7f,
            (float)bounds.Max.Y + 7f);

        using var glow = new SKPaint
        {
            Color = colour.WithAlpha(65),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 10f,
        };
        using var outline = new SKPaint
        {
            Color = colour.WithAlpha(235),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.5f,
        };

        canvas.DrawRoundRect(rect, 16f, 16f, glow);
        canvas.DrawRoundRect(rect, 16f, 16f, outline);

        // Badge in the top-right: "+" when collapsed (can expand), "-" when expanded.
        var centreX = rect.Right - 4f;
        var centreY = rect.Top + 4f;

        using var badge = new SKPaint { Color = colour, IsAntialias = true, Style = SKPaintStyle.Fill };
        using var mark = new SKPaint { Color = new SKColor(10, 20, 30), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.5f };

        canvas.DrawCircle(centreX, centreY, 11f, badge);
        canvas.DrawLine(centreX - 5f, centreY, centreX + 5f, centreY, mark);
        if (!_overlay.HoverExpanded)
            canvas.DrawLine(centreX, centreY - 5f, centreX, centreY + 5f, mark);
    }

    private void DrawPulses(SKCanvas canvas)
    {
        for (var i = 0; i < _overlay.Pulses.Count; i++)
        {
            var pulse = _overlay.Pulses[i];
            var progress = Math.Clamp(pulse.Progress, 0f, 1f);
            var radius = 14f + progress * 50f;
            var alpha = (byte)((1f - progress) * 210f);

            var colour = pulse.Kind switch
            {
                InteractionPulseKind.Expand => ExpandColour,
                InteractionPulseKind.Collapse => CollapseColour,
                _ => CursorPointing,
            };

            using var paint = new SKPaint
            {
                Color = colour.WithAlpha(alpha),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 3f,
            };

            canvas.DrawCircle(pulse.ScreenCentre.X, pulse.ScreenCentre.Y, radius, paint);
        }
    }

    private void DrawCursor(SKCanvas canvas)
    {
        if (_overlay.HandCursor is not { } cursor || _overlay.Pointer == PointerGesture.None)
            return;

        var pinching = _overlay.Pointer == PointerGesture.Pinching;
        var colour = pinching ? CursorPinching : CursorPointing;
        var radius = pinching ? 8f : 15f;

        using var ring = new SKPaint
        {
            Color = colour.WithAlpha(230),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = pinching ? 3f : 2.5f,
        };
        using var dot = new SKPaint { Color = colour.WithAlpha(230), IsAntialias = true, Style = SKPaintStyle.Fill };

        canvas.DrawCircle(cursor.X, cursor.Y, radius, ring);
        canvas.DrawCircle(cursor.X, cursor.Y, pinching ? 3.5f : 2f, dot);

        if (pinching)
        {
            // A short cross marks the contact point.
            using var cross = new SKPaint { Color = colour.WithAlpha(200), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };
            canvas.DrawLine(cursor.X - 12f, cursor.Y, cursor.X - 6f, cursor.Y, cross);
            canvas.DrawLine(cursor.X + 6f, cursor.Y, cursor.X + 12f, cursor.Y, cross);
            canvas.DrawLine(cursor.X, cursor.Y - 12f, cursor.X, cursor.Y - 6f, cross);
            canvas.DrawLine(cursor.X, cursor.Y + 6f, cursor.X, cursor.Y + 12f, cross);
        }
    }
}