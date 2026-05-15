using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using Microsoft.Extensions.Logging;
using AresToys.App.Services.Wormholes;
using AresToys.App.Views;
using AresToys.Core.Pipeline;

namespace AresToys.App.Services.PipelineTasks;

/// <summary>"Create wormhole, smart". Detects the foreground Explorer window and:
/// <list type="bullet">
///   <item>If a single folder item is selected → create a wormhole for THAT folder, no dialog.</item>
///   <item>Otherwise (no selection, multi-select, file selected, no Explorer in foreground) →
///         open <see cref="NewWormholeDialog"/> so the user picks a folder.</item>
/// </list>
/// COM dance is the same shape as <c>CaptureSelectedExplorerFileTask</c>: dispatched on the
/// STA dispatcher, dynamic on Shell.Application, returns null on every "couldn't figure it out"
/// path so the caller has one branch.</summary>
public sealed class WormholeCreateTask : IPipelineTask
{
    public const string TaskId = "arestoys.wormhole-create";

    private readonly IWormholeWindowManager _manager;
    private readonly ILogger<WormholeCreateTask> _logger;

    public WormholeCreateTask(IWormholeWindowManager manager, ILogger<WormholeCreateTask> logger)
    {
        _manager = manager;
        _logger = logger;
    }

    public string Id => TaskId;
    public string DisplayName => "Create wormhole";
    public PipelineTaskKind Kind => PipelineTaskKind.PostCapture;

    public async Task ExecuteAsync(PipelineContext context, JsonNode? config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Tray-menu dismissal grace period — without it GetForegroundWindow returns AresToys's
        // popup instead of the Explorer window the user wants to act on.
        await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);

        var folder = await Application.Current.Dispatcher.InvokeAsync(ResolveExplorerSelectedFolder).Task.ConfigureAwait(true);
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
        {
            // Auto-create flow: title = folder name, no dialog. Mirrors the Explorer right-click
            // verb and the cold-start handler in App.HandleCreateWormhole.
            var title = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(title)) title = "Wormhole";
            try { await _manager.CreateAsync(title, folder, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WormholeCreateTask auto-create failed for {Folder}", folder);
            }
            return;
        }

        // Fallback: dialog. Has to run on the UI thread (modal Window).
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                var dlg = new NewWormholeDialog();
                if (dlg.ShowDialog() != true || dlg.Result is null) return;
                var choice = dlg.Result;
                _ = _manager.CreateAsync(choice.Title, choice.SourceFolder, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WormholeCreateTask dialog flow failed");
            }
        }).Task.ConfigureAwait(false);
    }

    /// <summary>Walk Shell.Application.Windows() to find the foreground Explorer's folder:
    /// either the single selected folder, or — when nothing is selected — the folder currently
    /// being viewed. Returns null when: no Explorer in foreground, multiple items selected,
    /// the selected item isn't a folder, or any COM call throws. The caller treats null as
    /// "open the dialog instead".
    ///
    /// We trust <c>Directory.Exists(item.Path)</c> rather than <c>item.IsFolder</c> alone:
    /// some shell namespace extensions (Quick Access pinned entries, OneDrive, mapped network
    /// drives) report IsFolder inconsistently across Windows versions, and a disk-level check
    /// is the ground truth we actually care about for creating a wormhole.</summary>
    private string? ResolveExplorerSelectedFolder()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return null;

        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null) return null;
            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell is null) return null;

            dynamic windows = shell.Windows();
            foreach (dynamic window in windows)
            {
                IntPtr hwnd;
                try { hwnd = new IntPtr((int)window.HWND); }
                catch { continue; }
                if (hwnd != foreground) continue;

                dynamic? document;
                try { document = window.Document; }
                catch { continue; }

                // Selection branch — preferred when present. Count + first-item capture in a
                // single pass; bail on multi-select.
                dynamic? items = null;
                try { items = document.SelectedItems(); } catch { items = null; }

                if (items is not null)
                {
                    int count = 0;
                    string? selectedPath = null;
                    foreach (dynamic item in items)
                    {
                        count++;
                        if (count > 1) return null; // multi-select bails to dialog
                        try { selectedPath = item.Path as string; } catch { }
                    }
                    if (count == 1 && !string.IsNullOrEmpty(selectedPath) && Directory.Exists(selectedPath))
                        return selectedPath;
                    if (count >= 1) return null; // selection present but not a folder → dialog
                }

                // Nothing selected → fall back to the currently-displayed folder. Matches user
                // mental model: "I'm inside the folder I want, just make a wormhole for it".
                try
                {
                    string? currentPath = document.Folder.Self.Path as string;
                    if (!string.IsNullOrEmpty(currentPath) && Directory.Exists(currentPath))
                        return currentPath;
                }
                catch { /* shell folder without a filesystem path (Quick Access, etc.) */ }
                return null;
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "WormholeCreateTask: Shell.Application introspection failed");
            return null;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
