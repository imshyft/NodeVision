using System.Collections.Generic;
using NodeVision.Core;
using NodeVision.Rendering.DrawCommandBuilders;

namespace NodeVision.Rendering;

public class RenderBuilder
{
    private readonly Dictionary<Type, DrawCommandBuilder> _builders = new()
    {
        { typeof(RectangleObject), new RectangleDrawCommandBuilder() }
    };
    
    // on render
    // loop through all
    // send commands to render backend
    public List<DrawCommand> BuildScene(Scene scene)
    {
        var commands = new List<DrawCommand>();
        
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
