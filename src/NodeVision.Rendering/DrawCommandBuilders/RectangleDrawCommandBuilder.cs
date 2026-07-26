using NodeVision.Core;
using NodeVision.Rendering.ObjectRenderInfo;

namespace NodeVision.Rendering.DrawCommandBuilders;

public class RectangleDrawCommandBuilder : DrawCommandBuilder
{
    public override void BuildCommand(SceneObject sceneObject, List<DrawCommand> commands)
    {
        var rectangleObject = (RectangleObject)sceneObject;
        commands.Add(new DrawRectangleCommand()
        {
            Colour = rectangleObject.Colour,
            Position = rectangleObject.Transform.Position,
            Size = rectangleObject.Transform.Scale
        });
    }
}