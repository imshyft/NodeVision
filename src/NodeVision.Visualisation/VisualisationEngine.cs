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

        /// <summary>
        /// Converts a screen pixel position to canvas coordinates.
        /// </summary>
        /// <param name="screenPoint">Screen coordinate in pixels.</param>
        /// <param name="viewportSize">Dimensions of the render viewport in pixels.</param>
        public Vector2 ScreenToCanvas(Vector2 screenPoint, Vector2 viewportSize)
        {
            if (CameraZoom <= 0f)
                return CameraPosition;

            Vector2 center = viewportSize * 0.5f;
            return CameraPosition + (screenPoint - center) / CameraZoom;
        }

        /// <summary>
        /// Converts a canvas coordinate to screen pixel position.
        /// </summary>
        /// <param name="canvasPoint">Position in canvas units.</param>
        /// <param name="viewportSize">Dimensions of the render viewport in pixels.</param>
        public Vector2 CanvasToScreen(Vector2 canvasPoint, Vector2 viewportSize)
        {
            Vector2 center = viewportSize * 0.5f;
            return center + (canvasPoint - CameraPosition) * CameraZoom;
        }

        /// <summary>
        /// Zooms the camera relative to a specific focal point on screen (e.g. mouse cursor).
        /// Automatically adjusts camera position so the world point under the cursor stays fixed.
        /// </summary>
        /// <param name="zoomDelta">Amount to change the zoom level.</param>
        /// <param name="focalScreenPoint">The screen position (in pixels) to pivot zoom around.</param>
        /// <param name="viewportSize">Dimensions of the render viewport in pixels.</param>
        /// <param name="minZoom">Minimum allowed zoom level.</param>
        /// <param name="maxZoom">Maximum allowed zoom level.</param>
        public void ZoomAt(float zoomDelta, Vector2 focalScreenPoint, Vector2 viewportSize, float minZoom = 0.1f, float maxZoom = 10f)
        {
            float oldZoom = CameraZoom;
            float newZoom = MathF.Max(minZoom, MathF.Min(maxZoom, oldZoom + zoomDelta));
            if (MathF.Abs(newZoom - oldZoom) < 0.0001f)
                return;

            Vector2 canvasFocalPoint = ScreenToCanvas(focalScreenPoint, viewportSize);
            CameraZoom = newZoom;
            
            Vector2 center = viewportSize * 0.5f;
            CameraPosition = canvasFocalPoint - (focalScreenPoint - center) / newZoom;
        }

        /// <summary>
        /// Centers the camera on a specific coordinate in the canvas.
        /// </summary>
        /// <param name="canvasPosition">Target canvas position.</param>
        public void FocusOn(Vector2 canvasPosition)
        {
            CameraPosition = canvasPosition;
        }

        /// <summary>
        /// Resets the camera to default position (0, 0) and default zoom (1.0).
        /// </summary>
        public void ResetCamera()
        {
            CameraPosition = Vector2.Zero;
            CameraZoom = 1f;
        }
    }
}