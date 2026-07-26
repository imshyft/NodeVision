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
                Colour = new Colour(30, 30, 30)
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
                Colour = new Colour(70, 120, 220)
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
                Colour = Colour.White
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
                Colour = new Colour(220, 80, 80)
            });

            // Second node
            scene.AddObject(new RectangleObject
            {
                Transform = new Transform
                {
                    Position = new Vector2(500, 350),
                    Scale = new Vector2(100, 100)
                },
                Size = new Vector2(250, 100),
                Colour = new Colour(80, 180, 120)
            });

            scene.AddObject(new TextObject
            {
                Transform = new Transform
                {
                    Position = new Vector2(520, 390),
                    Scale = new Vector2(100, 100)
                },
                Text = "Topic A",
                Colour = Colour.White
            });

            return scene;
        }
    }
}