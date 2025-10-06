# Fast Offline Thumbnail Picker

`thumbpick` is a .NET 8 command line tool that extracts and ranks thumbnail candidates from a video without relying on GPU acceleration or cloud services.

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

## Running

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

## Testing

```
dotnet test
```

## Optional learning loop

The codebase is structured so you can later plug in lightweight weight updates by extending the `PresetProvider`.
