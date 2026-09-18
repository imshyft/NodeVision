using NodeVision.Core;

namespace NodeVision.Visualisation
{
    public static class TestSceneFactory
    {
        public static Scene CreateScene()
        {
            var scene = new Scene();

            // node.main is the root; node.topicA and node.topicB are its children, and
            // node.topicA.detail is a grandchild, so the reveal has to recurse.
            scene.AddObject(new Node
            {
                Id = "node.main",
                Transform = new Transform
                {
                    Position = new Vector2(150, -150),
                    Scale = Vector2.One
                },
                Size = new Vector2(300, 160),
                Header = "NodeVision",
                Body = "Gesture driven spatial presentation canvas. Navigate ideas in space instead of stepping through slides."
            });

            scene.AddObject(new Node
            {
                Id = "node.topicA",
                Transform = new Transform
                {
                    Position = new Vector2(-350, -150),
                    Scale = Vector2.One
                },
                Size = new Vector2(300, 160),
                Header = "Semantic zoom",
                Body = "Overview shows structure and relationships. Detail appears only as the presenter zooms in."
            });

            scene.AddObject(new Node
            {
                Id = "node.topicA.detail",
                Transform = new Transform
                {
                    Position = new Vector2(-350, 120),
                    Scale = Vector2.One
                },
                Size = new Vector2(260, 140),
                Header = "Detail on approach",
                Body = "A child stays hidden until its parent is expanded, then it emerges from the parent card."
            });

            scene.AddObject(new Node
            {
                Id = "node.topicB",
                Transform = new Transform
                {
                    Position = new Vector2(150, 120),
                    Scale = Vector2.One
                },
                Size = new Vector2(300, 160),
                Header = "Gesture navigation",
                Body = "Point and pinch to move through the canvas; expanding a node is the first gesture driven reveal."
            });

            scene.AddObject(new Connection { Id = "conn.main.topicA", ParentId = "node.main", ChildId = "node.topicA" });
            scene.AddObject(new Connection { Id = "conn.main.topicB", ParentId = "node.main", ChildId = "node.topicB" });
            scene.AddObject(new Connection { Id = "conn.topicA.detail", ParentId = "node.topicA", ChildId = "node.topicA.detail" });

            scene.AddObject(new CircleObject
            {
                Id = "node.decorative-circle",
                Transform = new Transform
                {
                    Position = new Vector2(430, 300),
                    Scale = new Vector2(100, 100)
                },
                Radius = 40,
                Colour = Colour.Green
            });

            scene.AddObject(new ImageObject
            {
                Id = "node.decorative-image",
                Transform = new Transform
                {
                    Position = new Vector2(-470, 250),
                    Scale = Vector2.One
                },
                FilePath = "Assets/test-image.png",
                Size = new Vector2(128, 128)
            });

            return scene;
        }
    }
}