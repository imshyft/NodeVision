using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NodeVision.Core;
using NodeVision.Inference;
using NodeVision.Visualisation;

namespace NodeVision.App.Views;

public partial class MainWindow : Window
{
    private readonly VisualizationEngine _visualisation = new VisualizationEngine();
    private readonly WebcamCaptureService _webcamCapture;
    private readonly WebcamFrameRingBuffer _ringBuffer = new(4);
    
    public MainWindow()
    {
        InitializeComponent();
        
        // load webcam
        _webcamCapture = new WebcamCaptureService(
            new CaptureConfig(DeviceIndex: 3, Width: 1280, Height: 720, Fps: 30),
            _ringBuffer);
        
        _webcamCapture.Events.FrameCaptured += OnWebcamFrameCaptured;
        _webcamCapture.Events.Error += OnWebcamError;
        _webcamCapture.Events.Started += () => Console.WriteLine("[Webcam] Started");
        _webcamCapture.Events.Stopped += () => Console.WriteLine("[Webcam] Stopped");
        
        SceneViewControl.SetWebcamSource(_ringBuffer);
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        SceneViewControl.Scene = _visualisation.Scene;
        
        _ = _webcamCapture.StartAsync();
        
        DispatcherTimer timer = new()
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };

        timer.Tick += (_, _) =>
        {
            _visualisation.Update(1f / 60f);

            SceneViewControl.CameraTranslation = _visualisation.CameraPosition;
            SceneViewControl.CameraZoom = _visualisation.CameraZoom;
            
            SceneViewControl.UpdateWebcamFrame();
            
            // force updates control
            SceneViewControl.InvalidateVisual();
        };

        timer.Start();
    }

    private void OnWebcamFrameCaptured(WebcamFrame frame)
    {
        // callback on capture thread
        // Console.WriteLine($"[Webcam] Frame: {frame.Width}x{frame.Height} @ {frame.Timestamp}");
    }

    private void OnWebcamError(Exception ex)
    {
        Console.WriteLine($"[Webcam] Error: {ex.Message}");
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _webcamCapture.Dispose();
        _ringBuffer.Dispose();
    }
}