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

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (Scene == null)
            return;

        var commands = _renderBuilder.BuildScene(Scene);

        context.Custom(
            new SceneDrawOperation(
                Bounds,
                _renderer,
                commands));
    }
}

internal sealed class SceneDrawOperation : ICustomDrawOperation
{
    private readonly SkiaRenderer _renderer;
    private readonly IReadOnlyList<RenderCommand> _commands;

    public SceneDrawOperation(
        Rect bounds,
        SkiaRenderer renderer,
        IReadOnlyList<RenderCommand> commands)
    {
        Bounds = bounds;
        _renderer = renderer;
        _commands = commands;
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
        _renderer.Render(_commands);
        _renderer.EndRender();
    }
}

