using System.Collections.Generic;

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
        public string Id { get; set; } = string.Empty;
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

    public class ImageObject : SceneObject
    {
        public string FilePath { get; set; } = "";
        public Vector2 Size { get; set; }
    }

    public class Node : SceneObject
    {
        public string Header { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public Vector2 Size { get; set; }

        // Presentation value written by the visualisation: 0 = hidden, 1 = fully revealed.
        public float Reveal { get; set; } = 1f;
    }

    public class Connection : SceneObject
    {
        public string ParentId { get; set; } = string.Empty;
        public string ChildId { get; set; } = string.Empty;
    }
}