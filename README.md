# Fast Offline Thumbnail Picker

`thumbpick` is a .NET 8 toolkit that extracts and ranks thumbnail candidates from a video without relying on GPU acceleration or cloud services. It now includes both a command line interface and a WPF desktop client with drag-and-drop support.

## Features

- Deterministic, preset-driven scoring pipeline with classical computer vision heuristics.
- Haar cascade face detection with overlay-safe region analysis.
- Temporal and appearance diversity filters to keep the top picks distinct.
- Manifest JSON output including per-frame metric scores and recommended crops.

## Building

This repository targets .NET 8. Install the .NET 8 SDK and restore dependencies:

```bash
dotnet restore
```

### Visual Studio

Open `ThumbPick.sln` in Visual Studio 2022 (17.x or newer). The solution includes the CLI, WPF UI, and test projects and builds for
both Debug and Release Any CPU configurations out of the box.

## Running (CLI)

```
dotnet run --project src/ThumbPick \
  -- --input "video.mp4" \
  --preset Presenter \
  --fps 2 \
  --top 6 \
  --neighbors 2 \
  --out "./thumbnails"
```

- Place Haar cascade XML files inside `./cascades` before running (see `cascades/README.md`).
- Preset JSON files live under `./presets` and can be overridden via `--preset-dir` or `--config`.
- Inline weight overrides can be supplied with `--weights '{"face":0.3,"motion":0.01}'`.
- Use `--ffmpeg` if the executable is not located next to `thumbpick.exe`; otherwise the tool will search the application folder and your `PATH`.

## Desktop interface

The `ThumbPick.Gui` project provides a desktop workflow for operators who prefer a UI.

```bash
dotnet run --project src/ThumbPick.Gui
```

- Drag a video file anywhere onto the window or use the **Browse** button.
- Choose presets, sampling density, top-pick count, and neighbor count before kicking off the run.
- Click **Browse...** next to the ffmpeg field to point the app at your ffmpeg binary when it is stored outside the executable directory. The path is persisted into the manifest under `tools.ffmpeg` for traceability.

## Testing

```
dotnet test
```

## Optional learning loop

The codebase is structured so you can later plug in lightweight weight updates by extending the `PresetProvider`.
