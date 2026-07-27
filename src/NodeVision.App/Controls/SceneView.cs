using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
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
    private readonly RenderBuilder _renderBuilder = new();
    private readonly SkiaRenderer _renderer = new();

    public Scene? Scene { get; set; }
    public Vector2 CameraTranslation { get; set; }
    public float CameraZoom { get; set; } = 1f;

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (Scene == null)
            return;

        var commands = _renderBuilder.BuildScene(Scene);

        var renderContext = new RenderContext
        {
            CameraTranslation = CameraTranslation,
            CameraZoom = CameraZoom,
            RenderTargetSize = new Vector2((float)Bounds.Width, (float)Bounds.Height)
        };

        context.Custom(
            new SceneDrawOperation(
                Bounds,
                _renderer,
                commands,
                renderContext));
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

        _renderer.BeginRender(lease.SkCanvas);
        _renderer.Render(_commands, _renderContext);
        _renderer.EndRender();
    }
}

