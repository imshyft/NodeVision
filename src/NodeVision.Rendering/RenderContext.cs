using NodeVision.Core;

namespace NodeVision.Rendering;

public class RenderContext
{
    public Vector2 CameraTranslation { get; init; }
    public float CameraZoom { get; init; } = 1f;
    public Vector2 RenderTargetSize { get; init; }

    public Vector2 ScreenToWorld(Vector2 screenPoint)
    {
        var centred = screenPoint - (RenderTargetSize * 0.5f);
        return new Vector2(
            centred.X / CameraZoom + CameraTranslation.X,
            centred.Y / CameraZoom + CameraTranslation.Y
        );
    }

    public Vector2 WorldToScreen(Vector2 worldPoint)
    {
        var offset = worldPoint - CameraTranslation;
        return new Vector2(
            offset.X * CameraZoom + RenderTargetSize.X * 0.5f,
            offset.Y * CameraZoom + RenderTargetSize.Y * 0.5f
        );
    }
}
