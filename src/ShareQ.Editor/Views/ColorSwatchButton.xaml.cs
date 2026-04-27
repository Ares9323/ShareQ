using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ShareQ.Editor.Model;

namespace ShareQ.Editor.Views;

public partial class ColorSwatchButton : UserControl
{
    private static readonly ShapeColor[] Palette =
    [
        new(255, 220, 20, 60),    // crimson
        new(255, 255, 99, 71),    // tomato
        new(255, 255, 165, 0),    // orange
        new(255, 255, 215, 0),    // gold
        new(255, 50, 205, 50),    // limegreen
        new(255, 0, 200, 100),    // emerald
        new(255, 70, 130, 180),   // steel blue
        new(255, 30, 144, 255),   // dodger
        new(255, 138, 43, 226),   // purple
        new(255, 255, 105, 180),  // pink
        new(255, 139, 69, 19),    // brown
        new(255, 105, 105, 105),  // gray
        new(255, 0, 0, 0),        // black
        new(255, 255, 255, 255),  // white
        new(0, 0, 0, 0)           // transparent
    ];

    private readonly Popup _popup = new();

    public static readonly DependencyProperty SelectedColorProperty = DependencyProperty.Register(
        nameof(SelectedColor),
        typeof(ShapeColor),
        typeof(ColorSwatchButton),
        new PropertyMetadata(ShapeColor.Red, OnSelectedColorChanged));

    public ColorSwatchButton()
    {
        InitializeComponent();
        UpdateBrush();
    }

    public ShapeColor SelectedColor
    {
        get => (ShapeColor)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ColorSwatchButton b) b.UpdateBrush();
    }

    private void UpdateBrush()
    {
        SwatchBrush.Color = SelectedColor.IsTransparent
            ? Colors.Transparent
            : Color.FromArgb(SelectedColor.A, SelectedColor.R, SelectedColor.G, SelectedColor.B);
    }

    private void OnClicked(object sender, MouseButtonEventArgs e)
    {
        var grid = new UniformGrid { Columns = 5, Margin = new Thickness(8) };
        foreach (var c in Palette)
        {
            var preview = new Border
            {
                Width = 28, Height = 28, Margin = new Thickness(4),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Background = c.IsTransparent
                    ? Brushes.Transparent
                    : new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B)),
                Cursor = Cursors.Hand,
                Tag = c
            };
            preview.MouseLeftButtonUp += (_, _) =>
            {
                SelectedColor = (ShapeColor)preview.Tag!;
                if (_popup.IsOpen) _popup.IsOpen = false;
            };
            grid.Children.Add(preview);
        }

        _popup.Child = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = grid
        };
        _popup.PlacementTarget = SwatchBorder;
        _popup.Placement = PlacementMode.Bottom;
        _popup.StaysOpen = false;
        _popup.IsOpen = true;
    }
}
