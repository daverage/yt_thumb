using System.Text.Json;
using System.Text.Json.Serialization;
using OpenCvSharp;
using ThumbPick.Configuration;
using ThumbPick.Models;

namespace ThumbPick.IO;

public sealed class ManifestWriter
{
    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string WriteOutput(
        AppOptions appOptions,
        VideoMetadata metadata,
        IEnumerable<FrameMetrics> allFrames,
        IEnumerable<FrameMetrics> topFrames,
        Dictionary<FrameMetrics, List<(int offset, FrameMetrics metrics)>> neighbors,
        PresetDefinition preset,
        double sampleRate)
    {
        var baseDir = appOptions.OutputDirectory;
        var framesDir = Path.Combine(baseDir, "frames");
        var candidatesDir = Path.Combine(baseDir, "candidates");
        Directory.CreateDirectory(framesDir);
        Directory.CreateDirectory(candidatesDir);

        foreach (var frame in allFrames)
        {
            frame.SavedPath = SaveFrame(frame.Frame, framesDir, frame.TimeSec);
        }

        var topManifest = new List<ManifestEntry>();
        foreach (var frame in topFrames)
        {
            var mainPath = SaveCandidate(frame.Frame, candidatesDir, frame.TimeSec, "main");
            var manifestEntry = new ManifestEntry
            {
                Time = frame.TimeSec,
                Score = frame.FinalScore,
                Path = mainPath,
                Neighbors = new List<NeighborEntry>(),
                SuggestedCrop = ComputeSuggestedCrop(metadata)
            };

            if (neighbors.TryGetValue(frame, out var neighborList))
            {
                foreach (var neighbor in neighborList)
                {
                    var suffix = neighbor.offset >= 0 ? $"p{neighbor.offset}" : $"m{-neighbor.offset}";
                    var neighborPath = SaveCandidate(neighbor.metrics.Frame, candidatesDir, frame.TimeSec, suffix);
                    manifestEntry.Neighbors.Add(new NeighborEntry
                    {
                        Offset = neighbor.offset,
                        Path = neighborPath
                    });
                    neighbor.metrics.Frame?.Dispose();
                    neighbor.metrics.Frame = null;
                    neighbor.metrics.Downscaled?.Dispose();
                    neighbor.metrics.Downscaled = null;
                }
            }

            topManifest.Add(manifestEntry);
            frame.Frame?.Dispose();
            frame.Frame = null;
            frame.Downscaled?.Dispose();
            frame.Downscaled = null;
        }

        foreach (var frame in allFrames)
        {
            frame.Frame?.Dispose();
            frame.Frame = null;
            frame.Downscaled?.Dispose();
            frame.Downscaled = null;
        }

        var manifest = new Manifest
        {
            Video = new ManifestVideo
            {
                Path = metadata.Path,
                DurationSec = metadata.DurationSeconds,
                Fps = metadata.FrameRate,
                Width = metadata.Width,
                Height = metadata.Height
            },
            Preset = preset.Name,
            Parameters = new ManifestParameters
            {
                SampleRate = sampleRate,
                Top = appOptions.Top,
                Neighbors = appOptions.Neighbors
            },
            FramesAnalyzed = allFrames.Count(),
            Scores = allFrames.Select(ToFrameScore).ToList(),
            Top = topManifest
        };

        var manifestPath = Path.Combine(baseDir, "manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, _options));
        return manifestPath;
    }

    private static string SaveFrame(Mat? frame, string baseDir, double time)
    {
        if (frame == null)
        {
            return string.Empty;
        }

        var fileName = Path.Combine(baseDir, $"f_{time:000000.000}.png");
        Cv2.ImWrite(fileName, frame);
        return fileName;
    }

    private static string SaveCandidate(Mat? frame, string baseDir, double time, string suffix)
    {
        if (frame == null)
        {
            return string.Empty;
        }

        var fileName = Path.Combine(baseDir, $"c_{time:000000.000}_{suffix}.png");
        Cv2.ImWrite(fileName, frame);
        return fileName;
    }

    private static CropSuggestion ComputeSuggestedCrop(VideoMetadata metadata)
    {
        if (Math.Abs((metadata.Width / (double)metadata.Height) - (16.0 / 9.0)) < 0.01)
        {
            return new CropSuggestion(0, 0, metadata.Width, metadata.Height);
        }

        var targetWidth = metadata.Width;
        var targetHeight = (int)(metadata.Width * 9.0 / 16.0);
        if (targetHeight > metadata.Height)
        {
            targetHeight = metadata.Height;
            targetWidth = (int)(metadata.Height * 16.0 / 9.0);
        }

        var x = (metadata.Width - targetWidth) / 2;
        var y = (metadata.Height - targetHeight) / 2;
        return new CropSuggestion(x, y, targetWidth, targetHeight);
    }

    private static FrameScore ToFrameScore(FrameMetrics metrics) => new()
    {
        Time = metrics.TimeSec,
        Sharp = metrics.Sharpness,
        RawSharp = metrics.RawSharpness,
        Exposure = metrics.Exposure,
        RawExposure = metrics.RawExposure,
        Contrast = metrics.Contrast,
        RawContrast = metrics.RawContrast,
        Color = metrics.Colorfulness,
        RawColor = metrics.RawColorfulness,
        Face = metrics.FaceScore,
        RawFace = metrics.RawFaceScore,
        Centrality = metrics.Centrality,
        RawCentrality = metrics.RawCentrality,
        Clutter = metrics.Clutter,
        RawClutter = metrics.RawClutter,
        Overlay = metrics.OverlaySafe,
        RawOverlay = metrics.RawOverlaySafe,
        Motion = metrics.Motion,
        RawMotion = metrics.RawMotion,
        TimeScore = metrics.TimePrior,
        RawTime = metrics.RawTimePrior,
        Score = metrics.FinalScore,
        Path = metrics.SavedPath ?? string.Empty
    };
}

public record Manifest
{
    [JsonPropertyName("video")]
    public ManifestVideo Video { get; init; } = new();

    [JsonPropertyName("preset")]
    public string Preset { get; init; } = string.Empty;

    [JsonPropertyName("parameters")]
    public ManifestParameters Parameters { get; init; } = new();

    [JsonPropertyName("framesAnalyzed")]
    public int FramesAnalyzed { get; init; }

    [JsonPropertyName("scores")]
    public List<FrameScore> Scores { get; init; } = new();

    [JsonPropertyName("top")]
    public List<ManifestEntry> Top { get; init; } = new();
}

public record ManifestVideo
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("durationSec")]
    public double DurationSec { get; init; }

    [JsonPropertyName("fps")]
    public double Fps { get; init; }

    [JsonPropertyName("width")]
    public int Width { get; init; }

    [JsonPropertyName("height")]
    public int Height { get; init; }
}

public record ManifestParameters
{
    [JsonPropertyName("fps")]
    public double SampleRate { get; init; }

    [JsonPropertyName("top")]
    public int Top { get; init; }

    [JsonPropertyName("neighbors")]
    public int Neighbors { get; init; }
}

public record FrameScore
{
    [JsonPropertyName("t")]
    public double Time { get; init; }

    [JsonPropertyName("sharp")]
    public double Sharp { get; init; }

    [JsonPropertyName("sharpRaw")]
    public double RawSharp { get; init; }

    [JsonPropertyName("exposure")]
    public double Exposure { get; init; }

    [JsonPropertyName("exposureRaw")]
    public double RawExposure { get; init; }

    [JsonPropertyName("contrast")]
    public double Contrast { get; init; }

    [JsonPropertyName("contrastRaw")]
    public double RawContrast { get; init; }

    [JsonPropertyName("color")]
    public double Color { get; init; }

    [JsonPropertyName("colorRaw")]
    public double RawColor { get; init; }

    [JsonPropertyName("face")]
    public double Face { get; init; }

    [JsonPropertyName("faceRaw")]
    public double RawFace { get; init; }

    [JsonPropertyName("centrality")]
    public double Centrality { get; init; }

    [JsonPropertyName("centralityRaw")]
    public double RawCentrality { get; init; }

    [JsonPropertyName("clutter")]
    public double Clutter { get; init; }

    [JsonPropertyName("clutterRaw")]
    public double RawClutter { get; init; }

    [JsonPropertyName("overlay")]
    public double Overlay { get; init; }

    [JsonPropertyName("overlayRaw")]
    public double RawOverlay { get; init; }

    [JsonPropertyName("motion")]
    public double Motion { get; init; }

    [JsonPropertyName("motionRaw")]
    public double RawMotion { get; init; }

    [JsonPropertyName("time")]
    public double TimeScore { get; init; }

    [JsonPropertyName("timeRaw")]
    public double RawTime { get; init; }

    [JsonPropertyName("score")]
    public double Score { get; init; }

    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;
}

public record ManifestEntry
{
    [JsonPropertyName("t")]
    public double Time { get; init; }

    [JsonPropertyName("score")]
    public double Score { get; init; }

    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("neighbors")]
    public List<NeighborEntry> Neighbors { get; init; } = new();

    [JsonPropertyName("suggestedCrop")]
    public CropSuggestion SuggestedCrop { get; init; } = new(0, 0, 0, 0);
}

public record NeighborEntry
{
    [JsonPropertyName("dt")]
    public int Offset { get; init; }

    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;
}

public record CropSuggestion(int X, int Y, int Width, int Height);
