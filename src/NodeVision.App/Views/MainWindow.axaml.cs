using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NodeVision.Core;
using NodeVision.Visualisation;

namespace NodeVision.App.Views;

public partial class MainWindow : Window
{
    private readonly VisualizationEngine _visualisation = new VisualizationEngine();
    
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        SceneViewControl.Scene = _visualisation.Scene;
        
        DispatcherTimer timer = new()
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };

        timer.Tick += (_, _) =>
        {
            _visualisation.Update(1f / 60f);

            SceneViewControl.CameraTranslation = _visualisation.CameraPosition;
            SceneViewControl.CameraZoom = _visualisation.CameraZoom;

            SceneViewControl.InvalidateVisual();
        };

        timer.Start();
    }
}