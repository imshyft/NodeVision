using Avalonia.Controls;
using Avalonia.Interactivity;
using NodeVision.Core;
using NodeVision.Visualisation;

namespace NodeVision.App.Views;

public partial class MainWindow : Window
{
    private Scene scene;
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        scene = TestSceneFactory.CreateScene();
        SceneViewControl.SetScene(scene);
    }
}