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
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using NodeVision.Core;
using NodeVision.Rendering;
using NodeVision.Rendering.Skia;
using SkiaSharp;

namespace NodeVision.App.Controls;

public class SceneView : Control
{
    private const float ZoomStep = 0.1f;

    private readonly RenderBuilder _renderBuilder = new();
    private readonly SkiaRenderer _renderer = new();

    private SKImage? _latestWebcamImage;
    private readonly object _webcamImageLock = new();
    private int _webcamConsumerId = -1;
    private WebcamFrameRingBuffer? _ringBuffer;

    private bool _isPanning;
    private Point _lastPointerPosition;

    public event Action<Vector2>? PanRequested;
    public event Action<float, Vector2>? ZoomRequested;

    public Scene? Scene { get; set; }
    public Vector2 CameraTranslation { get; set; }
    public float CameraZoom { get; set; } = 1f;

    public Vector2 ViewportSize => new Vector2((float)Bounds.Width, (float)Bounds.Height);

    /// <summary>
    /// Registers this view as a consumer of the buffer
    /// </summary>
    public void SetWebcamSource(WebcamFrameRingBuffer ringBuffer)
    {
        _ringBuffer = ringBuffer;
        _webcamConsumerId = ringBuffer.RegisterConsumer();
    }

    /// <summary>
    /// Polls the buffer for a new frame, and updates the internal image.
    /// </summary>
    public void UpdateWebcamFrame()
    {
        if (_ringBuffer == null || _webcamConsumerId < 0)
            return;

        if (_ringBuffer.TryRead(_webcamConsumerId, out var frame))
        {
            lock (_webcamImageLock)
            {
                _latestWebcamImage?.Dispose();

                var info = new SKImageInfo(frame.Width, frame.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                using var bitmap = new SKBitmap(info);
                frame.BgraData.Span.CopyTo(bitmap.GetPixelSpan());
                _latestWebcamImage = SKImage.FromBitmap(bitmap);
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        _isPanning = true;
        _lastPointerPosition = e.GetPosition(this);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_isPanning)
            return;

        var position = e.GetPosition(this);
        var delta = position - _lastPointerPosition;
        _lastPointerPosition = position;

        PanRequested?.Invoke(new Vector2((float)delta.X, (float)delta.Y));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!_isPanning)
            return;

        _isPanning = false;
        e.Pointer.Capture(null);
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
            var commands = _renderBuilder.BuildScene(Scene);

            var renderContext = new RenderContext
            {
                CameraTranslation = CameraTranslation,
                CameraZoom = CameraZoom,
                RenderTargetSize = new Vector2((float)Bounds.Width, (float)Bounds.Height)
            };

            context.Custom(
                new SceneDrawOperation(
                    new Rect(0, 0, (float)Bounds.Width, (float)Bounds.Height),
                    _renderer,
                    commands,
                    renderContext));
        }
    }

    private void DrawWebcamBackground(DrawingContext context)
    {
        SKImage? imageToDraw;

        lock (_webcamImageLock)
        {
            if (_latestWebcamImage == null)
                return;

            imageToDraw = _latestWebcamImage;
        }

        try
        {
            var rect = new Rect(0, 0, Bounds.Width, Bounds.Height);

            context.Custom(new WebcamDrawOperation(rect, imageToDraw));
        }
        catch
        {
            // ignore
        }
    }
}

internal sealed class WebcamDrawOperation : ICustomDrawOperation
{
    private readonly SKImage _image;
    private readonly Rect _bounds;

    public WebcamDrawOperation(Rect bounds, SKImage image)
    {
        _bounds = bounds;
        _image = image;
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

        using (context.PushClip(_bounds))
        {
            var dstRect = new SKRect(
                (float)_bounds.X,
                (float)_bounds.Y,
                (float)_bounds.Right,
                (float)_bounds.Bottom);

            canvas.DrawImage(_image, dstRect);
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