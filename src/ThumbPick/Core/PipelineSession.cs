using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using OpenCvSharp;
using ThumbPick.Configuration;
using ThumbPick.IO;
using ThumbPick.Metrics;
using ThumbPick.Models;

namespace ThumbPick.Core;

public readonly record struct PipelineProgress(string Stage, double Value, double Maximum, string? Detail = null);

public sealed class PipelineSession : IDisposable
{
    private readonly AppOptions _options;
    private readonly PresetDefinition _preset;
    private readonly VideoSampler _sampler;
    private readonly MetricsEngine _metrics;
    private readonly CandidateRanker _ranker;
    private readonly NeighborFetcher _neighbors;
    private readonly ManifestWriter _writer;

    private readonly List<FrameMetrics> _frames = new();
    private readonly List<FrameMetrics> _top = new();

    public string ManifestPath { get; private set; } = string.Empty;

    public PipelineSession(
        AppOptions options,
        PresetDefinition preset,
        VideoSampler sampler,
        MetricsEngine metrics,
        CandidateRanker ranker,
        NeighborFetcher neighbors,
        ManifestWriter writer)
    {
        _options = options;
        _preset = preset;
        _sampler = sampler;
        _metrics = metrics;
        _ranker = ranker;
        _neighbors = neighbors;
        _writer = writer;
    }

    public void Execute(IProgress<PipelineProgress>? progress = null)
    {
        progress?.Report(new PipelineProgress("Opening video", 0, 1));

        using var capture = new VideoCapture(_options.InputPath);
        if (!capture.IsOpened())
        {
            throw new InvalidOperationException($"Unable to open video {_options.InputPath}");
        }

        var metadata = VideoMetadata.FromCapture(_options.InputPath, capture);
        var sampleRate = _options.ExplicitSampleRate ?? ResolveSamplingRate(metadata);
        var timestamps = _sampler.GenerateTimestamps(metadata.DurationSeconds, sampleRate);
        var totalFrames = Math.Max(timestamps.Count, 1);
        var processedFrames = 0;

        progress?.Report(new PipelineProgress("Sampling frames", processedFrames, totalFrames));

        foreach (var stamp in timestamps)
        {
            if (_sampler.TryReadFrameAt(capture, stamp, out var frame))
            {
                using var mat = frame;
                var metrics = _metrics.Evaluate(mat, metadata, stamp, _preset);
                metrics.SavedPath = SaveRawFrame(metrics);
                metrics.Frame?.Dispose();
                metrics.Frame = null;
                _frames.Add(metrics);
            }

            processedFrames++;
            progress?.Report(new PipelineProgress(
                "Sampling frames",
                processedFrames,
                totalFrames,
                $"Processed {processedFrames} of {totalFrames} frames"));
        }

        if (timestamps.Count == 0)
        {
            progress?.Report(new PipelineProgress("Sampling frames", totalFrames, totalFrames, "No frames sampled."));
        }

        progress?.Report(new PipelineProgress("Scoring frames", 0, 1));

        _metrics.Normalize(_frames);
        foreach (var frame in _frames)
        {
            _metrics.ComputeFinalScore(frame, _preset.Weights);
        }

        progress?.Report(new PipelineProgress("Selecting top candidates", 0, 1));

        var hardFiltered = _frames.Where(f => !_metrics.IsHardRejected(f, _preset)).ToList();
        _top.AddRange(_ranker.SelectTopCandidates(hardFiltered, _preset, _options.Top));

        var neighborOffsets = _options.NeighborOffsets?.ToArray() ?? Enumerable.Range(1, _options.Neighbors)
            .SelectMany(offset => new[] { -offset, offset })
            .OrderBy(v => Math.Abs(v))
            .ThenBy(v => v)
            .ToArray();

        progress?.Report(new PipelineProgress("Fetching neighbors", 0, 1));

        var neighborFrames = _neighbors.FetchNeighbors(capture, _top, neighborOffsets, sampleRate, _metrics, _preset, metadata);

        progress?.Report(new PipelineProgress("Writing manifest", 0, 1));

        ManifestPath = _writer.WriteOutput(_options, metadata, _frames, _top, neighborFrames, _preset, sampleRate);

        progress?.Report(new PipelineProgress("Completed", 1, 1, ManifestPath));

        foreach (var frame in _frames)
        {
            frame.Dispose();
        }
    }

    private string SaveRawFrame(FrameMetrics metrics)
    {
        var framesDir = Path.Combine(_options.OutputDirectory, "frames");
        Directory.CreateDirectory(framesDir);

        if (metrics.SavedPath is not null && File.Exists(metrics.SavedPath))
        {
            return metrics.SavedPath;
        }

        if (metrics.Frame is null)
        {
            throw new InvalidOperationException("Cannot save frame because the image data is unavailable.");
        }

        var path = Path.Combine(framesDir, $"f_{metrics.TimeSec:000000.000}.png");
        Cv2.ImWrite(path, metrics.Frame);
        metrics.SavedPath = path;
        return path;
    }

    private double ResolveSamplingRate(VideoMetadata metadata)
    {
        return _preset.Sampling?.Mode.ToLowerInvariant() switch
        {
            "fpm" => (_preset.Sampling?.Value ?? 120) / 60.0,
            _ => _preset.Sampling?.Value ?? Math.Min(metadata.FrameRate, 2.0)
        };
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
