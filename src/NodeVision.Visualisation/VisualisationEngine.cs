using System.Collections.Generic;
using System.Diagnostics;
using NodeVision.Core;
using NodeVision.Visualisation.Persistence;

namespace NodeVision.Visualisation
{
    public class VisualizationEngine
    {
        private readonly PresentationState _presentation = new();
        private readonly List<ISceneBehaviour> _behaviours = new();
        private readonly SceneGraph _graph;
        private float _time;

        public Scene Scene { get; }

        /// <summary>
        /// Parent/child structure and the per-node reveal animation for that scene.
        /// </summary>
        public NodeExpansion Expansion { get; } = new();

        public Vector2 CameraPosition { get; set; }
        public float CameraZoom { get; set; } = 1f;

        /// <summary>Size of the render viewport in pixels; used to resolve screen-space scene events.</summary>
        public Vector2 ViewportSize { get; set; }

        public NodeLayout CurrentLayout { get; } = new();

        public VisualizationEngine()
        {
            Scene = TestSceneFactory.CreateScene();

            // One behaviour per feature. Adding one here is all it takes for it to run every frame.
            _behaviours.Add(Expansion);

            _graph = SceneGraph.Build(Scene);
            _graph.ApplyDrawOrder(Scene);

            foreach (var behaviour in _behaviours)
                behaviour.Build(_graph);

            Refresh(0f); // present the initial state, so nothing animates in on the first frame
        }

        public void Update(float deltaTime)
        {
            _time += deltaTime;

            Refresh(deltaTime);
            SyncLayout();
        }

        /// <summary>
        /// Applies the scene events accumulated since the last frame, then runs the frame. The scene
        /// is only ever mutated here, on the visualisation loop, never on the gesture/inference thread.
        /// </summary>
        public void Update(float deltaTime, IReadOnlyList<SceneEvent> sceneEvents)
        {
            foreach (var sceneEvent in sceneEvents)
                ApplySceneEvent(sceneEvent);

            Update(deltaTime);
        }

        private void ApplySceneEvent(SceneEvent sceneEvent)
        {
            switch (sceneEvent)
            {
                case PanSceneEvent pan:
                    Pan(pan.ScreenDelta);
                    break;

                case ZoomSceneEvent zoom:
                    ZoomAt(zoom.Amount, zoom.ScreenFocalPoint, ViewportSize);
                    break;

                case ExpandSceneEvent expand:
                    if (HitTestNode(expand.CanvasPosition) is { } nodeId)
                        ToggleExpanded(nodeId);
                    break;

                case ResetSceneEvent:
                    ResetCamera();
                    break;
            }
        }

        /// <summary>
        /// Runs every behaviour for this frame and applies what they agree on.
        /// </summary>
        private void Refresh(float deltaTime)
        {
            _presentation.Reset();

            foreach (var behaviour in _behaviours)
                behaviour.Update(deltaTime, _graph, _presentation);

            // The only writer of Node.Reveal and node positions, so two features cannot fight over
            // the same field and last-writer-wins is never a surprise.
            _presentation.Apply(_graph);
        }

        /// <summary>
        /// Mirrors the canvas layout for persistence. A node can be mid-reveal, so the layout records
        /// the position the scene authored rather than the animated one.
        /// </summary>
        private void SyncLayout()
        {
            foreach (var obj in Scene.Objects)
            {
                if (obj is not Node node)
                {
                    continue;
                }

                if (!CurrentLayout.Positions.TryGetValue(node.Id, out var position))
                {
                    position = new NodePosition();
                    CurrentLayout.Positions[node.Id] = position;
                }

                var source = _graph.Contains(node.Id) ? _graph.AuthoredPosition(node.Id) : node.Position;
                position.X = source.X;
                position.Y = source.Y;
            }
        }

        /// <summary>
        /// True when the node is expanded, meaning its children are being revealed.
        /// </summary>
        public bool IsExpanded(int nodeId) => Expansion.IsExpanded(nodeId);

        /// <summary>
        /// Expands or collapses a node, animating its children out of it or back into it.
        /// </summary>
        public void ToggleExpanded(int nodeId) => Expansion.ToggleExpanded(nodeId);

        /// <summary>
        /// The topmost visible node under a canvas-space point, or null. Pointer input has to be
        /// converted with <see cref="ScreenToCanvas"/> first.
        /// </summary>
        public int? HitTestNode(Vector2 canvasPoint) => _presentation.HitTest(canvasPoint, _graph);

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