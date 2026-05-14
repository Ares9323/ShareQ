using System.IO;
using System.Windows;
using AresToys.App.Services.Wormholes;
using Microsoft.Win32;

namespace AresToys.App.Views;

public partial class NewWormholeDialog : Window
{
    private bool _loaded;

    public NewWormholeDialog()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _loaded = true;
            TitleBox.Focus();
            TitleBox.SelectAll();
        };
    }

    /// <summary>Result of an OK confirmation. Null if the dialog was cancelled. The caller hands
    /// this off to <see cref="IWormholeWindowManager"/> which materialises the record.</summary>
    public NewWormholeChoice? Result { get; private set; }

    private void OnKindChanged(object sender, RoutedEventArgs e)
    {
        if (PortalFolderRow is null) return; // event fires during XAML init before the field is wired
        var portal = PortalRadio.IsChecked == true;
        PortalFolderRow.Visibility = portal ? Visibility.Visible : Visibility.Collapsed;

        // Auto-open the folder picker the first time the user lands on Portal — typing a path
        // by hand was error-prone (escaping, trailing slashes, missing quotes) and there's no
        // real benefit to the textbox-first workflow. Only fires after Loaded so the initial
        // Checked event on DataRadio (during XAML init) doesn't trigger anything.
        if (portal && _loaded && string.IsNullOrWhiteSpace(FolderBox.Text))
        {
            // Dispatcher.BeginInvoke so the radio button finishes its visual update before the
            // modal folder dialog opens — otherwise the radio dot can render a frame late and
            // look unresponsive.
            Dispatcher.BeginInvoke(new Action(() => OnBrowseClicked(this, new RoutedEventArgs())),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void OnBrowseClicked(object sender, RoutedEventArgs e)
    {
        // .NET 8+ ships Microsoft.Win32.OpenFolderDialog directly in WPF — no WinForms interop
        // needed and no third-party folder picker. Initial directory defaults to the user's
        // Desktop, matching Portals' "Default Folder Picker Folder" behaviour out of the box.
        var dlg = new OpenFolderDialog
        {
            Title = "Choose the folder this wormhole will mirror",
            InitialDirectory = string.IsNullOrWhiteSpace(FolderBox.Text)
                ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                : FolderBox.Text,
        };
        if (dlg.ShowDialog(this) == true)
        {
            FolderBox.Text = dlg.FolderName;
            // Auto-fill the title from the folder name when the user hasn't typed anything
            // custom yet (still on the default). Matches the Portals "Use Custom Display Name"
            // workflow — most users want the folder name as the title and this saves a
            // keystroke.
            if (TitleBox.Text == "Wormhole")
                TitleBox.Text = Path.GetFileName(dlg.FolderName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
    }

    private void OnOkClicked(object sender, RoutedEventArgs e)
    {
        var title = (TitleBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(title))
        {
            MessageBox.Show(this, "Title can't be empty.", "AresToys",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            TitleBox.Focus();
            return;
        }

        if (PortalRadio.IsChecked == true)
        {
            var folder = (FolderBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                MessageBox.Show(this, "Pick an existing folder for the Portal wormhole to mirror.",
                    "AresToys", MessageBoxButton.OK, MessageBoxImage.Warning);
                FolderBox.Focus();
                return;
            }
            Result = new NewWormholeChoice(title, WormholeKind.Portal, folder);
        }
        else
        {
            Result = new NewWormholeChoice(title, WormholeKind.Data, null);
        }

        DialogResult = true;
        Close();
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

/// <summary>Captures the user's choice from <see cref="NewWormholeDialog"/>. The manager turns
/// this into a persisted <see cref="WormholeRecord"/> + a live window.</summary>
public sealed record NewWormholeChoice(string Title, WormholeKind Kind, string? SourceFolder);
