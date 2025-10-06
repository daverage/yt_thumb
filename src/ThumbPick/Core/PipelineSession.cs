using OpenCvSharp;
using ThumbPick.Configuration;
using ThumbPick.IO;
using ThumbPick.Metrics;
using ThumbPick.Models;

namespace ThumbPick.Core;

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

    public void Execute()
    {
        using var capture = new VideoCapture(_options.InputPath);
        if (!capture.IsOpened())
        {
            throw new InvalidOperationException($"Unable to open video {_options.InputPath}");
        }

        var metadata = VideoMetadata.FromCapture(_options.InputPath, capture);
        var sampleRate = _options.ExplicitSampleRate ?? ResolveSamplingRate(metadata);
        var timestamps = _sampler.GenerateTimestamps(metadata.DurationSeconds, sampleRate);

        foreach (var stamp in timestamps)
        {
            if (!_sampler.TryReadFrameAt(capture, stamp, out var frame))
            {
                continue;
            }

            using var mat = frame;
            var metrics = _metrics.Evaluate(mat, metadata, stamp, _preset);
            _frames.Add(metrics);
        }

        _metrics.Normalize(_frames);
        foreach (var frame in _frames)
        {
            _metrics.ComputeFinalScore(frame, _preset.Weights);
        }

        var hardFiltered = _frames.Where(f => !_metrics.IsHardRejected(f, _preset)).ToList();
        _top.AddRange(_ranker.SelectTopCandidates(hardFiltered, _preset, _options.Top));

        var neighborOffsets = _options.NeighborOffsets?.ToArray() ?? Enumerable.Range(1, _options.Neighbors)
            .SelectMany(offset => new[] { -offset, offset })
            .OrderBy(v => Math.Abs(v))
            .ThenBy(v => v)
            .ToArray();

        var neighborFrames = _neighbors.FetchNeighbors(capture, _top, neighborOffsets, sampleRate, _metrics, _preset, metadata);

        ManifestPath = _writer.WriteOutput(_options, metadata, _frames, _top, neighborFrames, _preset, sampleRate);
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
