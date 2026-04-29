using System.Windows;
using System.Windows.Input;

namespace ShareQ.App.Windows;

public enum PinSource { Cancelled, Screen, Clipboard, File }

public partial class PinSourceChooserWindow : Window
{
    public PinSource Result { get; private set; } = PinSource.Cancelled;

    public PinSourceChooserWindow()
    {
        InitializeComponent();
        FromScreenButton.Click    += (_, _) => Pick(PinSource.Screen);
        FromClipboardButton.Click += (_, _) => Pick(PinSource.Clipboard);
        FromFileButton.Click      += (_, _) => Pick(PinSource.File);
        CancelButton.Click        += (_, _) => { DialogResult = false; Close(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; Close(); } };
    }

    private void Pick(PinSource s) { Result = s; DialogResult = true; Close(); }
}
