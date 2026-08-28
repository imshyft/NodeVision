using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Selection;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NodeVision.Core;
using NodeVision.Inference;
using NodeVision.Visualisation;

namespace NodeVision.App.Views;

public partial class MainWindow : Window
{
    private readonly VisualizationEngine _visualizationEngine = new();
    private readonly WebcamFrameRingBuffer _webcamFrameBuffer = new(4);

    private WebcamCaptureService? _webcamCaptureService;
    private DispatcherTimer? _renderTimer;

    public MainWindow()
    {
        InitializeComponent();

        SceneViewControl.SetWebcamSource(_webcamFrameBuffer);
    }

    private void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        SceneViewControl.Scene = _visualizationEngine.Scene;

        LoadAvailableCameras();
        StartRenderLoop();
    }

    private void LoadAvailableCameras()
    {
        //Camera Device Option found in Core
        IReadOnlyList<CameraDeviceOption> availableCameras =
            GetAvailableCameraDevices();

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

        _renderTimer.Tick += OnRenderTimerTick;
        _renderTimer.Start();
    }

    private void OnRenderTimerTick(object? sender, EventArgs e)
    {
        _visualizationEngine.Update(1f / 60f);

        SceneViewControl.CameraTranslation =
            _visualizationEngine.CameraPosition;

        SceneViewControl.CameraZoom =
            _visualizationEngine.CameraZoom;

        if (_webcamCaptureService is not null)
        {
            SceneViewControl.UpdateWebcamFrame();
        }

        SceneViewControl.InvalidateVisual();
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
    
    private IReadOnlyList<CameraDeviceOption> GetAvailableCameraDevices()
    {
        return new List<CameraDeviceOption>
        {
            new(0, "Camera 1"),
            new(1, "Camera 2"),
            new(2, "Camera 3")
        };
    }
}