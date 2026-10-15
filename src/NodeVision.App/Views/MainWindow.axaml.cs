using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Selection;
using Avalonia.Input;
using Avalonia.Interactivity;
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
    private string _handStatus = "hand: starting";

    private Vector2 _pointerPosition;
    private readonly HashSet<Key> _heldKeys = new();

    private WebcamCaptureService? _webcamCaptureService;
    private DispatcherTimer? _renderTimer;

    private bool _handDebugEnabled;
    private readonly List<string> _handDebugLog = new();

    public MainWindow()
    {
        InitializeComponent();

        _gestureToSceneMapper = new GestureToSceneMapper(
            () => SceneViewControl.ViewportSize,
            screenPoint => _visualizationEngine.ScreenToCanvas(screenPoint, SceneViewControl.ViewportSize));

        SceneViewControl.SetWebcamSource(_webcamFrameBuffer);

        SceneViewControl.PanRequested += delta => _visualizationEngine.Pan(delta);

        SceneViewControl.Clicked += point =>
        {
            var canvasPoint = _visualizationEngine.ScreenToCanvas(point, SceneViewControl.ViewportSize);
            if (_visualizationEngine.HitTestNode(canvasPoint) is { } nodeId)
                _visualizationEngine.ToggleExpanded(nodeId);
        };
        SceneViewControl.ZoomRequested += (delta, focalPoint) => _visualizationEngine.ZoomAt(delta, focalPoint, SceneViewControl.ViewportSize);

        _keyboardSource = new KeyboardGestureSource();
        _keyboardSource.GestureAvailable += _gestureEvents.Enqueue;
        _keyboardSource.Start();
        UpdateDebugOverlay();

        SceneViewControl.PointerMoved += (_, e) =>
        {
            var point = e.GetPosition(SceneViewControl);
            _pointerPosition = new Vector2((float)point.X, (float)point.Y);
        };

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

        UpdateDebugOverlay();
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
            _handStatus = "hand: tracking";
        }
        catch (Exception ex)
        {
            _inferenceEngine?.Dispose();
            _inferenceEngine = null;
            _handSource = null;
            _handStatus = "hand: unavailable";
            Console.WriteLine($"[Gesture] Hand tracking unavailable: {ex.Message}");
        }

        UpdateDebugOverlay();
    }

    private void UpdateDebugOverlay()
    {
        DebugGestureText.Text = (_keyboardSource?.BuildDebugText() ?? string.Empty)
            + Environment.NewLine + _handStatus
            + Environment.NewLine + "F1: toggle hand debug";
    }

    private void LoadAvailableCameras()
    {
        IReadOnlyList<CameraDeviceOption> availableCameras =
            CameraDeviceEnumerator.Enumerate();

        CameraDeviceComboBox.ItemsSource = availableCameras;

        if (availableCameras.Count == 0)
        {
            CameraStatusText.Text = "No cameras were detected.";
            ConfirmCameraButton.IsEnabled = false;
        }
        else
        {
            CameraStatusText.Text = "Select a camera to continue.";
        }
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
        if (CameraDeviceComboBox.SelectedItem is not CameraDeviceOption selectedCamera)
        {
            CameraStatusText.Text = "Please select a camera.";
            return;
        }

        if (string.IsNullOrWhiteSpace(_selectedSaveFilePath))
        {
            SaveFileWarningText.Text =
                "Please select a save file before continuing.";
            SaveFileWarningText.IsVisible = true;
            return;
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

            var selectedMode = CameraModeComboBox.SelectedItem as CameraMode;
            await StartSelectedCameraAsync(selectedCamera, selectedMode);

            CameraPlaceholder.IsVisible = false;
            CameraSelectionOverlay.IsVisible = false;
            SceneViewControl.IsVisible = true;
            
        }
        catch (Exception ex)
        {
            CameraStatusText.Text =
                $"Unable to start camera: {ex.Message}";

            CameraDeviceComboBox.IsEnabled = true;
            CameraSettingsButton.IsEnabled = selectedCamera.Modes.Count > 0;
            SelectSaveFileButton.IsEnabled = true;
            ConfirmCameraButton.IsEnabled = true;
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

        _pendingSceneEvents.Clear();
        foreach (var gesture in _pendingGestures)
            _pendingSceneEvents.AddRange(_gestureToSceneMapper.Map(gesture));

        return _pendingSceneEvents;
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

        var debugLandmarks = new List<DebugLandmark>(landmarks.Length);
        foreach (var landmark in landmarks)
            debugLandmarks.Add(new DebugLandmark(new Vector2(landmark.X / width, landmark.Y / height), landmark.Name));

        var palmKeypoints = new List<Vector2>();
        (Vector2 Min, Vector2 Max)? palmBox = null;
        if (_handSource.LastPalm is { } palm)
        {
            foreach (var keypoint in palm.PalmLandmarks)
                palmKeypoints.Add(new Vector2(keypoint.X / width, keypoint.Y / height));

            palmBox = (
                new Vector2(palm.Box.X1 / width, palm.Box.Y1 / height),
                new Vector2(palm.Box.X2 / width, palm.Box.Y2 / height));
        }

        Vector2? pinchPosition = null;
        Vector2? pointPosition = null;
        if (landmarks.Length > 0)
        {
            pinchPosition = new Vector2(_handSource.LastPinch.Position.X / width, _handSource.LastPinch.Position.Y / height);

            if (landmarks.Length > 8)
                pointPosition = new Vector2(landmarks[8].X / width, landmarks[8].Y / height);
        }

        return new HandDebugSnapshot(
            debugLandmarks,
            palmKeypoints,
            palmBox,
            _handSource.IsPinching,
            _handSource.LastPinch.Strength,
            pinchPosition,
            pointPosition);
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
            text.AppendLine($"  palm score: {_handSource.LastPalm?.Box.Score ?? 0f:0.00}");
            text.AppendLine($"  presence: {_handSource.LastPresence:0.00}  handedness: {_handSource.LastHandedness:0.00}");

            if (_handSource.Diagnostics is { } diagnostics)
            {
                text.AppendLine($"  confirmed: {diagnostics.Confirmed}  candidate: {diagnostics.Candidate}");
                text.AppendLine($"  curled  idx:{Flag(diagnostics.IndexCurled)} mid:{Flag(diagnostics.MiddleCurled)} ring:{Flag(diagnostics.RingCurled)} pinky:{Flag(diagnostics.PinkyCurled)}");
                text.AppendLine($"  index up: {Flag(diagnostics.IndexPointingUp)}");
            }
            else
            {
                text.AppendLine("  state: no hand");
            }

            var pinch = _handSource.LastPinch;
            text.AppendLine($"  pinch: {pinch.Strength:0.00} ({(_handSource.IsPinching ? "held" : "open")})");

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
}
