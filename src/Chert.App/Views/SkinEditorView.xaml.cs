using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Chert.App.ViewModels;

namespace Chert.App.Views;

public partial class SkinEditorView : UserControl
{
    private SkinEditorViewModel VM => (SkinEditorViewModel)DataContext;

    public SkinEditorView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        BuildPalette();
        UpdateColorPreviews();
        VM.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(VM.PrimaryColor) or nameof(VM.SecondaryColor))
                UpdateColorPreviews();
            // FullBitmap 为原地改写的同一 WriteableBitmap 实例，DP 不会触发重建，故手动刷新 3D
            if (args.PropertyName is nameof(VM.FullBitmap) or nameof(VM.IsSlim))
                Skin3D.RebuildModel();
        };
    }

    private void BuildPalette()
    {
        foreach (var c in VM.Palette)
        {
            var rect = new Border
            {
                Width = 24, Height = 24, Margin = new Thickness(1),
                Background = new SolidColorBrush(c),
                Cursor = Cursors.Hand
            };
            rect.MouseLeftButtonDown += (_, __) => VM.PrimaryColor = c;
            rect.MouseRightButtonDown += (_, e2) => { VM.SecondaryColor = c; e2.Handled = true; };
            PalettePanel.Children.Add(rect);
        }
    }

    private void UpdateColorPreviews()
    {
        PrimaryColorPreview.Background = new SolidColorBrush(VM.PrimaryColor);
        SecondaryColorPreview.Background = new SolidColorBrush(VM.SecondaryColor);
    }

    private void Face_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBlock tb && tb.DataContext is SkinFace face)
            VM.SelectedFace = face;
    }

    private void Mode_Changed(object sender, RoutedEventArgs e)
    {
        var is2D = sender == Mode2D;
        Mode2D.IsChecked = is2D;
        Mode3D.IsChecked = !is2D;
        Editor2D.Visibility = is2D ? Visibility.Visible : Visibility.Collapsed;
        Preview3D.Visibility = is2D ? Visibility.Collapsed : Visibility.Visible;
    }

    private void FaceCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        VM.Paint(e.GetPosition(FaceImage));
    }

    private void FaceCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            VM.Paint(e.GetPosition(FaceImage));
    }

    private void FillBtn_Click(object sender, RoutedEventArgs e)
    {
        // Flood fill at center of current face
        if (VM.SelectedFace is not null)
            VM.FloodFill(new Point(
                VM.SelectedFace.W * VM.FaceZoom / 2.0,
                VM.SelectedFace.H * VM.FaceZoom / 2.0));
    }

    private void Brush_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (BrushCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            VM.BrushSize = int.Parse(tag);
    }

    private void Zoom_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ZoomCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            VM.FaceZoom = int.Parse(tag);
    }
}
