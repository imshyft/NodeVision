using NodeVision.Core;

namespace NodeVision.Visualisation
{
    public static class TestSceneFactory
    {
        public static Scene CreateScene()
        {
            var scene = new Scene();

            // Node 1 is the root; 2 and 4 are its children, and 3 is a grandchild of 2, so the reveal
            // has to recurse.
            scene.AddObject(new Node
            {
                Id = 1,
                Position = new Vector2(150, -150),
                Size = new Vector2(300, 160),
                NodeName = "NodeVision",
                Content = new TextContent
                {
                    Value = "Gesture driven spatial presentation canvas. Navigate ideas in space instead of stepping through slides."
                }
            });

            scene.AddObject(new Node
            {
                Id = 2,
                Position = new Vector2(-350, -150),
                Size = new Vector2(300, 160),
                NodeName = "Semantic zoom",
                Content = new TextContent
                {
                    Value = "Overview shows structure and relationships. Detail appears only as the presenter zooms in."
                }
            });

            scene.AddObject(new Node
            {
                Id = 3,
                Position = new Vector2(-350, 120),
                Size = new Vector2(260, 140),
                NodeName = "Detail on approach",
                Content = new TextContent
                {
                    Value = "A child stays hidden until its parent is expanded, then it emerges from the parent card."
                }
            });

            scene.AddObject(new Node
            {
                Id = 4,
                Position = new Vector2(150, 120),
                Size = new Vector2(300, 160),
                NodeName = "Gesture navigation",
                Content = new TextContent
                {
                    Value = "Point and pinch to move through the canvas; expanding a node is the first gesture driven reveal."
                }
            });

            scene.AddObject(new Connection { Id = 1, ParentNodeId = 1, ChildNodeId = 2 });
            scene.AddObject(new Connection { Id = 2, ParentNodeId = 1, ChildNodeId = 4 });
            scene.AddObject(new Connection { Id = 3, ParentNodeId = 2, ChildNodeId = 3 });

            scene.AddObject(new CircleObject
            {
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
