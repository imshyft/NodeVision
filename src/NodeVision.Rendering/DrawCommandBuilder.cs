using NodeVision.Core;

namespace NodeVision.Rendering;

public abstract class DrawCommandBuilder
{
    public abstract void BuildCommand (SceneObject sceneObject, List<RenderCommand> commands);
}