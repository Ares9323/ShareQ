using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using ShareQ.App.ViewModels;
using ShareQ.Core.Domain;

namespace ShareQ.App.Windows;

public partial class PopupWindow : Window
{
    public PopupWindow(PopupWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        ViewModel = viewModel;

        Loaded += (_, _) => SearchBox.Focus();
        Deactivated += (_, _) => Hide();
        // PreviewKeyDown (tunneling) so the Window sees Ctrl+digits before the SearchBox swallows them.
        PreviewKeyDown += OnKeyDown;
        ResultsList.MouseDoubleClick += OnResultsListDoubleClick;

        // RichTextBox.Document and WebBrowser.NavigateToString aren't bindable, so we wire them up
        // imperatively when the VM publishes new preview bytes/html.
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PopupWindowViewModel.PreviewRtfBytes):
                // Defer until after layout: by then the Visibility binding driven by IsRtfPreview has
                // flipped the RichTextBox to Visible, so loading the document actually paints.
                Dispatcher.BeginInvoke(
                    new Action(() => LoadRtfPreview(ViewModel.PreviewRtfBytes)),
                    DispatcherPriority.ContextIdle);
                break;
            case nameof(PopupWindowViewModel.PreviewHtml):
                Dispatcher.BeginInvoke(
                    new Action(() => LoadHtmlPreview(ViewModel.PreviewHtml)),
                    DispatcherPriority.ContextIdle);
                break;
        }
    }

    private void LoadRtfPreview(byte[]? rtf)
    {
        var doc = new FlowDocument();
        if (rtf is { Length: > 0 })
        {
            try
            {
                using var ms = new MemoryStream(rtf);
                var range = new TextRange(doc.ContentStart, doc.ContentEnd);
                range.Load(ms, DataFormats.Rtf);
            }
            catch
            {
                // Malformed RTF — leave the document empty.
            }
        }
        RtfPreviewBox.Document = doc;
    }

    private void LoadHtmlPreview(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            HtmlPreviewBox.NavigateToString("<html><body></body></html>");
            return;
        }
        // Clipboard HTML usually arrives wrapped in CF_HTML metadata (Version: ... StartHTML: ...);
        // strip everything up to <!--StartFragment--> when present so the WebBrowser only sees the
        // visible markup.
        var trimmed = StripCfHtmlPreamble(html);
        try { HtmlPreviewBox.NavigateToString(trimmed); }
        catch { /* Some HTML payloads break MSHTML — render nothing rather than crash. */ }
    }

    private static string StripCfHtmlPreamble(string html)
    {
        if (string.IsNullOrEmpty(html)) return html;

        // Standard CF_HTML wraps the visible content between <!--StartFragment--> and
        // <!--EndFragment-->. Use those when present.
        var startMarker = html.IndexOf("<!--StartFragment-->", StringComparison.OrdinalIgnoreCase);
        if (startMarker >= 0)
        {
            startMarker += "<!--StartFragment-->".Length;
            var endMarker = html.IndexOf("<!--EndFragment-->", startMarker, StringComparison.OrdinalIgnoreCase);
            if (endMarker < 0) endMarker = html.Length;
            return WrapWithCharset(html[startMarker..endMarker]);
        }

        // Some sources (Google Docs in some browser combinations) emit only the CF_HTML key:value
        // preamble (Version:, StartHTML:, StartFragment:, SourceURL:, …) without the comment
        // markers. Strip everything before the first '<' that opens a real tag when a known
        // preamble key is present.
        var firstTag = html.IndexOf('<');
        if (firstTag > 0)
        {
            var preamble = html[..firstTag];
            if (preamble.Contains("Version:", StringComparison.OrdinalIgnoreCase)
                || preamble.Contains("StartHTML:", StringComparison.OrdinalIgnoreCase)
                || preamble.Contains("StartFragment:", StringComparison.OrdinalIgnoreCase))
            {
                return InjectCharsetMeta(html[firstTag..]);
            }
        }

        return InjectCharsetMeta(html);
    }

    private static string WrapWithCharset(string innerHtml)
        => $"<html><head><meta charset=\"utf-8\">{DarkThemeStyle}</head><body>{innerHtml}</body></html>";

    /// <summary>Dark CSS injected into all rendered preview HTML so the WebBrowser doesn't flash a
    /// blinding white page. Explicit colors in the source still win via cascade specificity.</summary>
    private const string DarkThemeStyle =
        "<style>" +
        "html,body{background:#1E1E1E;color:#DDD;font-family:Segoe UI,sans-serif;font-size:13px;margin:8px;}" +
        "a{color:#7FB3FF;}" +
        "table,td,th{border-color:#444;}" +
        "</style>";

    /// <summary>WebBrowser/MSHTML defaults to the user's locale (Windows-1252 on Italian) when no
    /// charset is declared. CF_HTML payloads are UTF-8 by spec, so insert a meta tag so the
    /// rendering engine doesn't double-decode and produce "Â", "â€™" etc.</summary>
    private static string InjectCharsetMeta(string html)
    {
        if (html.Contains("<meta charset", StringComparison.OrdinalIgnoreCase)) return html;
        var headIdx = html.IndexOf("<head", StringComparison.OrdinalIgnoreCase);
        if (headIdx >= 0)
        {
            var headClose = html.IndexOf('>', headIdx);
            if (headClose >= 0)
            {
                return html[..(headClose + 1)] + "<meta charset=\"utf-8\">" + DarkThemeStyle + html[(headClose + 1)..];
            }
        }
        var htmlIdx = html.IndexOf("<html", StringComparison.OrdinalIgnoreCase);
        if (htmlIdx >= 0)
        {
            var htmlClose = html.IndexOf('>', htmlIdx);
            if (htmlClose >= 0)
            {
                return html[..(htmlClose + 1)] + $"<head><meta charset=\"utf-8\">{DarkThemeStyle}</head>" + html[(htmlClose + 1)..];
            }
        }
        return WrapWithCharset(html);
    }

    public PopupWindowViewModel ViewModel { get; }

    public event EventHandler<long>? PasteRequested;
    public event EventHandler<long>? OpenInEditorRequested;

    private void OnResultsListDoubleClick(object? sender, MouseButtonEventArgs e)
    {
        if (ViewModel.SelectedRow is { } row && row.Kind == ItemKind.Image)
        {
            Hide();
            OpenInEditorRequested?.Invoke(this, row.Id);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Hide();
                e.Handled = true;
                break;
            case Key.Down:
                ViewModel.MoveSelectionCommand.Execute(1);
                ScrollSelectedIntoView();
                e.Handled = true;
                break;
            case Key.Up:
                ViewModel.MoveSelectionCommand.Execute(-1);
                ScrollSelectedIntoView();
                e.Handled = true;
                break;
            case Key.Enter:
                if (ViewModel.SelectedRow is { } row)
                {
                    // Don't Hide() here — the popup must remain the foreground window so AutoPaster
                    // can call SetForegroundWindow on the target without tripping Win32's
                    // anti-focus-stealing rules. The popup hides itself via Deactivated when the
                    // target window gets focus.
                    PasteRequested?.Invoke(this, row.Id);
                }
                e.Handled = true;
                break;
            default:
                // Ctrl+P toggles pin on the selected row. Plain "P" can't be used because the search
                // box always holds focus and P is a valid search character.
                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.P)
                {
                    ViewModel.TogglePinSelectedCommand.Execute(null);
                    e.Handled = true;
                    break;
                }
                // Ctrl+1..9: quick-paste row N (1-indexed). Same Hide-after-restore reasoning as Enter.
                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
                    && e.Key >= Key.D1 && e.Key <= Key.D9)
                {
                    var idx = e.Key - Key.D1;
                    if (idx >= 0 && idx < ViewModel.Rows.Count)
                    {
                        var quick = ViewModel.Rows[idx];
                        PasteRequested?.Invoke(this, quick.Id);
                    }
                    e.Handled = true;
                }
                break;
        }
    }

    private void ScrollSelectedIntoView()
    {
        if (ViewModel.SelectedRow is null) return;
        ResultsList.ScrollIntoView(ViewModel.SelectedRow);
    }
}
