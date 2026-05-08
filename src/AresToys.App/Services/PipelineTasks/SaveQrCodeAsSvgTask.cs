using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using AresToys.App.Services.Qr;
using AresToys.Core.Pipeline;

namespace AresToys.App.Services.PipelineTasks;

/// <summary>QR export as scalable vector. Same shape as <see cref="SaveQrCodeAsImageTask"/>
/// but writes the SVG document text instead of a PNG byte stream — useful when the QR is
/// going into a print job, a slide deck, or any context where pixel resolution matters.</summary>
public sealed class SaveQrCodeAsSvgTask : IPipelineTask
{
    public const string TaskId = "arestoys.save-qr-as-svg";

    private readonly ILogger<SaveQrCodeAsSvgTask> _logger;
    private readonly QrCodeService _qr;

    public SaveQrCodeAsSvgTask(ILogger<SaveQrCodeAsSvgTask> logger, QrCodeService qr)
    {
        _logger = logger;
        _qr = qr;
    }

    public string Id => TaskId;
    public string DisplayName => "Save QR as SVG";
    public PipelineTaskKind Kind => PipelineTaskKind.Both;

    public async Task ExecuteAsync(PipelineContext context, JsonNode? config, CancellationToken cancellationToken)
    {
        var text = ResolveText(context, config);
        if (string.IsNullOrEmpty(text)) { _logger.LogWarning("SaveQrCodeAsSvgTask: no text resolved; skipping"); return; }

        var svg = _qr.TryRenderSvg(text);
        if (string.IsNullOrEmpty(svg)) return;

        var configPath = (string?)config?["path"];
        var picked = !string.IsNullOrWhiteSpace(configPath)
            ? Environment.ExpandEnvironmentVariables(configPath)
            : await PickPathAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(picked)) { _logger.LogDebug("SaveQrCodeAsSvgTask: user cancelled"); return; }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(picked) ?? ".");
            await File.WriteAllTextAsync(picked, svg, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            context.Bag[PipelineBagKeys.LocalPath] = picked;
            context.Bag[PipelineBagKeys.FileExtension] = "svg";
            _logger.LogDebug("SaveQrCodeAsSvgTask: wrote {Chars} chars to {Path}", svg.Length, picked);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SaveQrCodeAsSvgTask: failed to write {Path}", picked);
        }
    }

    private static async Task<string?> PickPathAsync()
    {
        return await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var dlg = new SaveFileDialog
            {
                Title = "Save QR as SVG",
                Filter = "SVG image|*.svg|All files|*.*",
                FileName = "qr.svg",
                DefaultExt = ".svg",
                AddExtension = true,
            };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        });
    }

    private static string? ResolveText(PipelineContext context, JsonNode? config)
    {
        if (config?["text"] is { } textNode && textNode.GetValueKind() == System.Text.Json.JsonValueKind.String)
            return textNode.GetValue<string>();
        if (context.Bag.TryGetValue(PipelineBagKeys.UploadUrl, out var u) && u is string url) return url;
        if (context.Bag.TryGetValue(PipelineBagKeys.PayloadBytes, out var p) && p is byte[] bytes)
            return Encoding.UTF8.GetString(bytes);
        return null;
    }
}
