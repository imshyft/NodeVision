using NodeVision.Core;

namespace NodeVision.Visualisation
{
    public static class TestSceneFactory
    {
        public static Scene CreateScene()
        {
            var scene = new Scene();

            // Background
            scene.AddObject(new RectangleObject
            {
                Transform = new Transform
                {
                    Position = new Vector2(-400, -300),
                    Scale = new Vector2(100, 100)
                },
                Size = new Vector2(800, 600),
                Colour = new Colour(0.30f, 0.30f, 0.30f)
            });

            // Main node
            scene.AddObject(new RectangleObject
            {
                Transform = new Transform
                {
                    Position = new Vector2(0, 0),
                    Scale = new Vector2(100, 100)
                },
                Size = new Vector2(300, 150),
                Colour = new Colour(0.70f, 0.120f, 0.220f)
            });

            // Title
            scene.AddObject(new TextObject
            {
                Transform = new Transform
                {
                    Position = new Vector2(120, 140),
                    Scale = new Vector2(100, 100)
                },
                Text = "NodeVision",
                Colour = Colour.Green
            });

            // Decorative circle
            scene.AddObject(new CircleObject
            {
                Transform = new Transform
                {
                    Position = new Vector2(550, 200),
                    Scale = new Vector2(100, 100)
                },
                Radius = 60,
                Colour = Colour.Green
            });

            // Second node
            scene.AddObject(new RectangleObject
            {
                Transform = new Transform
                {
                    Position = new Vector2(500, 350),
                    Scale = new Vector2(1000, 100)
                },
                Size = new Vector2(250, 100),
                Colour = Colour.Blue
            });

            scene.AddObject(new TextObject
            {
                Transform = new Transform
                {
                    Position = new Vector2(520, 390),
                    Scale = new Vector2(100, 100)
                },
                Text = "Topic A",
                Colour = Colour.Red
            });

            return scene;
        }
    }
}