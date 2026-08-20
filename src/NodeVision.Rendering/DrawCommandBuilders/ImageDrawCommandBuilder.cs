using NodeVision.Core;
using NodeVision.Rendering.ObjectRenderInfo;

namespace NodeVision.Rendering.DrawCommandBuilders;

public class ImageDrawCommandBuilder : DrawCommandBuilder
{
    public override void BuildCommand(SceneObject sceneObject, List<RenderCommand> commands)
    {
        var imageObject = (ImageObject)sceneObject;
        commands.Add(new ImageRenderCommand()
        {
            FilePath = imageObject.FilePath,
            Position = imageObject.Transform.Position,
            Size = imageObject.Size
        });
    }
}
