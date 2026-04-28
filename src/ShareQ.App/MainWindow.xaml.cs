using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ShareQ.App.ViewModels;
using Wpf.Ui.Controls;

namespace ShareQ.App;

public partial class MainWindow : FluentWindow
{
    private readonly MainWindowViewModel _vm;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = viewModel;
        viewModel.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainWindowViewModel.SelectedItemPayload):
                UpdatePreviewImage();
                break;
            case nameof(MainWindowViewModel.SelectedItem):
                FocusSelectedListBoxItem();
                break;
            case nameof(MainWindowViewModel.SelectedItemRtfBytes):
                Dispatcher.BeginInvoke(new Action(() => UpdateRtfPreview(_vm.SelectedItemRtfBytes)),
                    DispatcherPriority.ContextIdle);
                break;
            case nameof(MainWindowViewModel.SelectedItemHtml):
                Dispatcher.BeginInvoke(new Action(() => UpdateHtmlPreview(_vm.SelectedItemHtml)),
                    DispatcherPriority.ContextIdle);
                break;
        }
    }

    private void UpdateRtfPreview(byte[]? rtf)
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
            catch { /* malformed RTF — leave empty */ }
        }
        RtfPreviewBox.Document = doc;
    }

    private void UpdateHtmlPreview(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            HtmlPreviewBox.NavigateToString("<html><body></body></html>");
            return;
        }
        try { HtmlPreviewBox.NavigateToString(StripCfHtmlPreamble(html)); }
        catch { /* MSHTML may reject some payloads — render nothing rather than crash. */ }
    }

    private static string StripCfHtmlPreamble(string html)
    {
        if (string.IsNullOrEmpty(html)) return html;
        var startMarker = html.IndexOf("<!--StartFragment-->", StringComparison.OrdinalIgnoreCase);
        if (startMarker >= 0)
        {
            startMarker += "<!--StartFragment-->".Length;
            var endMarker = html.IndexOf("<!--EndFragment-->", startMarker, StringComparison.OrdinalIgnoreCase);
            if (endMarker < 0) endMarker = html.Length;
            return $"<html><head><meta charset=\"utf-8\">{DarkThemeStyle}</head><body>{html[startMarker..endMarker]}</body></html>";
        }
        var firstTag = html.IndexOf('<');
        if (firstTag > 0)
        {
            var head = html[..firstTag];
            if (head.Contains("Version:", StringComparison.OrdinalIgnoreCase)
                || head.Contains("StartHTML:", StringComparison.OrdinalIgnoreCase)
                || head.Contains("StartFragment:", StringComparison.OrdinalIgnoreCase))
            {
                return InjectCharsetMeta(html[firstTag..]);
            }
        }
        return InjectCharsetMeta(html);
    }

    private const string DarkThemeStyle =
        "<style>" +
        "html,body{background:#1E1E1E;color:#DDD;font-family:Segoe UI,sans-serif;font-size:13px;margin:8px;}" +
        "a{color:#7FB3FF;}" +
        "table,td,th{border-color:#444;}" +
        "</style>";

    /// <summary>WebBrowser/MSHTML defaults to the user's locale (Windows-1252 on Italian) when no
    /// charset is declared. CF_HTML payloads are UTF-8 by spec, so inject the meta tag and a dark
    /// theme stylesheet so the preview doesn't flash white.</summary>
    private static string InjectCharsetMeta(string html)
    {
        if (html.Contains("<meta charset", StringComparison.OrdinalIgnoreCase)) return html;
        var headIdx = html.IndexOf("<head", StringComparison.OrdinalIgnoreCase);
        if (headIdx >= 0)
        {
            var headClose = html.IndexOf('>', headIdx);
            if (headClose >= 0)
                return html[..(headClose + 1)] + "<meta charset=\"utf-8\">" + DarkThemeStyle + html[(headClose + 1)..];
        }
        var htmlIdx = html.IndexOf("<html", StringComparison.OrdinalIgnoreCase);
        if (htmlIdx >= 0)
        {
            var htmlClose = html.IndexOf('>', htmlIdx);
            if (htmlClose >= 0)
                return html[..(htmlClose + 1)] + $"<head><meta charset=\"utf-8\">{DarkThemeStyle}</head>" + html[(htmlClose + 1)..];
        }
        return $"<html><head><meta charset=\"utf-8\">{DarkThemeStyle}</head><body>{html}</body></html>";
    }

    /// <summary>Bring the selected row into view AND give it keyboard focus, so arrow-down/up
    /// navigation continues from the actually-selected item (not from the top of the list, which is
    /// where WPF's internal keyboard cursor parks itself after Items.Clear/Add).</summary>
    private void FocusSelectedListBoxItem()
    {
        var item = _vm.SelectedItem;
        if (item is null) return;
        // Container generation is async on virtualized lists — defer until layout settles.
        ItemsList.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            ItemsList.ScrollIntoView(item);
            if (ItemsList.ItemContainerGenerator.ContainerFromItem(item) is System.Windows.Controls.ListBoxItem lbi)
            {
                lbi.Focus();
            }
        });
    }

    private void UpdatePreviewImage()
    {
        var bytes = _vm.SelectedItemPayload;
        if (bytes is null || bytes.Length == 0)
        {
            PreviewImage.Source = null;
            return;
        }
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = new MemoryStream(bytes);
        bmp.EndInit();
        bmp.Freeze();
        PreviewImage.Source = bmp;
    }
}
