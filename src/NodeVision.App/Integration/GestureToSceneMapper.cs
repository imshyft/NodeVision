using System;
using System.Collections.Generic;
using NodeVision.Inference;
using NodeVision.Visualisation;

namespace NodeVision.App.Integration;

/// <summary>
/// Turns a GestureEvent into the SceneEvents the visualisation loop executes.
/// </summary>
public interface IGestureToSceneMapper
{
    IReadOnlyList<SceneEvent> Map(GestureEvent gesture);
}

/// <summary>
/// Placeholder mapper: forwards nothing, so the transport (queue -> drain -> engine.Update) can run
/// end to end before any mapping exists.
/// </summary>
public sealed class NullGestureToSceneMapper : IGestureToSceneMapper
{
    public IReadOnlyList<SceneEvent> Map(GestureEvent gesture) => Array.Empty<SceneEvent>();
}