using NodeVision.Core;

namespace NodeVision.Rendering;

/// <summary>
/// An axis-aligned rectangle in canvas space. When set on a <see cref="RenderCommand"/>, the backend
/// clips that command to these bounds, so content cannot spill outside its owning object.
/// </summary>
public readonly record struct ClipRect(Vector2 Position, Vector2 Size);
