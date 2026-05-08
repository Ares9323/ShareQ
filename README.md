# AresToys

> Modern clipboard + screenshot tool for Windows, unifying the strengths of CopyQ and ShareX into a single app.

**Status:** Alpha. Builds and runs. Velopack packaging is green (installer + portable + delta updates) — the public v0.1.0 release tag is the only thing still pending.

---

## What AresToys is

AresToys is a Windows desktop application that brings together two of the most-used productivity tools in the screenshot/clipboard space — [CopyQ](https://github.com/hluk/CopyQ) and [ShareX](https://github.com/ShareX/ShareX) — under a single modern UI built on .NET 10 and WPF.

The core idea: **everything you copy or capture is part of the same searchable history**. A clipboard text entry, a screenshot, and a freshly-generated share link all live as items in one timeline — same store, same search, same browser.

## What it aims to fix

- **CopyQ has a powerful clipboard engine but a UI that feels stuck in the early 2000s.** AresToys keeps the engine, replaces the UI with a clean Fluent-style WPF design.
- **ShareX has every feature imaginable, and that's the problem.** Hundreds of uploaders, dozens of effects, a dense forest of options. AresToys keeps the proven backend (capture, recorder, image editor logic, upload pipeline) and trims the UI to the essentials, with everything else moved behind an opt-in plugin system.
- **The ShareX image editor has known UX pain points** (tool color leaking into already-drawn objects, awkward color picker, cramped toolbar). AresToys rebuilds the editor with a clear separation between "next-object color" (global swatches) and "selected-object color" (per-object property panel), a proper color picker with palette/recents/eyedropper, and a logically grouped toolbar.
- **Windows' native clipboard history is too shallow, ShareX's is screenshot-only, CopyQ's is clipboard-only.** AresToys unifies them into a single browsable timeline.

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

**Image effects**
- 60+ effects ported from ShareX across Adjustments (brightness / contrast / saturation / hue / levels / curves / temperature / split-toning / film emulation / …), Manipulations (canvas / crop / resize / rotate / shadow / rounded-corners / auto-crop / skew / flip / orthogonal-rotate), Filters (blur / motion-blur / sharpen / glow / pixelate / vignette / colour halftone / torn-edge / emboss / edge-detect / add-noise), Drawings (background / border / text / text-ex / image / particles / checkerboard / gradient overlay)
- Editor with multi-preset list, drag-reorderable effect chain, live preview on a sample image (or a user-loaded one), property grid with sliders / colour swatches / paddings / gradients / fonts
- Gradient picker (multi-stop with per-stop alpha + 9 ShareX presets) and a font picker (filterable family list + size slider + bold/italic chips)
- ShareX-compatible `.sxie` import + export — preserves the exact PascalCase + `$type` schema so a preset round-trips between ShareX and AresToys. Exports bundle asset files (DrawImage overlays) into the `.sxie` ZIP automatically
- File-association toggle for `.sxie` (mirrors the `.sxcu` flow): double-click a downloaded preset in Explorer → opens the editor with the preset already imported
- Pipeline task `Apply effects preset` lets the user attach any preset to capture / clipboard workflows so every screenshot can be auto-watermarked / coloured / cornered

**QR codes**
- Generator with live preview window (multiline editor → real-time QR), error-correction picker (L/M/Q/H), module-size slider, copy-to-clipboard, save as PNG, save as SVG, save into clipboard history (treated like a screenshot — file in the screenshot folder + history entry + optional upload)
- Decoder via region select (Tools → "Read QR code" or as a pipeline task)
- Right-click on a clipboard text item → "Generate QR code…" pre-fills the generator with that text
- Pipeline tasks: Show QR code, Save QR as image, Save QR as SVG, Copy QR to clipboard — all chainable with the rest of the workflow system

**Notifications**
- Modern Windows toasts (`ToastNotificationManagerCompat`) — they persist in the Notification Center after the popup fades, with inline preview thumbnails for image-bearing toasts (screenshot saves, QR saves) and unique tag/group per toast so successive notifications don't replace or collapse each other

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

Plus a declarative `.sxcu` engine that loads ShareX-compatible JSON uploader files dropped into `%LOCALAPPDATA%\AresToys\custom-uploaders\` for the long tail. Optional file-association toggle in Settings registers AresToys as the default opener for `.sxcu` files in Explorer (per-user, no admin), so double-clicking a `.sxcu` from a website triggers an import-confirmation dialog.

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

- **No public release tag yet.** Velopack packaging is wired and tested locally + via the CI workflow; the first GitHub Release (v0.1.0) is the last M7 step still pending.
- **No SharedFolder / FTP / SFTP / S3 / Azure / B2 uploaders** — backlog (FTP+SharedFolder are next; cloud-storage providers come after).
- **No scrolling capture / image combiner / hash checker / metadata viewer** — backlog. (Explorer context menu shipped; OCR was tried via Windows.Media.Ocr and dropped — too unreliable on dark themes / low contrast for the maintenance cost.)
- **Bundled OAuth client IDs aren't shipped** in the public source. Maintainers create `src/AresToys.Uploaders/Secrets.Local.cs` with their own credentials (gitignored). End users of a public release get zero-friction sign-in; users building from source themselves will see "isn't configured in this build" until they either drop in their own keys or paste credentials in the Configure dialog.
- **No i18n** — UI is English only.
- **No CLI / scripting interface** — everything runs through hotkeys + workflows.

## Tech stack

- .NET 10, C# (Windows-only)
- WPF + [WPF-UI](https://github.com/lepoco/wpfui) for Fluent/Mica styling
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) for MVVM
- [Microsoft.Data.Sqlite](https://learn.microsoft.com/dotnet/standard/data/sqlite/) with WAL + FTS5 + DPAPI-encrypted payloads
- DXGI Desktop Duplication + `Windows.Graphics.Capture` for screen capture
- FFmpeg (auto-downloaded on first use) for screen recording
- [SkiaSharp](https://github.com/mono/SkiaSharp) for the image-effects pipeline (ports of ShareX's per-pixel work to a managed surface)
- [QRCoder](https://github.com/codebude/QRCoder) for QR generation (PNG + SVG)
- [ZXing.Net](https://github.com/micjahn/ZXing.Net) for QR decoding
- [Microsoft.Toolkit.Uwp.Notifications](https://learn.microsoft.com/windows/apps/design/shell/tiles-and-notifications/send-local-toast?tabs=desktop) for Windows toast notifications (Action Center persistence + inline images)
- [Velopack](https://github.com/velopack/velopack) for installer / portable packaging and auto-update via GitHub Releases

## Roadmap

| Milestone | Scope | Status |
|-----------|-------|--------|
| **M0** | Solution scaffold, CI green, WPF app opens with tray | ✅ done |
| **M1** | Clipboard hook + storage + popup window with FTS search | ✅ done |
| **M2** | Hotkeys + region capture + base pipeline | ✅ done |
| **M3** | Image editor (port + UX fixes) | ✅ done |
| **M4** | Plugin loader + Imgur uploader + upload pipeline | ✅ done (replaced DLL plugin loader with bundled-class architecture + `.sxcu` for the long tail) |
| **M5** | OneDrive uploader + main window timeline + plugin manager UI | ✅ done (extended to OneDrive + GoogleDrive + Dropbox + 7 more) |
| **M6** | Screen recorder + Beautify + Watermark/Step counter | ✅ done — recorder ships; Watermark + step-counter live in the image-effects pass below; Beautify (the standalone one-click "make this look nicer" tool) shelved as low-impact |
| **M6.5** | Image-effects framework (60+ ShareX effects, `.sxie` round-trip, gradient + font pickers, live editor, pipeline task) | ✅ done |
| **M6.6** | QR generator UX (live preview window + multiline editor + PNG/SVG export + Action-Center notifications) | ✅ done |
| **M7** | Velopack packaging + GitHub auto-update + release v0.1.0 | 🟡 packaging + updater wired; first release tag still pending |

After M7, the next themed passes (rough order):
1. SharedFolder + FTP/FTPS/SFTP uploaders
2. Cloud storage uploaders (S3 / Azure Blob / Backblaze B2 / Google Cloud / Cloudflare R2)
3. Scrolling capture
4. CLI for scripting
5. i18n

See [`docs/Improvements.md`](docs/Improvements.md) for the granular feature-parity checklist.

## Building

Requirements: .NET 10 SDK, Windows 10 / 11.

```powershell
git clone https://github.com/Ares9323/AresToys
cd AresToys
dotnet build
dotnet run --project src/AresToys.App
```

For the OAuth uploaders (OneDrive / Google Drive / Dropbox / Imgur user-mode) to work without per-user setup, register apps with each provider following [`docs/RegisterAppGuide.md`](docs/RegisterAppGuide.md), then create `src/AresToys.Uploaders/Secrets.Local.cs` (gitignored) with the constants documented inside `src/AresToys.Uploaders/Secrets.cs`. Without `Secrets.Local.cs` the OAuth uploaders surface "isn't configured in this build" — the rest of AresToys runs fine.

User data lives at `%LOCALAPPDATA%\AresToys\` (SQLite + custom-uploaders + recordings). Delete that folder to reset to defaults.

## Releases

Distribution is built with [Velopack](https://velopack.io): every release ships both an installer (`AresToys-win-Setup.exe`) and a portable zip (`AresToys-win-Portable.zip`). The in-app updater (Settings → About → "Check for updates") fetches delta packages from GitHub Releases — only the differences between versions are downloaded.

To cut a release (maintainer):

1. Open the **Actions** tab → **Release** workflow → **Run workflow**.
2. Type the new semantic version (e.g. `0.1.0`) and tick "prerelease" if applicable.
3. The workflow builds, packs with `vpk`, and uploads a published GitHub Release with all assets.

The `vpk` CLI is pinned in [`.config/dotnet-tools.json`](.config/dotnet-tools.json) — `dotnet tool restore` installs the same version locally for testing the pack step before pushing a tag. The convenience wrapper [`tools/pack-local.ps1`](tools/pack-local.ps1) drives the same `dotnet publish` + `vpk pack` chain the workflow uses, with a few quality-of-life shortcuts:

```powershell
pwsh tools/pack-local.ps1                 # auto-pick the latest version in Releases\, overwrite
pwsh tools/pack-local.ps1 -Version 0.2.0  # explicit version (cleans prior artifacts for the channel)
pwsh tools/pack-local.ps1 -SkipBuild      # reuse the existing bin\Release output
pwsh tools/pack-local.ps1 -Channel local  # produce artifacts on a separate channel so the public 'win' channel stays clean
```

After pack, `Releases\AresToys-<channel>-Setup.exe` installs AresToys into `%LocalAppData%\AresToys` exactly as a real release would; `Releases\AresToys-<channel>-Portable.zip` extracts into a folder you can run from anywhere.

## Acknowledgements

AresToys stands directly on the shoulders of two GPL-3 projects, and reuses substantial portions of one of them:

- **[ShareX](https://github.com/ShareX/ShareX)** — the bulk of AresToys's backend (capture, image editor, screen recorder, image effects, uploaders) is being ported from ShareX's modular libraries. Original authors and maintainers retain credit; AresToys is a derivative work and is itself GPL-3-or-later.
- **[CopyQ](https://github.com/hluk/CopyQ)** — used as conceptual reference for the clipboard manager experience: history model, item-plugin architecture, popup-on-hotkey workflow.
- **[MaxLauncher](https://maxlauncher.sourceforge.io/)** — inspired AresToys's launcher overlay: the keyboard-centric grid of mappable cells (F1-F10 global strip + 10 numeric tabs × 30 QWERTY keys), drag-and-drop assignment of files / shortcuts / folders, and the search-as-you-type filter that hides non-matching tabs and cells.

The licensing of both upstream projects (GPL-3-or-later) carries through to AresToys. See [`LICENSE`](LICENSE).

## License

[GPL-3.0-or-later](LICENSE), inherited from both upstream projects.

## Contributing

The project is in early alpha and the API surface is still moving. Issues + bug reports are welcome; for code contributions please open an issue first to discuss scope before sending a PR.
