using System.Windows;
using ShareQ.App.Services;

namespace ShareQ.App.Views;

/// <summary>Tiny modal that asks for a URL before the webpage-capture pipeline runs. Kept as a
/// separate dialog (not a generic input box) so the placeholder text + the explanation about
/// login-walls live next to the field instead of being passed in by every caller.</summary>
public partial class WebpageUrlDialog : Window
{
    public WebpageUrlDialog(string? initialUrl = null)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        if (!string.IsNullOrWhiteSpace(initialUrl)) UrlBox.Text = initialUrl;
        Loaded += (_, _) =>
        {
            UrlBox.Focus();
            UrlBox.SelectAll();
        };
    }

    public string Url { get; private set; } = string.Empty;

    private void OnOkClicked(object sender, RoutedEventArgs e)
    {
        var raw = UrlBox.Text.Trim();
        if (string.IsNullOrEmpty(raw)) return;
        // Auto-prefix scheme so the user can type "example.com" — WebView2 rejects schemeless input.
        if (!raw.Contains("://", StringComparison.Ordinal)) raw = "https://" + raw;
        Url = raw;
        DialogResult = true;
        Close();
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
