using System.Diagnostics;
using NodeVision.Core;
using NodeVision.Visualisation.Persistence;

namespace NodeVision.Visualisation
{
    public class VisualizationEngine
    {
        private float _time;
        public Scene Scene { get; }

        public Vector2 CameraPosition { get; set; }
        public float CameraZoom { get; set; } = 1f;

        public NodeLayout CurrentLayout { get; } = new();

        public VisualizationEngine()
        {
            Scene = TestSceneFactory.CreateScene();
        }

        public void Update(float deltaTime)
        {
            _time += deltaTime;

            foreach (var obj in Scene.Objects)
            {
                if (string.IsNullOrEmpty(obj.Id))
                {
                    continue;
                }

                if (!CurrentLayout.Positions.TryGetValue(obj.Id, out var position))
                {
                    position = new NodePosition();
                    CurrentLayout.Positions[obj.Id] = position;
                }

                position.X = obj.Transform.Position.X;
                position.Y = obj.Transform.Position.Y;
            }
        }

        /// <summary>
        /// Pans the camera position by a screen-space delta (in pixels).
        /// Adjusts for current camera zoom level.
        /// </summary>
        /// <param name="screenDelta">Displacement vector in screen pixels.</param>
        public void Pan(Vector2 screenDelta)
        {
            if (CameraZoom <= 0f)
                return;

            CameraPosition -= screenDelta / CameraZoom;
        }

        /// <summary>
        /// Pans the camera position directly by a canvas-space delta.
        /// </summary>
        /// <param name="canvasDelta">Displacement vector in canvas units.</param>
        public void PanCanvas(Vector2 canvasDelta)
        {
            CameraPosition -= canvasDelta;
        }
    }
}