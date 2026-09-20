using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Selection;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NodeVision.App.Integration;
using NodeVision.Core;
using NodeVision.Inference;
using NodeVision.Visualisation;

namespace NodeVision.App.Views;

public partial class MainWindow : Window
{
    private readonly VisualizationEngine _visualizationEngine = new();
    private readonly WebcamFrameRingBuffer _webcamFrameBuffer = new(4);

    // Glue between the ML loop and the visualisation loop: a gesture source pushes GestureEvents into
    // the queue, and the render tick drains them, maps them to SceneEvents, then hands them to the
    // engine.
    private KeyboardGestureSource? _gestureSource; //TODO: switch out with HandGestureSource once models ready
    
    private readonly GestureEventQueue _gestureEvents = new();
    // TODO: make an actual Gesture mapper; this just returns an empty list always
    private readonly IGestureToSceneMapper _gestureToSceneMapper = new NullGestureToSceneMapper();
    private readonly List<GestureEvent> _pendingGestures = new();
    private readonly List<SceneEvent> _pendingSceneEvents = new();

    private Vector2 _pointerPosition;

    private WebcamCaptureService? _webcamCaptureService;
    private DispatcherTimer? _renderTimer;

    public MainWindow()
    {
        InitializeComponent();

        SceneViewControl.SetWebcamSource(_webcamFrameBuffer);

        SceneViewControl.PanRequested += delta => _visualizationEngine.Pan(delta);

        SceneViewControl.Clicked += point =>
        {
            var canvasPoint = _visualizationEngine.ScreenToCanvas(point, SceneViewControl.ViewportSize);
            if (_visualizationEngine.HitTestNode(canvasPoint) is { } nodeId)
                _visualizationEngine.ToggleExpanded(nodeId);
        };
        SceneViewControl.ZoomRequested += (delta, focalPoint) => _visualizationEngine.ZoomAt(delta, focalPoint, SceneViewControl.ViewportSize);

        _gestureSource = new KeyboardGestureSource();
        _gestureSource.GestureAvailable += _gestureEvents.Enqueue;
        _gestureSource.Start();
        DebugGestureText.Text = _gestureSource.BuildDebugText();

        SceneViewControl.PointerMoved += (_, e) =>
        {
            var point = e.GetPosition(SceneViewControl);
            _pointerPosition = new Vector2((float)point.X, (float)point.Y);
        };

        KeyDown += OnWindowKeyDown;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (_gestureSource is null)
            return;

        var viewport = SceneViewControl.ViewportSize;
        if (viewport.X <= 0f || viewport.Y <= 0f)
            return;

        var normalized = new Vector2(_pointerPosition.X / viewport.X, _pointerPosition.Y / viewport.Y);
        if (_gestureSource.TryTrigger(e.Key, normalized) is null)
            return;

        DebugGestureText.Text = _gestureSource.BuildDebugText();
        e.Handled = true;
    }

    private void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        SceneViewControl.Scene = _visualizationEngine.Scene;

        LoadAvailableCameras();
        StartRenderLoop();
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
        ConfirmCameraButton.IsEnabled =
            CameraDeviceComboBox.SelectedItem is CameraDeviceOption;
    }

    private async void OnConfirmCameraClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (CameraDeviceComboBox.SelectedItem is not CameraDeviceOption selectedCamera)
        {
            return;
        }

        ConfirmCameraButton.IsEnabled = false;
        CameraDeviceComboBox.IsEnabled = false;
        CameraStatusText.Text = $"Starting {selectedCamera.DisplayName}...";

        try
        {
            await StartSelectedCameraAsync(selectedCamera);

            CameraPlaceholder.IsVisible = false;
            CameraSelectionOverlay.IsVisible = false;
            SceneViewControl.IsVisible = true;
        }
        catch (Exception ex)
        {
            CameraStatusText.Text = $"Unable to start camera: {ex.Message}";

            CameraDeviceComboBox.IsEnabled = true;
            ConfirmCameraButton.IsEnabled = true;
        }
    }

    private async Task StartSelectedCameraAsync(CameraDeviceOption selectedCamera)
    {
        _webcamCaptureService?.Dispose();

        _webcamCaptureService = new WebcamCaptureService(
            new CaptureConfig(
                DeviceIndex: selectedCamera.DeviceIndex,
                Width: 1280,
                Height: 720,
                Fps: 30),
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

            _visualizationEngine.ViewportSize = SceneViewControl.ViewportSize;

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

        _pendingSceneEvents.Clear();
        foreach (var gesture in _pendingGestures)
            _pendingSceneEvents.AddRange(_gestureToSceneMapper.Map(gesture));

        return _pendingSceneEvents;
    }

    private void OnWebcamFrameCaptured(WebcamFrame frame)
    {
        // Runs on the webcam capture thread.
        // Frame data is already being placed into the ring buffer.
    }

    private void OnWebcamStarted()
    {
        Console.WriteLine("[Webcam] Started");
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

        _webcamCaptureService?.Dispose();
        _webcamFrameBuffer.Dispose();

        base.OnClosed(e);
    }
}
