using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace NodeVision.App.Controls;

public enum GestureHintKind
{
    Pan,
    Zoom,
    Expand,
    Collapse,
}

/// <summary>
/// A small hand-skeleton diagram for the gesture tooltips. Poses are built procedurally (open hand,
/// fist, and the two pinch poses), so each tooltip can show what the gesture looks like.
/// </summary>
public sealed class GestureHint : Control
{
    public static readonly StyledProperty<GestureHintKind> KindProperty =
        AvaloniaProperty.Register<GestureHint, GestureHintKind>(nameof(Kind));

    public GestureHintKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    static GestureHint()
    {
        AffectsRender<GestureHint>(KindProperty);
    }

    private static readonly (int A, int B)[] Bones =
    {
        (0, 1), (1, 2), (2, 3), (3, 4),
        (0, 5), (5, 6), (6, 7), (7, 8),
        (5, 9), (9, 10), (10, 11), (11, 12),
        (9, 13), (13, 14), (14, 15), (15, 16),
        (13, 17), (17, 18), (18, 19), (19, 20),
        (0, 17),
    };

    private static readonly int[] Fingertips = { 4, 8, 12, 16, 20 };

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var scale = Math.Min(Bounds.Width, Bounds.Height) / 110.0;
        if (scale <= 0)
            return;

        var offsetX = (Bounds.Width - 100 * scale) / 2;
        var offsetY = (Bounds.Height - 100 * scale) / 2;

        Point Map(Point point) => new(offsetX + point.X * scale, offsetY + point.Y * scale);

        var points = BuildPose(Kind);

        var bonePen = new Pen(new SolidColorBrush(Color.Parse("#BBD8F5")), Math.Max(1.5, 2.2 * scale))
        {
            LineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        var tipBrush = new SolidColorBrush(Color.Parse("#E8F4FF"));
        var palmBrush = new SolidColorBrush(Color.Parse("#14FFFFFF"));

        var palm = new StreamGeometry();
        using (var geometry = palm.Open())
        {
            geometry.BeginFigure(Map(points[0]), true);
            geometry.LineTo(Map(points[5]));
            geometry.LineTo(Map(points[9]));
            geometry.LineTo(Map(points[13]));
            geometry.LineTo(Map(points[17]));
            geometry.EndFigure(true);
        }

        context.DrawGeometry(palmBrush, null, palm);

        foreach (var (a, b) in Bones)
            context.DrawLine(bonePen, Map(points[a]), Map(points[b]));

        foreach (var index in Fingertips)
            context.DrawEllipse(tipBrush, null, Map(points[index]), 2.6 * scale, 2.6 * scale);
    }

    private static Point[] BuildPose(GestureHintKind kind)
    {
        var points = new Point[21];
        points[0] = new Point(50, 94);

        var indexMcp = new Point(36, 62);
        var middleMcp = new Point(46, 58);
        var ringMcp = new Point(55, 60);
        var pinkyMcp = new Point(63, 65);

        float indexCurl;
        float middleCurl;
        float ringCurl;
        float pinkyCurl;

        switch (kind)
        {
            case GestureHintKind.Expand:
                indexCurl = middleCurl = ringCurl = pinkyCurl = 0.05f;
                break;

            case GestureHintKind.Collapse:
                indexCurl = middleCurl = ringCurl = pinkyCurl = 1f;
                break;

            case GestureHintKind.Pan:
                indexCurl = 0.02f;
                middleCurl = ringCurl = pinkyCurl = 1f;
                break;

            default:
                indexCurl = 0.55f;
                middleCurl = ringCurl = pinkyCurl = 1f;
                break;
        }

        BuildFinger(points, 5, indexMcp, -1.95f, 10f, 8f, 6f, indexCurl);
        BuildFinger(points, 9, middleMcp, -1.75f, 12f, 9f, 7f, middleCurl);
        BuildFinger(points, 13, ringMcp, -1.55f, 11f, 8f, 6f, ringCurl);
        BuildFinger(points, 17, pinkyMcp, -1.35f, 9f, 7f, 5f, pinkyCurl);

        var thumbBase = new Point(40, 82);
        var thumbTarget = kind switch
        {
            GestureHintKind.Pan => points[12],
            GestureHintKind.Zoom => points[8],
            GestureHintKind.Expand => new Point(22, 56),
            _ => new Point(52, 78),
        };

        BuildThumb(points, thumbBase, thumbTarget);

        return points;
    }

    private static void BuildFinger(
        Point[] points,
        int start,
        Point mcp,
        float baseAngle,
        float first,
        float second,
        float third,
        float curl)
    {
        points[start] = mcp;

        var angle = baseAngle;
        var current = mcp;

        for (var segment = 0; segment < 3; segment++)
        {
            var bend = segment switch
            {
                0 => curl * 0.2f,
                1 => curl * 1.45f,
                _ => curl * 1.2f,
            };

            angle += bend;

            var length = segment switch
            {
                0 => first,
                1 => second,
                _ => third,
            };

            current = new Point(
                current.X + Math.Cos(angle) * length,
                current.Y + Math.Sin(angle) * length);

            points[start + segment + 1] = current;
        }
    }

    private static void BuildThumb(Point[] points, Point basePoint, Point target)
    {
        var dx = target.X - basePoint.X;
        var dy = target.Y - basePoint.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);

        if (length < 1e-3)
        {
            points[1] = points[2] = points[3] = points[4] = target;
            return;
        }

        var bow = Math.Min(10.0, length * 0.18);
        var control = new Point(
            (basePoint.X + target.X) / 2 - dy / length * bow,
            (basePoint.Y + target.Y) / 2 + dx / length * bow);

        points[1] = Quadratic(basePoint, control, target, 0.30);
        points[2] = Quadratic(basePoint, control, target, 0.58);
        points[3] = Quadratic(basePoint, control, target, 0.82);
        points[4] = target;
    }

    private static Point Quadratic(Point p0, Point p1, Point p2, double t)
    {
        var inverse = 1 - t;
        return new Point(
            p0.X * inverse * inverse + 2 * p1.X * inverse * t + p2.X * t * t,
            p0.Y * inverse * inverse + 2 * p1.Y * inverse * t + p2.Y * t * t);
    }
}
