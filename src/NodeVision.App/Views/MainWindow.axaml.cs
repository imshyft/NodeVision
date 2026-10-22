using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Selection;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using NodeVision.App.Controls;
using NodeVision.App.Integration;
using NodeVision.Core;
using NodeVision.Inference;
using NodeVision.Visualisation;
using Avalonia.Platform.Storage;

namespace NodeVision.App.Views;

public partial class MainWindow : Window
{
    private string? _selectedSaveFilePath;
    private IReadOnlyList<CameraDeviceOption> _availableCameras = Array.Empty<CameraDeviceOption>();
    private readonly VisualizationEngine _visualizationEngine = new();
    private readonly WebcamFrameRingBuffer _webcamFrameBuffer = new(4);

    // Glue between the ML loop and the visualisation loop: gesture sources push GestureEvents into
    // the queue, and the render tick drains them, maps them to SceneEvents, then hands them to the
    // engine. The hand-tracking source runs alongside the keyboard one once the models load.
    private readonly GestureEventQueue _gestureEvents = new();
    private readonly IGestureToSceneMapper _gestureToSceneMapper;
    private readonly List<GestureEvent> _pendingGestures = new();
    private readonly List<SceneEvent> _pendingSceneEvents = new();

    private KeyboardGestureSource? _keyboardSource;
    private HandGestureSource? _handSource;
    private InferenceEngine? _inferenceEngine;

    private Vector2 _pointerPosition;
    private readonly HashSet<Key> _heldKeys = new();

    private WebcamCaptureService? _webcamCaptureService;
    private DispatcherTimer? _renderTimer;

    private bool _handDebugEnabled;
    private readonly List<string> _handDebugLog = new();

    private bool _helpVisible;
    private const float PanKeyStep = 80f;
    private const float ConnectionHitRadiusPixels = 26f;

    private const float PulseLifetimeSeconds = 0.55f;
    private readonly List<ActivePulse> _interactionPulses = new();
    private bool _pointerInside;

    private readonly PanInertia _panInertia = new();
    private Vector2 _panDeltaThisTick;
    private bool _panGestureStarted;
    private bool _panGestureEnded;
    private bool _stopInertia;

    private static readonly IBrush GestureIdleBackground = new SolidColorBrush(Color.Parse("#121922"));
    private static readonly IBrush GestureActiveBackground = new SolidColorBrush(Color.Parse("#2E4A68"));
    private static readonly IBrush GestureIdleBorder = new SolidColorBrush(Color.Parse("#1E2A36"));
    private static readonly IBrush GestureActiveBorder = new SolidColorBrush(Color.Parse("#5FA8E8"));

    private const long GestureHighlightMs = 500;
    private long _expandHighlightUntilMs;
    private long _collapseHighlightUntilMs;

    public MainWindow()
    {
        InitializeComponent();

        _gestureToSceneMapper = new GestureToSceneMapper(
            normalized => SceneViewControl.FrameToScreen(normalized));

        SceneViewControl.SetWebcamSource(_webcamFrameBuffer);

        SceneViewControl.PanRequested += delta => _visualizationEngine.Pan(delta);

        SceneViewControl.Clicked += point =>
        {
            var viewport = SceneViewControl.ViewportSize;
            var canvasPoint = _visualizationEngine.ScreenToCanvas(point, viewport);
            if (_visualizationEngine.HitTestNode(canvasPoint) is not { } nodeId)
                return;

            if (!_visualizationEngine.CanExpand(nodeId))
                return;

            var wasExpanded = _visualizationEngine.IsExpanded(nodeId);
            _visualizationEngine.ToggleExpanded(nodeId);

            if (_visualizationEngine.TryGetNodeScreenBounds(nodeId, viewport, out var min, out var max))
                AddPulseAt((min + max) * 0.5f, wasExpanded ? InteractionPulseKind.Collapse : InteractionPulseKind.Expand);
        };
        SceneViewControl.ZoomRequested += (delta, focalPoint) => _visualizationEngine.ZoomAt(delta, focalPoint, SceneViewControl.ViewportSize);

        _keyboardSource = new KeyboardGestureSource();
        _keyboardSource.GestureAvailable += _gestureEvents.Enqueue;
        _keyboardSource.Start();

        SceneViewControl.PointerMoved += (_, e) =>
        {
            var point = e.GetPosition(SceneViewControl);
            _pointerPosition = new Vector2((float)point.X, (float)point.Y);
            _pointerInside = true;
        };

        SceneViewControl.PointerExited += (_, _) => _pointerInside = false;

        KeyDown += OnWindowKeyDown;
        KeyUp += OnWindowKeyUp;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        // F1 toggles the hand-tracking overlay; ignore auto-repeat like the gesture keys.
        if (e.Key == Key.F1)
        {
            if (_heldKeys.Add(e.Key))
                ToggleHandDebug();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F2)
        {
            if (_heldKeys.Add(e.Key))
                ToggleGestureHelp();
            e.Handled = true;
            return;
        }

        // Arrow keys repeat while held, which is what you want for nudging the canvas.
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            PanByKey(e.Key);
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Home or Key.R or Key.F or Key.E or Key.C)
        {
            if (!_heldKeys.Add(e.Key))
            {
                e.Handled = true;
                return;
            }

            HandleNavigationKey(e.Key);
            e.Handled = true;
            return;
        }

        if (_keyboardSource is null)
            return;

        // Ignore OS key auto-repeat: holding a key acts once, not on every repeat tick.
        if (!_heldKeys.Add(e.Key))
            return;

        var viewport = SceneViewControl.ViewportSize;
        if (viewport.X <= 0f || viewport.Y <= 0f)
            return;

        var normalized = new Vector2(_pointerPosition.X / viewport.X, _pointerPosition.Y / viewport.Y);
        if (_keyboardSource.TryTrigger(e.Key, normalized) is null)
            return;

        e.Handled = true;
    }

    private void OnWindowKeyUp(object? sender, KeyEventArgs e)
    {
        _heldKeys.Remove(e.Key);
        _keyboardSource?.Release(e.Key);
    }

    private void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        SceneViewControl.Scene = _visualizationEngine.Scene;

        LoadAvailableCameras();
        StartRenderLoop();
        StartHandGesturePipeline();

        RestoreLastSession();
    }

    /// <summary>
    /// Starts the hand-tracking source on the same webcam ring buffer the render loop uses. It needs
    /// the ONNX models in the Models folder; if they are missing the pipeline stays off and the app
    /// keeps running with the keyboard source only.
    /// </summary>
    private void StartHandGesturePipeline()
    {
        try
        {
            _inferenceEngine = new InferenceEngine(_webcamFrameBuffer);
            _handSource = new HandGestureSource(_inferenceEngine);
            _handSource.GestureAvailable += _gestureEvents.Enqueue;
            _handSource.Start();
        }
        catch (Exception ex)
        {
            _inferenceEngine?.Dispose();
            _inferenceEngine = null;
            _handSource = null;
            Console.WriteLine($"[Gesture] Hand tracking unavailable: {ex.Message}");
        }
    }

    private void ToggleGestureHelp()
    {
        _helpVisible = !_helpVisible;
        GestureHelpOverlay.IsVisible = _helpVisible;

        if (_helpVisible)
            GestureHelpText.Text = BuildGestureHelpText();
    }

    private static string BuildGestureHelpText()
    {
        return string.Join(Environment.NewLine, new[]
        {
            "Pan — point with the index, curl the other fingers, and touch the middle fingertip to the thumb. Flick to glide.",
            "Zoom — pinch thumb and index, then rotate your hand.",
            "Collapse — hold a fist over a node.",
            "Expand — make a quick fist, then open the palm over a node.",
            string.Empty,
            "Keyboard — arrows pan · Home overview · R reset · F focus · E expand · C collapse · F1 debug · F2 help",
        });
    }

    private void HandleNavigationKey(Key key)
    {
        switch (key)
        {
            case Key.Home:
                _visualizationEngine.ShowOverview();
                break;

            case Key.R:
                _visualizationEngine.ResetCamera();
                break;

            case Key.F:
                FocusHoveredNode();
                break;

            case Key.E:
                SetHoveredNodeExpanded(true);
                break;

            case Key.C:
                SetHoveredNodeExpanded(false);
                break;
        }
    }

    private void PanByKey(Key key)
    {
        var step = key switch
        {
            Key.Left => new Vector2(-PanKeyStep, 0f),
            Key.Right => new Vector2(PanKeyStep, 0f),
            Key.Up => new Vector2(0f, -PanKeyStep),
            Key.Down => new Vector2(0f, PanKeyStep),
            _ => Vector2.Zero,
        };

        if (step.LengthSquared > 0f)
            _visualizationEngine.Pan(step);
    }

    private void FocusHoveredNode()
    {
        var canvas = _visualizationEngine.ScreenToCanvas(_pointerPosition, SceneViewControl.ViewportSize);
        if (_visualizationEngine.HitTestNode(canvas) is { } nodeId)
            _visualizationEngine.FocusOnNode(nodeId);
    }

    private void SetHoveredNodeExpanded(bool expanded)
    {
        var canvas = _visualizationEngine.ScreenToCanvas(_pointerPosition, SceneViewControl.ViewportSize);
        if (_visualizationEngine.HitTestNode(canvas) is not { } nodeId)
            return;

        if (expanded && !_visualizationEngine.CanExpand(nodeId))
            return;

        _visualizationEngine.SetExpanded(nodeId, expanded);
    }

    private void UpdateGestureModeIndicator()
    {
        string? mode = null;

        if (_handSource is not null)
        {
            if (_handSource.IsPinching)
                mode = "ZOOM";
            else if (_handSource.IsPanActive)
                mode = "PAN";
        }

        GestureModeIndicator.IsVisible = mode is not null;
        if (mode is not null)
            GestureModeText.Text = mode;
    }

    /// <summary>
    /// Highlights the gesture currently being made in the sidebar. Pan and zoom are live; expand and
    /// collapse light up for a short moment after they fire.
    /// </summary>
    private void UpdateGestureSidebar()
    {
        if (!GestureSidebar.IsVisible)
            return;

        var panActive = _handSource?.IsPanActive ?? false;
        var zoomActive = _handSource?.IsPinching ?? false;
        var now = Environment.TickCount64;

        SetGestureItemActive(GesturePanItem, panActive);
        SetGestureItemActive(GestureZoomItem, zoomActive);
        SetGestureItemActive(GestureExpandItem, _expandHighlightUntilMs > now);
        SetGestureItemActive(GestureCollapseItem, _collapseHighlightUntilMs > now);
    }

    private static void SetGestureItemActive(Border item, bool active)
    {
        item.Background = active ? GestureActiveBackground : GestureIdleBackground;
        item.BorderBrush = active ? GestureActiveBorder : GestureIdleBorder;
        item.BorderThickness = new Thickness(active ? 1.5 : 1.0);
    }

    private void OnFlipCameraClick(object? sender, RoutedEventArgs e)
    {
        SceneViewControl.MirrorWebcam = !SceneViewControl.MirrorWebcam;
        SceneViewControl.InvalidateVisual();
    }

    private void LoadAvailableCameras()
    {
        _availableCameras = CameraDeviceEnumerator.Enumerate();
        CameraDeviceComboBox.ItemsSource = _availableCameras;

        if (_availableCameras.Count == 0)
        {
            CameraStatusText.Text = "No cameras were detected.";
            ConfirmCameraButton.IsEnabled = false;
        }
        else
        {
            CameraStatusText.Text = "Select a camera to continue.";
        }
    }

    /// <summary>
    /// Pre-fills the last-used camera, capture mode and project path so the user only has to confirm.
    /// Starting is always left to the OK button.
    /// </summary>
    private void RestoreLastSession()
    {
        var settings = AppSettingsStore.Load();
        if (settings is null)
            return;

        if (!string.IsNullOrWhiteSpace(settings.ProjectPath) && File.Exists(settings.ProjectPath))
        {
            _selectedSaveFilePath = settings.ProjectPath;
            SaveFilePathText.Text = $"Filepath: {_selectedSaveFilePath}";
            SaveFileWarningText.IsVisible = false;
        }

        var camera = FindSavedCamera(settings);
        if (camera is null)
        {
            if (_availableCameras.Count > 0)
                CameraStatusText.Text = "Last camera not found. Select a camera to continue.";
            return;
        }

        CameraDeviceComboBox.SelectedItem = camera;
        PopulateCameraModes(camera);
        ApplySavedMode(settings, camera);
    }

    private CameraDeviceOption? FindSavedCamera(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.CameraDisplayName))
        {
            var byName = _availableCameras.FirstOrDefault(
                camera => string.Equals(camera.DisplayName, settings.CameraDisplayName, StringComparison.OrdinalIgnoreCase));
            if (byName is not null)
                return byName;
        }

        if (settings.CameraDeviceIndex is { } index)
            return _availableCameras.FirstOrDefault(camera => camera.DeviceIndex == index);

        return null;
    }

    private void ApplySavedMode(AppSettings settings, CameraDeviceOption camera)
    {
        if (settings.CameraMode is not { } saved)
            return;

        var mode = camera.Modes.FirstOrDefault(candidate =>
            candidate.Width == saved.Width &&
            candidate.Height == saved.Height &&
            Math.Abs(candidate.Fps - saved.Fps) < 0.5 &&
            string.Equals(candidate.PixelFormat, saved.PixelFormat, StringComparison.OrdinalIgnoreCase));

        // A mode that no longer exists leaves the highest-fps default selected.
        if (mode is not null)
            CameraModeComboBox.SelectedItem = mode;
    }

    private void SaveCurrentSettings(CameraDeviceOption camera, CameraMode? mode)
    {
        AppSettingsStore.Save(new AppSettings(
            camera.DisplayName,
            camera.DeviceIndex,
            mode,
            _selectedSaveFilePath));
    }

    private void OnCameraSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        var selectedCamera = CameraDeviceComboBox.SelectedItem as CameraDeviceOption;
        ConfirmCameraButton.IsEnabled = selectedCamera is not null;

        PopulateCameraModes(selectedCamera);
    }

    private void PopulateCameraModes(CameraDeviceOption? camera)
    {
        IReadOnlyList<CameraMode> modes =
            camera?.Modes ?? Array.Empty<CameraMode>();

        CameraModeComboBox.ItemsSource = modes;
        CameraModeComboBox.SelectedItem = camera?.DefaultMode;

        CameraSettingsButton.IsEnabled = modes.Count > 0;
        if (modes.Count == 0)
            CameraSettingsPanel.IsVisible = false;
    }

    private void OnCameraSettingsClick(
        object? sender,
        RoutedEventArgs e)
    {
        CameraSettingsPanel.IsVisible = !CameraSettingsPanel.IsVisible;
    }

    private async void OnConfirmCameraClick(
        object? sender,
        RoutedEventArgs e)
    {
        await TryStartFromSelectionAsync();
    }

    private async Task<bool> TryStartFromSelectionAsync()
    {
        if (CameraDeviceComboBox.SelectedItem is not CameraDeviceOption selectedCamera)
        {
            CameraStatusText.Text = "Please select a camera.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(_selectedSaveFilePath))
        {
            SaveFileWarningText.Text =
                "Please select a save file before continuing.";
            SaveFileWarningText.IsVisible = true;
            return false;
        }

        SaveFileWarningText.IsVisible = false;

        ConfirmCameraButton.IsEnabled = false;
        CameraDeviceComboBox.IsEnabled = false;
        SelectSaveFileButton.IsEnabled = false;

        CameraStatusText.Text =
            $"Starting {selectedCamera.DisplayName}...";

        try
        {
            Scene loadedScene = ProjectLoader.Load(_selectedSaveFilePath);

            // The visualisation engine must now use the loaded scene.
            _visualizationEngine.LoadScene(loadedScene);
            SceneViewControl.Scene = loadedScene;

            var selectedMode = CameraModeComboBox.SelectedItem as CameraMode ?? selectedCamera.DefaultMode;
            await StartSelectedCameraAsync(selectedCamera, selectedMode);

            CameraPlaceholder.IsVisible = false;
            CameraSelectionOverlay.IsVisible = false;
            SceneViewControl.IsVisible = true;
            GestureSidebar.IsVisible = true;

            SaveCurrentSettings(selectedCamera, selectedMode);
            return true;
        }
        catch (Exception ex)
        {
            CameraStatusText.Text =
                $"Unable to start camera: {ex.Message}";

            CameraDeviceComboBox.IsEnabled = true;
            CameraSettingsButton.IsEnabled = selectedCamera.Modes.Count > 0;
            SelectSaveFileButton.IsEnabled = true;
            ConfirmCameraButton.IsEnabled = true;
            return false;
        }
    }

    private async Task StartSelectedCameraAsync(CameraDeviceOption selectedCamera, CameraMode? selectedMode)
    {
        _webcamCaptureService?.Dispose();

        var mode = selectedMode ?? selectedCamera.DefaultMode;

        _webcamCaptureService = new WebcamCaptureService(
            new CaptureConfig(
                DeviceIndex: selectedCamera.DeviceIndex,
                Width: mode?.Width ?? 1280,
                Height: mode?.Height ?? 720,
                Fps: mode?.Fps ?? 30,
                PixelFormat: mode?.PixelFormat),
            _webcamFrameBuffer);

        _webcamCaptureService.Events.FrameCaptured += OnWebcamFrameCaptured;
        _webcamCaptureService.Events.Error += OnWebcamError;
        _webcamCaptureService.Events.Started += OnWebcamStarted;
        _webcamCaptureService.Events.Stopped += OnWebcamStopped;

        await _webcamCaptureService.StartAsync();
    }

    private void StartRenderLoop()
    {
        _renderTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };

        var clock = Stopwatch.StartNew();
        var lastTick = clock.Elapsed;

        _renderTimer.Tick += (_, _) =>
        {
            var now = clock.Elapsed;
            var deltaTime = (float)(now - lastTick).TotalSeconds;
            lastTick = now;

            var viewport = SceneViewControl.ViewportSize;
            _visualizationEngine.ViewportSize = viewport;

            if (viewport.X > 0f && viewport.Y > 0f)
                _keyboardSource?.Update(new Vector2(_pointerPosition.X / viewport.X, _pointerPosition.Y / viewport.Y));

            // Clamped so a stall does not jump an animation straight to its end.
            _visualizationEngine.Update(Math.Min(deltaTime, 0.1f), DrainGestureEvents());

            UpdatePanInertia(Math.Min(deltaTime, 0.1f), now.TotalSeconds);
            UpdateInteractionOverlay(Math.Min(deltaTime, 0.1f));

            SceneViewControl.CameraTranslation =
                _visualizationEngine.CameraPosition;

            SceneViewControl.CameraZoom =
                _visualizationEngine.CameraZoom;

            if (_webcamCaptureService is not null)
            {
                SceneViewControl.UpdateWebcamFrame();
            }

            UpdateHandDebugOverlay();

            SceneViewControl.InvalidateVisual();
        };

        _renderTimer.Start();
    }

    /// <summary>
    /// Drains everything the gesture source has queued since the last frame and maps it to scene
    /// events, so both are processed as a batch inside this tick's engine update.
    /// </summary>
    private IReadOnlyList<SceneEvent> DrainGestureEvents()
    {
        _pendingGestures.Clear();
        _gestureEvents.Drain(_pendingGestures);

        if (_handDebugEnabled)
        {
            foreach (var gesture in _pendingGestures)
                _handDebugLog.Add($"{gesture.Kind} {gesture.Phase} mag={gesture.Magnitude:0.00} @ ({gesture.Position.X:0.00}, {gesture.Position.Y:0.00})");

            while (_handDebugLog.Count > 8)
                _handDebugLog.RemoveAt(0);
        }

        foreach (var gesture in _pendingGestures)
            AddGestureFeedback(gesture);

        _panDeltaThisTick = default;
        _panGestureStarted = false;
        _panGestureEnded = false;
        _stopInertia = false;

        _pendingSceneEvents.Clear();
        foreach (var gesture in _pendingGestures)
        {
            if (gesture.Kind == GestureKind.Point)
            {
                if (gesture.Phase == GesturePhase.Started)
                    _panGestureStarted = true;
                else if (gesture.Phase == GesturePhase.Ended)
                    _panGestureEnded = true;
            }
            else if (gesture.Phase == GesturePhase.Started)
            {
                // Any other gesture takes over from a pan or a fling.
                _stopInertia = true;
            }

            var mapped = _gestureToSceneMapper.Map(gesture);
            for (var i = 0; i < mapped.Count; i++)
            {
                if (mapped[i] is PanSceneEvent pan)
                    _panDeltaThisTick += pan.ScreenDelta;

                _pendingSceneEvents.Add(mapped[i]);
            }
        }

        return _pendingSceneEvents;
    }

    /// <summary>
    /// Applies fling momentum after the gesture-driven events have been handled. Starting a new pan
    /// stops any running fling, and other gestures cancel it as well.
    /// </summary>
    private void UpdatePanInertia(float deltaTime, double now)
    {
        if (_stopInertia)
            _panInertia.Cancel();

        if (_panGestureStarted)
            _panInertia.Begin(now);

        if (_panGestureEnded)
            _panInertia.End();

        var inertiaDelta = _panInertia.Update(deltaTime, now, _panDeltaThisTick);
        if (inertiaDelta.LengthSquared > 0f)
            _visualizationEngine.Pan(inertiaDelta);
    }

    /// <summary>
    /// Turns a started gesture into a transient on-screen pulse, so the presenter gets feedback even
    /// without the debug overlay. Expand/collapse pulses are placed on the node they act on.
    /// </summary>
    private void AddGestureFeedback(GestureEvent gesture)
    {
        if (gesture.Phase != GesturePhase.Started)
            return;

        switch (gesture.Kind)
        {
            case GestureKind.OpenHand:
                _expandHighlightUntilMs = Environment.TickCount64 + GestureHighlightMs;
                if (TryGetExpandableNode(gesture.Position, out var expandId, out var expandCentre))
                {
                    if (!_visualizationEngine.IsExpanded(expandId))
                        AddPulseAt(expandCentre, InteractionPulseKind.Expand);
                }
                else
                {
                    // Aiming the expand gesture at a connection focuses the child it leads to.
                    TryFocusChildUnderConnection(gesture.Position);
                }
                break;

            case GestureKind.Fist:
                _collapseHighlightUntilMs = Environment.TickCount64 + GestureHighlightMs;
                if (TryGetExpandableNode(gesture.Position, out var collapseId, out var collapseCentre) &&
                    _visualizationEngine.IsExpanded(collapseId))
                    AddPulseAt(collapseCentre, InteractionPulseKind.Collapse);
                break;

            case GestureKind.Point:
            case GestureKind.Pinch:
            case GestureKind.Twist:
                AddPulse(gesture.Position, InteractionPulseKind.Gesture);
                break;
        }
    }

    private bool TryGetExpandableNode(Vector2 normalizedPosition, out int nodeId, out Vector2 screenCentre)
    {
        var viewport = SceneViewControl.ViewportSize;
        var screen = SceneViewControl.FrameToScreen(normalizedPosition);
        var canvas = _visualizationEngine.ScreenToCanvas(screen, viewport);

        nodeId = -1;
        screenCentre = screen;

        if (_visualizationEngine.HitTestNode(canvas) is not { } id || !_visualizationEngine.CanExpand(id))
            return false;

        nodeId = id;
        if (_visualizationEngine.TryGetNodeScreenBounds(id, viewport, out var min, out var max))
            screenCentre = (min + max) * 0.5f;

        return true;
    }

    /// <summary>
    /// When the expand gesture is aimed at a connection rather than a node, focus the child node the
    /// connection leads to. Returns false when the point is over a node or not close to any line.
    /// </summary>
    private bool TryFocusChildUnderConnection(Vector2 normalizedPosition)
    {
        var viewport = SceneViewControl.ViewportSize;
        if (viewport.X <= 0f || viewport.Y <= 0f)
            return false;

        var screen = SceneViewControl.FrameToScreen(normalizedPosition);
        var canvas = _visualizationEngine.ScreenToCanvas(screen, viewport);

        // A node under the point takes priority; line focus is only the fallback.
        if (_visualizationEngine.HitTestNode(canvas) is not null)
            return false;

        Connection? best = null;
        var bestDistance = ConnectionHitRadiusPixels;

        foreach (var sceneObject in _visualizationEngine.Scene.Objects)
        {
            if (sceneObject is not Connection connection ||
                connection.ParentNode is not { } parent ||
                connection.ChildNode is not { } child)
                continue;

            if (parent.Reveal <= 0.01f || child.Reveal <= 0.01f)
                continue;

            var distance = DistanceToConnection(screen, parent, child, viewport);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = connection;
            }
        }

        if (best?.ChildNode is not { } target)
            return false;

        _visualizationEngine.FocusOnNode(target.Id);
        return true;
    }

    private float DistanceToConnection(Vector2 screenPoint, Node parent, Node child, Vector2 viewport)
    {
        var startCanvas = parent.Position + parent.Size * 0.5f;
        var endCanvas = child.Position + child.Size * 0.5f;
        var delta = endCanvas - startCanvas;
        var length = delta.Length;
        if (length < 1f)
            return float.MaxValue;

        // Mirrors ConnectionDrawCommandBuilder so the hit test follows the drawn curve.
        var perpendicular = new Vector2(-delta.Y / length, delta.X / length);
        var bow = MathF.Min(70f, length * 0.18f);
        var control1Canvas = startCanvas + delta * 0.33f + perpendicular * bow;
        var control2Canvas = startCanvas + delta * 0.67f + perpendicular * bow;

        var start = _visualizationEngine.CanvasToScreen(startCanvas, viewport);
        var end = _visualizationEngine.CanvasToScreen(endCanvas, viewport);
        var control1 = _visualizationEngine.CanvasToScreen(control1Canvas, viewport);
        var control2 = _visualizationEngine.CanvasToScreen(control2Canvas, viewport);

        const int steps = 24;
        var minimum = float.MaxValue;
        var previous = start;

        for (var i = 1; i <= steps; i++)
        {
            var point = CubicBezier(start, control1, control2, end, i / (float)steps);
            minimum = MathF.Min(minimum, DistanceToSegment(screenPoint, previous, point));
            previous = point;
        }

        return minimum;
    }

    private static Vector2 CubicBezier(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        var inverse = 1f - t;
        return p0 * (inverse * inverse * inverse) +
               p1 * (3f * inverse * inverse * t) +
               p2 * (3f * inverse * t * t) +
               p3 * (t * t * t);
    }

    private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        var abX = b.X - a.X;
        var abY = b.Y - a.Y;
        var lengthSquared = abX * abX + abY * abY;
        if (lengthSquared < 1e-6f)
            return (point - a).Length;

        var t = Math.Clamp(
            ((point.X - a.X) * abX + (point.Y - a.Y) * abY) / lengthSquared,
            0f,
            1f);

        var closest = new Vector2(a.X + abX * t, a.Y + abY * t);
        return (point - closest).Length;
    }

    private void AddPulse(Vector2 normalizedPosition, InteractionPulseKind kind)
    {
        AddPulseAt(SceneViewControl.FrameToScreen(normalizedPosition), kind);
    }

    private void AddPulseAt(Vector2 screenPosition, InteractionPulseKind kind)
    {
        _interactionPulses.Add(new ActivePulse { ScreenPosition = screenPosition, Kind = kind, Age = 0f });
    }

    private void UpdateInteractionOverlay(float deltaTime)
    {
        UpdateGestureModeIndicator();
        UpdateGestureSidebar();

        for (var i = _interactionPulses.Count - 1; i >= 0; i--)
        {
            var pulse = _interactionPulses[i];
            pulse.Age += deltaTime;
            if (pulse.Age >= PulseLifetimeSeconds)
                _interactionPulses.RemoveAt(i);
        }

        var viewport = SceneViewControl.ViewportSize;

        var handOutline = new List<Vector2>();
        var handOccluded = false;
        IReadOnlyList<(Vector2 Min, Vector2 Max)> occluders = Array.Empty<(Vector2 Min, Vector2 Max)>();
        Vector2? handCursor = null;
        var pointer = PointerGesture.None;

        if (_handSource is not null &&
            _handSource.FrameWidth > 0 && _handSource.FrameHeight > 0 &&
            _handSource.LastLandmarks.Length >= 21)
        {
            foreach (var landmark in _handSource.LastLandmarks)
            {
                handOutline.Add(SceneViewControl.FrameToScreen(new Vector2(
                    landmark.X / _handSource.FrameWidth,
                    landmark.Y / _handSource.FrameHeight)));
            }

            occluders = _visualizationEngine.VisibleNodeScreenBounds(viewport);
            handOccluded = IsHandOccluded(handOutline, occluders);

            if (_handSource.IsPinching)
                pointer = PointerGesture.Pinching;
            else if (_handSource.IsPanActive ||
                     _handSource.Diagnostics?.Confirmed is HandState.Pointing or HandState.Closed or HandState.Open)
                pointer = PointerGesture.Pointing;

            if (pointer != PointerGesture.None)
            {
                var palm = _handSource.LastPalmPosition;
                handCursor = SceneViewControl.FrameToScreen(new Vector2(
                    palm.X / _handSource.FrameWidth,
                    palm.Y / _handSource.FrameHeight));
            }
        }

        (Vector2 Min, Vector2 Max)? hoverBounds = null;
        var hoverExpanded = false;

        if (viewport.X > 0f && viewport.Y > 0f && (handCursor is not null || _pointerInside))
        {
            var cursor = handCursor ?? _pointerPosition;
            var canvas = _visualizationEngine.ScreenToCanvas(cursor, viewport);

            if (_visualizationEngine.HitTestNode(canvas) is { } nodeId && _visualizationEngine.CanExpand(nodeId))
            {
                hoverExpanded = _visualizationEngine.IsExpanded(nodeId);
                if (_visualizationEngine.TryGetNodeScreenBounds(nodeId, viewport, out var min, out var max))
                    hoverBounds = (min, max);
            }
        }

        var pulses = new List<InteractionPulse>(_interactionPulses.Count);
        foreach (var pulse in _interactionPulses)
            pulses.Add(new InteractionPulse(
                pulse.ScreenPosition,
                Math.Clamp(pulse.Age / PulseLifetimeSeconds, 0f, 1f),
                pulse.Kind));

        SceneViewControl.Interaction = handCursor is not null || hoverBounds is not null || pulses.Count > 0 || handOccluded
            ? new InteractionOverlay(handCursor, pointer, hoverBounds, hoverExpanded, pulses, handOutline, handOccluded, occluders)
            : null;
    }

    /// <summary>
    /// True when the hand's on-screen bounding box overlaps one of the occluder rectangles (visible
    /// scene nodes), i.e. the scene is drawn over the presenter's hand. Landmarks are screen-space.
    /// </summary>
    private static bool IsHandOccluded(
        IReadOnlyList<Vector2> screenLandmarks,
        IReadOnlyList<(Vector2 Min, Vector2 Max)> occluders)
    {
        if (screenLandmarks.Count == 0 || occluders.Count == 0)
            return false;

        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);

        foreach (var landmark in screenLandmarks)
        {
            min = new Vector2(MathF.Min(min.X, landmark.X), MathF.Min(min.Y, landmark.Y));
            max = new Vector2(MathF.Max(max.X, landmark.X), MathF.Max(max.Y, landmark.Y));
        }

        foreach (var rect in occluders)
        {
            if (max.X >= rect.Min.X && min.X <= rect.Max.X &&
                max.Y >= rect.Min.Y && min.Y <= rect.Max.Y)
                return true;
        }

        return false;
    }

    private void ToggleHandDebug()
    {
        _handDebugEnabled = !_handDebugEnabled;
        HandDebugOverlay.IsVisible = _handDebugEnabled;

        if (!_handDebugEnabled)
        {
            _handDebugLog.Clear();
            SceneViewControl.DebugOverlayEnabled = false;
            SceneViewControl.HandDebug = null;
            SceneViewControl.InvalidateVisual();
        }
    }

    private void UpdateHandDebugOverlay()
    {
        SceneViewControl.DebugOverlayEnabled = _handDebugEnabled;
        if (!_handDebugEnabled)
            return;

        SceneViewControl.HandDebug = BuildHandDebugSnapshot();
        HandDebugText.Text = BuildHandDebugText();
    }

    private HandDebugSnapshot? BuildHandDebugSnapshot()
    {
        if (_handSource is null)
            return null;

        var width = _handSource.FrameWidth;
        var height = _handSource.FrameHeight;
        if (width <= 0 || height <= 0)
            return null;

        var landmarks = _handSource.LastLandmarks;

        Vector2 ToScreen(float x, float y) => SceneViewControl.FrameToScreen(new Vector2(x / width, y / height));

        var debugLandmarks = new List<DebugLandmark>(landmarks.Length);
        foreach (var landmark in landmarks)
            debugLandmarks.Add(new DebugLandmark(ToScreen(landmark.X, landmark.Y), landmark.Name));

        var palmKeypoints = new List<Vector2>();
        (Vector2 Min, Vector2 Max)? palmBox = null;
        if (_handSource.LastPalm is { } palm)
        {
            foreach (var keypoint in palm.PalmLandmarks)
                palmKeypoints.Add(ToScreen(keypoint.X, keypoint.Y));

            var cornerA = ToScreen(palm.Box.X1, palm.Box.Y1);
            var cornerB = ToScreen(palm.Box.X2, palm.Box.Y2);
            palmBox = (
                new Vector2(MathF.Min(cornerA.X, cornerB.X), MathF.Min(cornerA.Y, cornerB.Y)),
                new Vector2(MathF.Max(cornerA.X, cornerB.X), MathF.Max(cornerA.Y, cornerB.Y)));
        }

        Vector2? pinchPosition = null;
        Vector2? pointPosition = null;
        Vector2? palmCenter = null;
        if (landmarks.Length > 0)
        {
            pinchPosition = ToScreen(_handSource.LastPinch.Position.X, _handSource.LastPinch.Position.Y);

            if (landmarks.Length > 8)
                pointPosition = ToScreen(landmarks[8].X, landmarks[8].Y);

            if (landmarks.Length >= 21)
                palmCenter = ToScreen(_handSource.LastPalmPosition.X, _handSource.LastPalmPosition.Y);
        }

        return new HandDebugSnapshot(
            debugLandmarks,
            palmKeypoints,
            palmBox,
            _handSource.IsPinching,
            _handSource.LastPinch.Strength,
            pinchPosition,
            pointPosition,
            palmCenter);
    }

    private string BuildHandDebugText()
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine("Hand debug (F1)");

        if (_handSource is null)
        {
            text.AppendLine("  no hand-tracking pipeline");
        }
        else
        {
            text.AppendLine($"  frame: {_handSource.FrameWidth}x{_handSource.FrameHeight}");
            text.AppendLine($"  landmarks: {_handSource.LastLandmarks.Length}");
            text.AppendLine($"  palm: ({_handSource.LastPalmPosition.X:0}, {_handSource.LastPalmPosition.Y:0})");
            text.AppendLine($"  palm score: {_handSource.LastPalm?.Box.Score ?? 0f:0.00}");
            text.AppendLine($"  presence: {_handSource.LastPresence:0.00}  handedness: {_handSource.LastHandedness:0.00}");

            if (_handSource.Diagnostics is { } diagnostics)
            {
                text.AppendLine($"  confirmed: {diagnostics.Confirmed}  candidate: {diagnostics.Candidate}");
                text.AppendLine($"  index: curled:{Flag(diagnostics.IndexCurled)} extended:{Flag(diagnostics.IndexExtended)} straight:{diagnostics.IndexStraightness:0.00} up:{Flag(diagnostics.IndexPointingUp)}");
                text.AppendLine($"  curled  mid:{Flag(diagnostics.MiddleCurled)} ring:{Flag(diagnostics.RingCurled)} pinky:{Flag(diagnostics.PinkyCurled)}");
                text.AppendLine($"  folded  mid:{Flag(diagnostics.MiddleFolded)} {diagnostics.MiddlePalmRatio:0.00}  ring:{Flag(diagnostics.RingFolded)} {diagnostics.RingPalmRatio:0.00}  pinky:{Flag(diagnostics.PinkyFolded)} {diagnostics.PinkyPalmRatio:0.00}");
                text.AppendLine($"  middle-thumb:{diagnostics.MiddleThumbRatio:0.00}  palm-width:{diagnostics.PalmWidthRatio:0.00}  pinch-reach:{diagnostics.PinchReachRatio:0.00}  pan:{Flag(_handSource.IsPanActive)}");
            }
            else
            {
                text.AppendLine("  state: no hand");
            }

            var pinch = _handSource.LastPinch;
            text.AppendLine($"  pinch: {pinch.Strength:0.00} ({(_handSource.IsPinching ? "held" : "open")})");
            text.AppendLine($"  roll: {_handSource.LastHandRoll * (180f / MathF.PI):0} deg");
            text.AppendLine($"  expand: {DescribeExpandState()}");
            text.AppendLine($"  fist fired:{Flag(_handSource.CollapseFiredThisFist)}  hold:{_handSource.FistHoldElapsedMs}/{HandGestureSource.FistHoldMs}ms");

            if (_handSource.LastStateChange is { } change)
                text.AppendLine($"  last change: {change.PreviousState} -> {change.NewState}");

            if (_handSource.LastGesture is { } gesture)
                text.AppendLine($"  last gesture: {gesture.Kind} {gesture.Phase} mag={gesture.Magnitude:0.00}");
        }

        if (_handDebugLog.Count > 0)
        {
            text.AppendLine("  events:");
            foreach (var entry in _handDebugLog)
                text.AppendLine($"    {entry}");
        }

        return text.ToString();
    }

    private static string Flag(bool value) => value ? "Y" : "n";

    /// <summary>
    /// Explains how close this frame is to firing an expand, so the F1 overlay can show why it did or
    /// did not happen.
    /// </summary>
    private string DescribeExpandState()
    {
        if (_handSource is null)
            return "n/a";

        if (_handSource.ExpandArmed)
            return "ARMED - expanding this frame";

        if (_handSource.Diagnostics is not { } state)
            return "no hand";

        if (state.Confirmed == HandState.Closed && state.Candidate == HandState.Open)
            return "opening - waiting for Open to confirm";

        if (state.Confirmed == HandState.Closed && _handSource.CollapseFiredThisFist)
            return "blocked - this fist already collapsed; open will NOT expand";

        if (state.Confirmed == HandState.Closed)
            return "fist held - open before the hold fires to expand";

        return "idle - close the hand, then open it";
    }

    private void OnWebcamFrameCaptured(WebcamFrame frame)
    {
        // Runs on the webcam capture thread.
        // Frame data is already being placed into the ring buffer.
    }

    private void OnWebcamStarted()
    {
        var mode = _webcamCaptureService?.ActualMode;
        Console.WriteLine(mode is null
            ? "[Webcam] Started"
            : $"[Webcam] Started at {mode}");
    }

    private void OnWebcamStopped()
    {
        Console.WriteLine("[Webcam] Stopped");
    }

    private void OnWebcamError(Exception exception)
    {
        Console.WriteLine($"[Webcam] Error: {exception.Message}");
    }

    protected override void OnClosed(EventArgs e)
    {
        _renderTimer?.Stop();

        _handSource?.Stop();
        _inferenceEngine?.Dispose();
        _webcamCaptureService?.Dispose();
        _webcamFrameBuffer.Dispose();
        
        ProjectLoader.CleanupTemporaryAssets();

        base.OnClosed(e);
    }
    
    private async void OnSelectSaveFileClick(
        object? sender,
        RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Select NodeVision save file",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("NodeVision project")
                    {
                        Patterns = new[] { "*.nodevision" }
                    },
                    FilePickerFileTypes.All
                }
            });

        // The user cancelled the dialog.
        if (files.Count == 0)
            return;

        var selectedFile = files[0];
        var localPath = selectedFile.TryGetLocalPath();

        if (string.IsNullOrWhiteSpace(localPath))
        {
            _selectedSaveFilePath = null;
            SaveFilePathText.Text = "Filepath: ...";
            SaveFileWarningText.Text =
                "The selected file does not have a valid local filepath.";
            SaveFileWarningText.IsVisible = true;
            return;
        }

        _selectedSaveFilePath = localPath;
        SaveFilePathText.Text = $"Filepath: {_selectedSaveFilePath}";
        SaveFileWarningText.IsVisible = false;
    }

    private sealed class ActivePulse
    {
        public Vector2 ScreenPosition;
        public InteractionPulseKind Kind;
        public float Age;
    }
}
