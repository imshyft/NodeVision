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
    private Scene? _scene;
    private RenderBuilder _graphicsBuilder;
    private SkiaRenderer _renderer;

    public SceneView()
    {
        _renderer = new SkiaRenderer();
        _graphicsBuilder = new RenderBuilder();
    }

    public void SetScene(Scene scene)
    {
        _scene = scene;
    }
    

    // visualisation.attatch(scene)
    public override void Render(DrawingContext context)
    {
        if (_scene != null)
        {
            var renderCommands = _graphicsBuilder.BuildScene(_scene);
            context.Custom(new SceneDrawOperation(new Rect(0, 0, Bounds.Width, Bounds.Height), _renderer, renderCommands));
        }
        Dispatcher.UIThread.InvokeAsync(InvalidateVisual, DispatcherPriority.Background);

        // Visualisation.update(deltaTime)
        // var renderCommands = renderBuilder.BuildCommands(scene)
        // renderer.render(renderCommands)
    }
}

public class TestSceneView : Control
{
    private readonly GlyphRun _noSkia;
    public TestSceneView()
    {
        ClipToBounds = true;
        var text = "Current rendering API is not Skia";
        var glyphs = text.Select(ch => Typeface.Default.GlyphTypeface.GetGlyph(ch)).ToArray();
        _noSkia = new GlyphRun(Typeface.Default.GlyphTypeface, 12, text.AsMemory(), glyphs);
    }

    public override void Render(DrawingContext context)
    {
        context.Custom(new CustomDrawOp(new Rect(0, 0, Bounds.Width, Bounds.Height), _noSkia));
        Dispatcher.UIThread.InvokeAsync(InvalidateVisual, DispatcherPriority.Background);
    }
}

class SceneDrawOperation : ICustomDrawOperation
{
    private List<DrawCommand> _drawCommands;
    private SkiaRenderer _renderer;

    private readonly IImmutableGlyphRunReference _noSkia;
    public SceneDrawOperation(Rect bounds, SkiaRenderer renderer, List<DrawCommand> drawCommands)
    {
        Bounds = bounds;
        _renderer = renderer;
        _drawCommands = drawCommands;
        
        var text = "Current rendering API is not Skia";
        var glyphs = text.Select(ch => Typeface.Default.GlyphTypeface.GetGlyph(ch)).ToArray();
        _noSkia = new GlyphRun(Typeface.Default.GlyphTypeface, 12, text.AsMemory(), glyphs).TryCreateImmutableGlyphRunReference();;
    }
    public void Dispose() {}

    public Rect Bounds { get; }
    public bool HitTest(Point p) => false;
    public bool Equals(ICustomDrawOperation other) => false;

    public void Render(ImmediateDrawingContext context)
    {
        var leaseFeature = context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) as ISkiaSharpApiLeaseFeature;
        if (leaseFeature == null)
            context.DrawGlyphRun(Brushes.Black, _noSkia);
        else
        {
            using var lease = leaseFeature.Lease();
            var canvas = lease.SkCanvas;
            canvas.Save();
            
            _renderer.BeginRender(canvas);
            _renderer.Render(_drawCommands);
            _renderer.EndRender();
            canvas.Restore();
        }
    }
}

class CustomDrawOp : ICustomDrawOperation
        {
            private readonly IImmutableGlyphRunReference _noSkia;

            public CustomDrawOp(Rect bounds, GlyphRun noSkia)
            {
                _noSkia = noSkia.TryCreateImmutableGlyphRunReference();
                Bounds = bounds;
            }
            
            public void Dispose()
            {
                // No-op
            }

            public Rect Bounds { get; }
            public bool HitTest(Point p) => false;
            public bool Equals(ICustomDrawOperation other) => false;
            static Stopwatch St = Stopwatch.StartNew();
            public void Render(ImmediateDrawingContext context)
            {
                var leaseFeature = context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) as ISkiaSharpApiLeaseFeature;
                if (leaseFeature == null)
                    context.DrawGlyphRun(Brushes.Black, _noSkia);
                else
                {
                    using var lease = leaseFeature.Lease();
                    var canvas = lease.SkCanvas;
                    canvas.Save();
                    // create the first shader
                    var colors = new SKColor[] {
                        new SKColor(0, 255, 255),
                        new SKColor(255, 0, 255),
                        new SKColor(255, 255, 0),
                        new SKColor(0, 255, 255)
                    };

                    var sx = Animate(100, 2, 10);
                    var sy = Animate(1000, 5, 15);
                    var lightPosition = new SKPoint(
                        (float)(Bounds.Width / 2 + Math.Cos(St.Elapsed.TotalSeconds) * Bounds.Width / 4),
                        (float)(Bounds.Height / 2 + Math.Sin(St.Elapsed.TotalSeconds) * Bounds.Height / 4));
                    using (var sweep =
                        SKShader.CreateSweepGradient(new SKPoint((int)Bounds.Width / 2, (int)Bounds.Height / 2), colors,
                            null)) 
                    using(var turbulence = SKShader.CreatePerlinNoiseFractalNoise(0.05f, 0.05f, 4, 0))
                    using(var shader = SKShader.CreateCompose(sweep, turbulence, SKBlendMode.SrcATop))
                    using(var blur = SKImageFilter.CreateBlur(Animate(100, 2, 10), Animate(100, 5, 15)))
                    using (var paint = new SKPaint
                    {
                        Shader = shader,
                        ImageFilter = blur
                    })
                        canvas.DrawPaint(paint);
                    
                    using (var pseudoLight = SKShader.CreateRadialGradient(
                        lightPosition,
                        (float) (Bounds.Width/3),
                        new [] { 
                            new SKColor(255, 200, 200, 100), 
                            SKColors.Transparent,
                            new SKColor(40,40,40, 220), 
                            new SKColor(20,20,20, (byte)Animate(100, 200,220)) },
                        new float[] { 0.3f, 0.3f, 0.8f, 1 },
                        SKShaderTileMode.Clamp))
                    using (var paint = new SKPaint
                    {
                        Shader = pseudoLight
                    })
                        canvas.DrawPaint(paint);
                    canvas.Restore();
                }
            }    
            static int Animate(int d, int from, int to)
            {
                var ms = (int)(St.ElapsedMilliseconds / d);
                var diff = to - from;
                var range = diff * 2;
                var v = ms % range;
                if (v > diff)
                    v = range - v;
                var rv = v + from;
                if (rv < from || rv > to)
                    throw new Exception("WTF");
                return rv;
            }
        }
