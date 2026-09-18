using System.Collections.Generic;
using NodeVision.Core;
using NodeVision.Rendering.DrawCommandBuilders;

namespace NodeVision.Rendering;

public class RenderBuilder
{
    private readonly Dictionary<Type, DrawCommandBuilder> _builders = new()
    {
        { typeof(RectangleObject), new RectangleDrawCommandBuilder() },
        { typeof(TextObject), new TextDrawCommandBuilder() },
        { typeof(ImageObject), new ImageDrawCommandBuilder() },
        { typeof(Node), new NodeDrawCommandBuilder() }
    };

    public List<RenderCommand> BuildScene(Scene scene)
    {
        var commands = new List<RenderCommand>();

        foreach (var sceneObject in scene.Objects)
        {
            if (_builders.TryGetValue(sceneObject.GetType(), out var builder))
            {
                builder.BuildCommand(sceneObject, commands);
            }
        }

        return commands;
    }
}