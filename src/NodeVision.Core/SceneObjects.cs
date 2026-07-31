using System.Collections.Generic;
using System.Drawing;
using System.Numerics;

namespace NodeVision.Core
{
    public class Scene
    {
        public List<SceneObject> Objects { get; } = new List<SceneObject>();

        public void AddObject(SceneObject obj)
        {
            Objects.Add(obj);
        }

        public void RemoveObject(SceneObject obj)
        {
            Objects.Remove(obj);
        }

        public void ClearObjects()
        {
            Objects.Clear();
        }
    }

    public abstract class SceneObject
    {
        public Transform Transform { get; set; } = Transform.Identity;
    }

    public class RectangleObject : SceneObject
    {
        public Vector2 Size { get; set; }
        public Colour Colour { get; set; }
    }

    public class TextObject : SceneObject
    {
        public string Text { get; set; }
        public Colour Colour { get; set; }
    }

    public class CircleObject : SceneObject
    {
        public float Radius { get; set; }
        public Colour Colour { get; set; }
    }
}