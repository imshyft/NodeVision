using NodeVision.Core;
using NodeVision.Visualisation;

public class VisualizationEngine
{
    public Scene Scene { get; }

    public VisualizationEngine()
    {
        Scene = TestSceneFactory.CreateScene();
    }

    public void Update(float deltaTime)
    {
        // modify scene here.
    }
}