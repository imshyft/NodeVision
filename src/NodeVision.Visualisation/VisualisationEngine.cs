using System.Diagnostics;
using NodeVision.Core;
using NodeVision.Visualisation;
using NodeVision.Visualisation.Persistence;

public class VisualizationEngine
{
    private float _time;
    public Scene Scene { get; }

    public Vector2 CameraPosition { get; set; }
    public float CameraZoom { get; set; } = 1f;

    public NodeLayout CurrentLayout { get; } = new();

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

        foreach (var obj in Scene.Objects)
        {
            if (string.IsNullOrEmpty(obj.Id))
            {
                continue;
            }

            if (!CurrentLayout.Positions.TryGetValue(obj.Id, out var position))
            {
                position = new NodePosition();
                CurrentLayout.Positions[obj.Id] = position;
            }

            position.X = obj.Transform.Position.X;
            position.Y = obj.Transform.Position.Y;
        }
    }
}