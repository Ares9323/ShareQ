using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ShareQ.Editor.Model;

namespace ShareQ.Editor.Views;

public partial class ColorSwatchButton : UserControl
{
    /// <summary>Set by the host (App.xaml.cs) before showing the editor; can be empty.
    /// Pre-populates the Recent palette inside <see cref="ColorPickerWindow"/> so the user
    /// sees their previous picks the moment they open it from any swatch.</summary>
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
        // Click on a swatch button opens the full ColorPickerWindow straight away. The old
        // dropdown (recents grid + palette + hex box + "Custom…" button) was redundant since
        // the picker window itself already exposes all those affordances. Recents stay
        // pre-loaded via CurrentRecents.
        OpenCustomPicker();
    }

    /// <summary>Hook the host can wire to provide a "pick from canvas / pick from screen" callback.
    /// When invoked, the host should hide the picker, run its eyedropper flow, then call the supplied
    /// continuation with the sampled color (or null on cancel). Returning null means: host doesn't
    /// support eyedropper for this swatch, hide the eyedropper button.</summary>
    public static Func<Action<ShapeColor?>, IDisposable?>? EyedropperHandler { get; set; }

    private void OpenCustomPicker()
    {
        // Snapshot the pre-edit value so a Cancel can roll back the live previews. Every
        // ColorChanged tick during the picker session writes through SelectedColor, which any
        // bound consumer (the editor wires SelOutlineSwatch / SelFillSwatch / SelTextColorSwatch
        // to a value-changed listener that LiveReplaceShape's the selection) already picks up
        // — that's how we get real-time recolouring of the selected shape.
        var originalColor = SelectedColor;

        var dlg = new ColorPickerWindow(SelectedColor)
        {
            Owner = Window.GetWindow(this)
        };
        dlg.ColorChanged += (_, c) =>
        {
            SelectedColor = c;
            OnColorPicked?.Invoke(c);
        };
        // Eyedropper is the only flow that needs the picker out of the way temporarily —
        // Hide() + Show() on a ShowDialog'd window keeps the modal loop alive (visibility
        // is independent of the dialog state machine), so the same dance works whether the
        // picker is modal or not.
        dlg.EyedropperRequested += (_, _) =>
        {
            var handler = EyedropperHandler;
            if (handler is null) return;
            dlg.Hide();
            handler(c =>
            {
                if (c is not null) dlg.ApplySampledColor(c);
                dlg.Show();
            });
        };
        // ShowDialog (modal) — required because OnOkClicked sets DialogResult, which is only
        // valid for modal dialogs. Bonus: blocks the swatch button until the user commits or
        // cancels, mirroring the Theme tab's color-picker flow in MainWindow.
        if (dlg.ShowDialog() == true)
        {
            SelectedColor = dlg.PickedColor;
            OnColorPicked?.Invoke(dlg.PickedColor);
        }
        else if (!SelectedColor.Equals(originalColor))
        {
            // Cancel / Esc — wind back the previews so the canvas matches the pre-edit state.
            SelectedColor = originalColor;
            OnColorPicked?.Invoke(originalColor);
        }
    }
}
