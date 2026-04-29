using System.Windows;
using System.Windows.Input;

namespace ShareQ.App.Windows;

/// <summary>
/// Simple modal prompt for workflow name (used by Add and Rename in Settings → Workflows). Returns
/// the trimmed name on OK, null on Cancel. Enter triggers OK, Esc triggers Cancel — same UX as
/// the rest of ShareQ's small modal dialogs.
/// </summary>
public partial class WorkflowNameDialog : Window
{
    public WorkflowNameDialog(string title, string initial)
    {
        InitializeComponent();
        TitleText.Text = title;
        NameTextBox.Text = initial;
        NameTextBox.SelectAll();
        Loaded += (_, _) => NameTextBox.Focus();
        OkButton.Click += (_, _) => Commit();
        CancelButton.Click += (_, _) => { DialogResult = false; Close(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Commit(); e.Handled = true; }
            else if (e.Key == Key.Escape) { DialogResult = false; Close(); e.Handled = true; }
        };
    }

    public string ResultName { get; private set; } = string.Empty;

    private void Commit()
    {
        var v = (NameTextBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(v)) return; // refuse empty; user fixes it or cancels
        ResultName = v;
        DialogResult = true;
        Close();
    }
}
