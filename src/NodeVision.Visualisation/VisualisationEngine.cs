using System.Diagnostics;
using NodeVision.Core;
using NodeVision.Visualisation;

public class VisualizationEngine
{
    private float _time;
    public Scene Scene { get; }

    public Vector2 CameraPosition { get; set; }
    public float CameraZoom { get; set; } = 1f;

    public VisualizationEngine()
    {
        Scene = TestSceneFactory.CreateScene();
    }

    public void Update(float deltaTime)
    {
        _time += deltaTime;
        
        float t = _time;
        CameraPosition = new Vector2(MathF.Sin(t) * 100, MathF.Cos(t) * 100);
        CameraZoom = 1f + MathF.Sin(t * 0.5f) * 0.3f;
    }
}