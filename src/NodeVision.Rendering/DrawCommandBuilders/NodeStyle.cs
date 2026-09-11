using NodeVision.Core;

namespace NodeVision.Rendering.DrawCommandBuilders;

public static class NodeStyle
{
    public static Colour Background => new Colour(0.086f, 0.098f, 0.122f);
    public static Colour Border => new Colour(0.169f, 0.192f, 0.235f);
    public static Colour Accent => new Colour(0.208f, 0.435f, 0.702f);
    public static Colour HeaderText => new Colour(0.902f, 0.918f, 0.949f);
    public static Colour BodyText => new Colour(0.588f, 0.639f, 0.714f);

    public const float CornerRadius = 10f;
    public const float Padding = 16f;
    public const float AccentInset = 12f;
    public const float AccentWidth = 4f;
    public const float TextInset = 26f;
    public const float HeaderSize = 17f;
    public const float BodySize = 13f;
    public const float HeaderGap = 10f;
    public const float LineSpacing = 1.4f;
}