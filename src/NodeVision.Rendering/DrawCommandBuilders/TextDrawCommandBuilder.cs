using NodeVision.Core;
using NodeVision.Rendering.ObjectRenderInfo;

namespace NodeVision.Rendering.DrawCommandBuilders;

public class TextDrawCommandBuilder : DrawCommandBuilder
{
    public override void BuildCommand(SceneObject sceneObject, List<RenderCommand> commands)
    {
        var textObject = (TextObject)sceneObject;
        commands.Add(new TextRenderCommand()
        {
            Text = textObject.Text,
            Colour = textObject.Colour,
            Position = textObject.Transform.Position,
            Size = textObject.Transform.Scale
        });
    }
}
