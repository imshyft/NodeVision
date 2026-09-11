using NodeVision.Core;

namespace NodeVision.Visualisation
{
    public static class TestSceneFactory
    {
        public static Scene CreateScene()
        {
            var scene = new Scene();

            scene.AddObject(new RectangleObject
            {
                Id = "background",
                Transform = new Transform
                {
                    Position = new Vector2(-400, -300),
                    Scale = new Vector2(100, 100)
                },
                Size = new Vector2(800, 600),
                Colour = new Colour(0.30f, 0.30f, 0.30f)
            });

            scene.AddObject(new Node
            {
                Id = "node.main",
                Transform = new Transform
                {
                    Position = new Vector2(0, 0),
                    Scale = Vector2.One
                },
                Size = new Vector2(340, 180),
                Header = "NodeVision",
                Body = "Gesture driven spatial presentation canvas. Navigate ideas in space instead of stepping through slides."
            });

            scene.AddObject(new Node
            {
                Id = "node.topicA",
                Transform = new Transform
                {
                    Position = new Vector2(420, 140),
                    Scale = Vector2.One
                },
                Size = new Vector2(320, 160),
                Header = "Semantic zoom",
                Body = "Overview shows structure and relationships. Detail appears only as the presenter zooms in."
            });

            scene.AddObject(new CircleObject
            {
                Id = "node.decorative-circle",
                Transform = new Transform
                {
                    Position = new Vector2(550, 200),
                    Scale = new Vector2(100, 100)
                },
                Radius = 60,
                Colour = Colour.Green
            });

            scene.AddObject(new ImageObject
            {
                Transform = new Transform
                {
                    Position = new Vector2(200, 200),
                    Scale = Vector2.One
                },
                FilePath = "Assets/test-image.png",
                Size = new Vector2(128, 128)
            });

            return scene;
        }
    }
}