using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using ShareQ.App.ViewModels;
using ShareQ.Core.Domain;
using ShareQ.Storage.Settings;

namespace ShareQ.App.Windows;

public partial class PopupWindow : Window
{
    private readonly ISettingsStore _settings;
    private const string SizeWidthKey = "popup.size.width";
    private const string SizeHeightKey = "popup.size.height";
    private const string PreviewWidthKey = "popup.preview.width";
    private const string PositionLeftKey = "popup.position.left";
    private const string PositionTopKey  = "popup.position.top";
    private bool _sizeRestored;
    /// <summary>True when the previous session saved a position. Read by
    /// <see cref="PopupWindowController"/> so it can skip its cursor-repositioning logic and
    /// honor the user's last placement instead.</summary>
    public bool HasPersistedPosition { get; private set; }

    public PopupWindow(PopupWindowViewModel viewModel, ISettingsStore settings)
    {
        InitializeComponent();
        DataContext = viewModel;
        ViewModel = viewModel;
        _settings = settings;
        ShareQ.App.Services.DarkTitleBar.ApplyRoundedCorners(this);

        Loaded += OnLoaded;
        // No "hide on deactivate" — the popup now persists until the user explicitly closes it
        // (X button, Esc, or pasting an item via the AutoPaster path). Lets the user click into
        // other apps without losing the popup, e.g. to drag-drop content out.
        // PreviewKeyDown (tunneling) so the Window sees Ctrl+digits before the SearchBox swallows them.
        PreviewKeyDown += OnKeyDown;
        ResultsList.MouseDoubleClick += OnResultsListDoubleClick;
        SizeChanged += OnSizeChanged;
        // Save the user's position on every move so the next open lands at the same spot.
        // The popup is hidden (not destroyed) so LocationChanged fires only on real drags
        // initiated via DragHandle — exactly what we want to persist.
        LocationChanged += OnLocationChanged;
        // GridSplitter drags don't trigger the window's SizeChanged — listen on the preview pane
        // itself so we capture both window resizes and splitter drags.
        PreviewPane.SizeChanged += OnPreviewPaneSizeChanged;

        // Custom drag chrome (WindowStyle=None has no native title bar).
        DragHandle.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource != CloseButton)
            {
                try { DragMove(); } catch { /* DragMove throws if mouse already up */ }
            }
        };
        CloseButton.Click += (_, _) => Hide();

        // RichTextBox.Document and WebBrowser.NavigateToString aren't bindable, so we wire them up
        // imperatively when the VM publishes new preview bytes/html.
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Focus the popup itself (not the SearchBox) so number-row digits, arrows and Enter go
        // straight to the keyboard handler instead of typing into the search field. Ctrl+F is
        // the explicit way to drop into search.
        Focus();
        // Pre-navigate the WebBrowser to a dark blank page so MSHTML doesn't flash white about:blank
        // before the first real preview lands.
        try { HtmlPreviewBox.NavigateToString(WrapWithCharset(string.Empty)); }
        catch { /* MSHTML may not be initialized yet — first real navigation will set the bg */ }
        try
        {
            var w = await _settings.GetAsync(SizeWidthKey, CancellationToken.None).ConfigureAwait(true);
            var h = await _settings.GetAsync(SizeHeightKey, CancellationToken.None).ConfigureAwait(true);
            if (w is not null && double.TryParse(w, NumberStyles.Float, CultureInfo.InvariantCulture, out var width)
                && h is not null && double.TryParse(h, NumberStyles.Float, CultureInfo.InvariantCulture, out var height))
            {
                Width = Math.Max(MinWidth, width);
                Height = Math.Max(MinHeight, height);
            }

            var pw = await _settings.GetAsync(PreviewWidthKey, CancellationToken.None).ConfigureAwait(true);
            if (pw is not null && double.TryParse(pw, NumberStyles.Float, CultureInfo.InvariantCulture, out var previewWidth))
            {
                PreviewColumn.Width = new GridLength(Math.Max(PreviewColumn.MinWidth, previewWidth), GridUnitType.Pixel);
            }

            // Restore last position if we saved one. Validate against the virtual screen so a
            // disconnected monitor between sessions doesn't open the window off-screen — fall
            // back to the controller's cursor-positioning logic in that case.
            var lx = await _settings.GetAsync(PositionLeftKey, CancellationToken.None).ConfigureAwait(true);
            var ly = await _settings.GetAsync(PositionTopKey,  CancellationToken.None).ConfigureAwait(true);
            if (lx is not null && double.TryParse(lx, NumberStyles.Float, CultureInfo.InvariantCulture, out var savedLeft)
                && ly is not null && double.TryParse(ly, NumberStyles.Float, CultureInfo.InvariantCulture, out var savedTop)
                && IsOnVirtualScreen(savedLeft, savedTop))
            {
                Left = savedLeft;
                Top  = savedTop;
                HasPersistedPosition = true;
            }
        }
        catch { /* settings unavailable — keep defaults */ }
        _sizeRestored = true;
    }

    private static bool IsOnVirtualScreen(double left, double top)
    {
        var rect = new Rect(left, top, 40, 40);
        var virt = new Rect(
            SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        return virt.IntersectsWith(rect);
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (!_sizeRestored) return;
        var l = Left.ToString(CultureInfo.InvariantCulture);
        var t = Top.ToString(CultureInfo.InvariantCulture);
        _ = _settings.SetAsync(PositionLeftKey, l, sensitive: false, CancellationToken.None);
        _ = _settings.SetAsync(PositionTopKey,  t, sensitive: false, CancellationToken.None);
        HasPersistedPosition = true;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Skip writes during the initial restore + WPF layout passes; only persist real user resizes.
        if (!_sizeRestored) return;
        var w = ActualWidth.ToString(CultureInfo.InvariantCulture);
        var h = ActualHeight.ToString(CultureInfo.InvariantCulture);
        _ = _settings.SetAsync(SizeWidthKey, w, sensitive: false, CancellationToken.None);
        _ = _settings.SetAsync(SizeHeightKey, h, sensitive: false, CancellationToken.None);
    }

    private void OnPreviewPaneSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_sizeRestored) return;
        if (!e.WidthChanged) return;
        // Persist the preview column's current pixel width so the splitter position is restored on
        // next open. The list column flexes via "*" so we don't store it.
        _ = _settings.SetAsync(PreviewWidthKey,
            PreviewPane.ActualWidth.ToString(CultureInfo.InvariantCulture),
            sensitive: false,
            CancellationToken.None);
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
            HtmlPreviewBox.NavigateToString(WrapWithCharset(string.Empty));
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

    /// <summary>Right-clicking a row should both select it and open its context menu. WPF
    /// doesn't auto-select on right-click, so the move-to command would otherwise act on
    /// whichever row was previously selected. Setting SelectedItem on the ListBox triggers
    /// the VM's selection binding, then ContextMenu opens normally on the click bubble.</summary>
    private void OnItemRowPreviewRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.Border b) return;
        if (b.DataContext is ItemRowViewModel row)
        {
            ResultsList.SelectedItem = row;
        }
    }

    /// <summary>Cycle the active category tab by <paramref name="delta"/> (+1 = next, -1 = prev).
    /// After applying the switch, RefreshAsync runs in the VM and selects the first row of the
    /// new category — ScrollSelectedIntoView keeps it visible. Wraps around at both ends.</summary>
    private async void SwitchCategory(int delta)
    {
        var tabs = ViewModel.Categories;
        if (tabs.Count == 0) return;
        var current = -1;
        for (var i = 0; i < tabs.Count; i++) if (tabs[i].IsActive) { current = i; break; }
        if (current < 0) current = 0;
        var next = (current + delta + tabs.Count) % tabs.Count;
        ViewModel.SelectCategoryCommand.Execute(tabs[next].Name);

        // RefreshAsync inside the VM reloads Rows; wait one dispatcher tick so the new rows
        // are materialized before we drop the focus on the first one.
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
        if (ViewModel.Rows.Count > 0)
        {
            ViewModel.SelectedRow = ViewModel.Rows[0];
            ScrollSelectedIntoView();
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
            case Key.Right:
            case Key.Left:
                // Sideways arrows cycle through the category tab strip — Right = next, Left =
                // previous. Skip when the search box has focus so the user can still move the
                // caret inside their query. After the switch, the VM auto-selects the first
                // visible row (see SwitchCategory below).
                if (!SearchBox.IsKeyboardFocused)
                {
                    SwitchCategory(e.Key == Key.Right ? 1 : -1);
                    e.Handled = true;
                }
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
            case Key.Delete:
                // Don't hijack Delete inside the SearchBox — let it edit characters there. Outside
                // the search box (most of the popup) it deletes the selected item.
                if (!SearchBox.IsKeyboardFocused)
                {
                    ViewModel.DeleteSelectedCommand.Execute(null);
                    e.Handled = true;
                }
                break;
            default:
                // Ctrl+F focuses the searchbox and selects all text so the user can start typing
                // immediately (matches the convention from browsers, IDEs, etc).
                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.F)
                {
                    SearchBox.Focus();
                    SearchBox.SelectAll();
                    e.Handled = true;
                    break;
                }
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
