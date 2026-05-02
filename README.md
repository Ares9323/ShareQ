# ShareQ

> Modern clipboard + screenshot tool for Windows, unifying the strengths of CopyQ and ShareX into a single app.

**Status:** Alpha. Builds and runs. No installer / packaged release yet — clone + `dotnet run`.

---

## What ShareQ is

ShareQ is a Windows desktop application that brings together two of the most-used productivity tools in the screenshot/clipboard space — [CopyQ](https://github.com/hluk/CopyQ) and [ShareX](https://github.com/ShareX/ShareX) — under a single modern UI built on .NET 10 and WPF.

The core idea: **everything you copy or capture is part of the same searchable history**. A clipboard text entry, a screenshot, and a freshly-generated share link all live as items in one timeline — same store, same search, same browser.

## What it aims to fix

- **CopyQ has a powerful clipboard engine but a UI that feels stuck in the early 2000s.** ShareQ keeps the engine, replaces the UI with a clean Fluent-style WPF design.
- **ShareX has every feature imaginable, and that's the problem.** Hundreds of uploaders, dozens of effects, a dense forest of options. ShareQ keeps the proven backend (capture, recorder, image editor logic, upload pipeline) and trims the UI to the essentials, with everything else moved behind an opt-in plugin system.
- **The ShareX image editor has known UX pain points** (tool color leaking into already-drawn objects, awkward color picker, cramped toolbar). ShareQ rebuilds the editor with a clear separation between "next-object color" (global swatches) and "selected-object color" (per-object property panel), a proper color picker with palette/recents/eyedropper, and a logically grouped toolbar.
- **Windows' native clipboard history is too shallow, ShareX's is screenshot-only, CopyQ's is clipboard-only.** ShareQ unifies them into a single browsable timeline.

## What works today

The app boots, hosts a tray icon, registers global hotkeys, and the following flows are complete end-to-end:

**Capture**
- Region capture (Win+Shift+S) with darkened overlay + window snap
- Fullscreen / per-monitor / last-region (tray menu)
- Screen recording → `.mp4` (Shift+PrtScn) and `.gif` (Ctrl+Shift+PrtScn) via FFmpeg
- Screen color picker / color sampler (hex/RGB/HSB/CMYK/decimal/linear/BGRA copy formats)
- Pin-to-screen (always-on-top thumbnail with drag + wheel zoom)

**Clipboard**
- Persistent SQLite history with FTS5 search + DPAPI-encrypted payloads
- CopyQ-style popup (Win+V) with custom categories, pin, search header, filter dropdown, vertical toolbar, geometry persistence, Ctrl+1-9 quick-paste

**Editor**
- WPF-based, ShareX-inspired — Rectangle, Ellipse, Freehand (with smooth/end-arrow), Text (drag-to-draw + wrap), Step counter, Image, Pixelate, Crop
- Outer-aligned outlines (EvenOdd ring geometry)
- Color picker with palette + recents + live preview + eyedropper
- Clipboard round-trip (Ctrl+C/X/V) preserves shapes as editable objects in-process, falls back to raster for other apps

**Uploaders** (10 bundled)
| Uploader | Auth | Capability |
|---|---|---|
| Catbox | none | Any file |
| Uguu.se | none | Any file (~3h expiry) |
| paste.rs | none | Text |
| Imgur (anonymous) | bundled Client ID | Image |
| ImgBB | user API key | Image |
| Pastebin | user API key | Text |
| GitHub Gist | user PAT | Text |
| OneDrive | OAuth (Azure AD v2) | Any file |
| Google Drive | OAuth | Any file |
| Dropbox | OAuth | Any file |

Plus a declarative `.sxcu` engine that loads ShareX-compatible JSON uploader files dropped into `%LOCALAPPDATA%\ShareQ\custom-uploaders\` for the long tail. Optional file-association toggle in Settings registers ShareQ as the default opener for `.sxcu` files in Explorer (per-user, no admin), so double-clicking a `.sxcu` from a website triggers an import-confirmation dialog.

**Pipeline & workflows**
- All capture / clipboard / upload flows run as composable pipelines (named "workflows"). Steps are user-editable in Settings → Hotkeys & workflows: add / remove / reorder / disable.
- Hotkey rebinder via low-level keyboard hook (handles Win+V, Win+Shift+S etc. that `RegisterHotKey` can't bind).
- Built-in profiles: region capture, screen recording, color picker/sampler, pin to screen, manual upload, upload clipboard text, open clipboard, open launcher.

**Other**
- Categories (CopyQ-style) with Move/Copy/Delete and per-category clear
- Settings backup / restore (JSON export / import)
- Themes with WPF-UI v4 brush overrides + custom Surface1/2/3 + Foreground/AccentForeground
- Launcher overlay (MaxLauncher-inspired): F1-F10 strip + 10 numeric tabs × 30 QWERTY cells, drag-and-drop assignment, search-as-you-type
- Autostart toggle (HKCU\Run, no admin)

## What's still missing

See [`docs/Improvements.md`](docs/Improvements.md) for the full feature-parity tracker against ShareX. Headline gaps:

- **No installer / no auto-update yet.** Velopack packaging + GitHub Releases auto-update is the M7 milestone — not done.
- **No image effects framework** (resize / blur / sepia / watermark / borders) — pencilled in as the next major editor pass.
- **No SharedFolder / FTP / SFTP / S3 / Azure / B2 uploaders** — backlog (FTP+SharedFolder are next; cloud-storage providers come after).
- **No OCR / scrolling capture / Explorer context menu / image combiner / hash checker / metadata viewer** — backlog.
- **Bundled OAuth client IDs aren't shipped** in the public source. Maintainers create `src/ShareQ.Uploaders/Secrets.Local.cs` with their own credentials (gitignored). End users of a public release get zero-friction sign-in; users building from source themselves will see "isn't configured in this build" until they either drop in their own keys or paste credentials in the Configure dialog.
- **No i18n** — UI is English only.
- **No CLI / scripting interface** — everything runs through hotkeys + workflows.

## Tech stack

- .NET 10, C# (Windows-only)
- WPF + [WPF-UI](https://github.com/lepoco/wpfui) for Fluent/Mica styling
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) for MVVM
- [Microsoft.Data.Sqlite](https://learn.microsoft.com/dotnet/standard/data/sqlite/) with WAL + FTS5 + DPAPI-encrypted payloads
- DXGI Desktop Duplication + `Windows.Graphics.Capture` for screen capture
- FFmpeg (auto-downloaded on first use) for screen recording
- [QRCoder](https://github.com/codebude/QRCoder) for QR generation
- [Velopack](https://github.com/velopack/velopack) (planned) for installer/portable packaging and auto-update via GitHub Releases

## Roadmap

| Milestone | Scope | Status |
|-----------|-------|--------|
| **M0** | Solution scaffold, CI green, WPF app opens with tray | ✅ done |
| **M1** | Clipboard hook + storage + popup window with FTS search | ✅ done |
| **M2** | Hotkeys + region capture + base pipeline | ✅ done |
| **M3** | Image editor (port + UX fixes) | ✅ done (image effects framework deferred) |
| **M4** | Plugin loader + Imgur uploader + upload pipeline | ✅ done (replaced DLL plugin loader with bundled-class architecture + `.sxcu` for the long tail) |
| **M5** | OneDrive uploader + main window timeline + plugin manager UI | ✅ done (extended to OneDrive + GoogleDrive + Dropbox + 7 more) |
| **M6** | Screen recorder + Beautify + Watermark/Step counter | 🟡 recorder done; Beautify / Watermark deferred to image-effects pass |
| **M7** | Velopack packaging + GitHub auto-update + release v0.1.0 | ⚪ not started |

After M7, the next themed passes (rough order):
1. Image effects framework (resize / blur / borders / sepia / watermark)
2. SharedFolder + FTP/FTPS/SFTP uploaders
3. Cloud storage uploaders (S3 / Azure Blob / Backblaze B2 / Google Cloud / Cloudflare R2)
4. OCR (Windows OCR API or Tesseract)
5. Scrolling capture
6. Explorer context menu integration
7. CLI for scripting
8. i18n

See [`docs/Improvements.md`](docs/Improvements.md) for the granular feature-parity checklist.

## Building

Requirements: .NET 10 SDK, Windows 10 / 11.

```powershell
git clone https://github.com/Ares9323/ShareQ
cd ShareQ
dotnet build
dotnet run --project src/ShareQ.App
```

For the OAuth uploaders (OneDrive / Google Drive / Dropbox / Imgur user-mode) to work without per-user setup, register apps with each provider following [`docs/RegisterAppGuide.md`](docs/RegisterAppGuide.md), then create `src/ShareQ.Uploaders/Secrets.Local.cs` (gitignored) with the constants documented inside `src/ShareQ.Uploaders/Secrets.cs`. Without `Secrets.Local.cs` the OAuth uploaders surface "isn't configured in this build" — the rest of ShareQ runs fine.

User data lives at `%LOCALAPPDATA%\ShareQ\` (SQLite + custom-uploaders + recordings). Delete that folder to reset to defaults.

## Acknowledgements

ShareQ stands directly on the shoulders of two GPL-3 projects, and reuses substantial portions of one of them:

- **[ShareX](https://github.com/ShareX/ShareX)** — the bulk of ShareQ's backend (capture, image editor, screen recorder, image effects, uploaders) is being ported from ShareX's modular libraries. Original authors and maintainers retain credit; ShareQ is a derivative work and is itself GPL-3-or-later.
- **[CopyQ](https://github.com/hluk/CopyQ)** — used as conceptual reference for the clipboard manager experience: history model, item-plugin architecture, popup-on-hotkey workflow.
- **[MaxLauncher](https://maxlauncher.sourceforge.io/)** — inspired ShareQ's launcher overlay: the keyboard-centric grid of mappable cells (F1-F10 global strip + 10 numeric tabs × 30 QWERTY keys), drag-and-drop assignment of files / shortcuts / folders, and the search-as-you-type filter that hides non-matching tabs and cells.

The licensing of both upstream projects (GPL-3-or-later) carries through to ShareQ. See [`LICENSE`](LICENSE).

## License

[GPL-3.0-or-later](LICENSE), inherited from both upstream projects.

## Contributing

The project is in early alpha and the API surface is still moving. Issues + bug reports are welcome; for code contributions please open an issue first to discuss scope before sending a PR.
