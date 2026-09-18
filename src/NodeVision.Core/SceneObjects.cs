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
        public Transform Transform { get; set; } = Transform.Identity;
    }

    public class RectangleObject : SceneObject
    {
        public Vector2 Size { get; set; }
        public Colour Colour { get; set; }
    }

    public class TextObject : SceneObject
    {
        public string Text { get; set; } = string.Empty;
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
        public int Id { get; set; }
        public string NodeName { get; set; } = string.Empty;
        public Vector2 Position { get; set; }
        public NodeContent Content { get; set; } = new TextContent();

        // Card size and presentation reveal, written by the visualisation and read by the renderer.
        public Vector2 Size { get; set; }
        public float Reveal { get; set; } = 1f;
    }

    public abstract class NodeContent
    {
    }

    public class TextContent : NodeContent
    {
        public string Value { get; set; } = string.Empty;
    }

    public class ImageContent : NodeContent
    {
        public string FilePath { get; set; } = string.Empty;
    }

    public class Connection : SceneObject
    {
        public int Id { get; set; }
        public int ParentNodeId { get; set; }
        public int ChildNodeId { get; set; }

        // Resolved after loading.
        public Node? ParentNode { get; set; }
        public Node? ChildNode { get; set; }
    }
}
