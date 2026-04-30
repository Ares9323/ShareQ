# ShareQ

> Modern clipboard + screenshot tool for Windows, unifying the strengths of CopyQ and ShareX into a single app.

**Status:** Pre-alpha. Design phase. No usable build yet.

---

## What ShareQ is

ShareQ is a Windows desktop application that brings together two of the most-used productivity tools in the screenshot/clipboard space — [CopyQ](https://github.com/hluk/CopyQ) and [ShareX](https://github.com/ShareX/ShareX) — under a single modern UI built on .NET 10 and WPF.

The core idea: **everything you copy or capture is part of the same searchable history**. A clipboard text entry, a screenshot, and a freshly-generated share link all live as items in one timeline — same store, same search, same browser.

## What it aims to fix

- **CopyQ has a powerful clipboard engine but a UI that feels stuck in the early 2000s.** ShareQ keeps the engine, replaces the UI with a clean Fluent-style WPF design.
- **ShareX has every feature imaginable, and that's the problem.** Hundreds of uploaders, dozens of effects, a dense forest of options. ShareQ keeps the proven backend (capture, recorder, image editor logic, upload pipeline) and trims the UI to the essentials, with everything else moved behind an opt-in plugin system.
- **The ShareX image editor has known UX pain points** (tool color leaking into already-drawn objects, awkward color picker, cramped toolbar). ShareQ rebuilds the editor with a clear separation between "next-object color" (global swatches) and "selected-object color" (per-object property panel), a proper color picker with palette/recents/eyedropper, and a logically grouped toolbar.
- **Windows' native clipboard history is too shallow, ShareX's is screenshot-only, CopyQ's is clipboard-only.** ShareQ unifies them into a single browsable timeline.

## Acknowledgements

ShareQ stands directly on the shoulders of two GPL-3 projects, and reuses substantial portions of one of them:

- **[ShareX](https://github.com/ShareX/ShareX)** — the bulk of ShareQ's backend (capture, image editor, screen recorder, image effects, uploaders) is being ported from ShareX's modular libraries. Original authors and maintainers retain credit; ShareQ is a derivative work and is itself GPL-3-or-later.
- **[CopyQ](https://github.com/hluk/CopyQ)** — used as conceptual reference for the clipboard manager experience: history model, item-plugin architecture, popup-on-hotkey workflow.
- **[MaxLauncher](https://maxlauncher.sourceforge.io/)** — inspired ShareQ's launcher overlay: the keyboard-centric grid of mappable cells (F1-F10 global strip + 10 numeric tabs × 30 QWERTY keys), drag-and-drop assignment of files / shortcuts / folders, and the search-as-you-type filter that hides non-matching tabs and cells.

The licensing of both upstream projects (GPL-3-or-later) carries through to ShareQ. See [`LICENSE`](LICENSE).

## Tech stack

- .NET 10, C# (Windows)
- WPF + [WPF-UI](https://github.com/lepoco/wpfui) for Fluent/Mica styling
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) for MVVM
- [Microsoft.Data.Sqlite](https://learn.microsoft.com/dotnet/standard/data/sqlite/) with WAL + FTS5 + DPAPI-encrypted payloads
- DXGI Desktop Duplication + `Windows.Graphics.Capture` for screen capture (Snipping-Tool-class reliability)
- FFmpeg (downloaded on first use) for screen recording
- [Velopack](https://github.com/velopack/velopack) for installer/portable packaging and auto-update via GitHub Releases

## Status & roadmap

ShareQ is at **design phase** — the architecture and scope are defined, the implementation plan is being written. The project is published in pre-alpha precisely so that interested observers can follow along; no usable binary exists yet.

Implementation milestones (high-level, full detail in the implementation plan):

| Milestone | Scope |
|-----------|-------|
| **M0** | Solution scaffold, CI green, WPF app opens with tray. |
| **M1** | Clipboard hook + storage + popup window with FTS search. |
| **M2** | Hotkeys + region capture + base pipeline. |
| **M3** | Image editor (port + UX fixes). |
| **M4** | Plugin loader + Imgur uploader + upload pipeline. |
| **M5** | OneDrive uploader + main window timeline + plugin manager UI. |
| **M6** | Screen recorder + Beautify + Watermark/Step counter. |
| **M7** | Velopack packaging + GitHub auto-update + release v0.1.0. |

## Building (placeholder)

Build instructions will land with milestone M0. For now: there's nothing to build.

## License

[GPL-3.0-or-later](LICENSE), inherited from both upstream projects.

## Contributing

The project isn't accepting contributions yet — happy to talk about it in issues, but please hold off on PRs until milestone M0 lands.
