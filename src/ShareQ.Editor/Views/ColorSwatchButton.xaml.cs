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
        new(255, 220, 20, 60),
        new(255, 255, 99, 71),
        new(255, 255, 165, 0),
        new(255, 255, 215, 0),
        new(255, 50, 205, 50),
        new(255, 0, 200, 100),
        new(255, 70, 130, 180),
        new(255, 30, 144, 255),
        new(255, 138, 43, 226),
        new(255, 255, 105, 180),
        new(255, 139, 69, 19),
        new(255, 105, 105, 105),
        new(255, 0, 0, 0),
        new(255, 255, 255, 255),
        new(0, 0, 0, 0)
    ];

    private readonly Popup _popup = new();

    /// <summary>Set by the host (App.xaml.cs) before showing the editor; can be empty.</summary>
    public static IReadOnlyList<ShapeColor> CurrentRecents { get; set; } = [];

    /// <summary>Hook fired when a color is picked. Used by host to persist recents.</summary>
    public static Action<ShapeColor>? OnColorPicked { get; set; }

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
        var rootStack = new StackPanel { Margin = new Thickness(8), MinWidth = 220 };

        if (CurrentRecents.Count > 0)
        {
            var recentsLabel = new TextBlock { Text = "Recent", Foreground = Brushes.Gray, FontSize = 10, Margin = new Thickness(2, 0, 0, 4) };
            rootStack.Children.Add(recentsLabel);
            var recentsGrid = new UniformGrid { Columns = 8, Margin = new Thickness(0, 0, 0, 8) };
            foreach (var c in CurrentRecents)
            {
                recentsGrid.Children.Add(BuildSwatch(c));
            }
            rootStack.Children.Add(recentsGrid);
        }

        var paletteGrid = new UniformGrid { Columns = 5 };
        foreach (var c in Palette)
        {
            paletteGrid.Children.Add(BuildSwatch(c));
        }
        rootStack.Children.Add(paletteGrid);

        var hexRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        hexRow.Children.Add(new TextBlock { Text = "Hex:", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 6, 0) });
        var hexBox = new TextBox
        {
            Width = 120,
            Background = Brushes.Black,
            Foreground = Brushes.White,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 2, 4, 2)
        };
        hexBox.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter && TryParseHex(hexBox.Text, out var c))
            {
                SelectedColor = c;
                OnColorPicked?.Invoke(c);
                _popup.IsOpen = false;
            }
        };
        hexRow.Children.Add(hexBox);
        rootStack.Children.Add(hexRow);

        _popup.Child = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = rootStack
        };
        _popup.PlacementTarget = SwatchBorder;
        _popup.Placement = PlacementMode.Bottom;
        _popup.StaysOpen = false;
        _popup.IsOpen = true;
    }

    private Border BuildSwatch(ShapeColor c)
    {
        var preview = new Border
        {
            Width = 24, Height = 24, Margin = new Thickness(2),
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Background = c.IsTransparent ? Brushes.Transparent : new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B)),
            Cursor = Cursors.Hand,
            Tag = c
        };
        preview.MouseLeftButtonUp += (_, _) =>
        {
            SelectedColor = (ShapeColor)preview.Tag!;
            OnColorPicked?.Invoke(SelectedColor);
            if (_popup.IsOpen) _popup.IsOpen = false;
        };
        return preview;
    }

    private static bool TryParseHex(string? input, out ShapeColor color)
    {
        color = ShapeColor.Black;
        if (string.IsNullOrWhiteSpace(input)) return false;
        var s = input.Trim().TrimStart('#');
        try
        {
            if (s.Length == 6)
            {
                var r = Convert.ToByte(s[..2], 16);
                var g = Convert.ToByte(s[2..4], 16);
                var b = Convert.ToByte(s[4..6], 16);
                color = new ShapeColor(255, r, g, b);
                return true;
            }
            if (s.Length == 8)
            {
                var a = Convert.ToByte(s[..2], 16);
                var r = Convert.ToByte(s[2..4], 16);
                var g = Convert.ToByte(s[4..6], 16);
                var b = Convert.ToByte(s[6..8], 16);
                color = new ShapeColor(a, r, g, b);
                return true;
            }
        }
        catch (FormatException) { return false; }
        catch (OverflowException) { return false; }
        return false;
    }
}
