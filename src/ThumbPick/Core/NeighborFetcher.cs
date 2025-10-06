using OpenCvSharp;
using ThumbPick.Configuration;
using ThumbPick.Metrics;
using ThumbPick.Models;

namespace ThumbPick.Core;

public sealed class NeighborFetcher
{
    public Dictionary<FrameMetrics, List<(int offset, FrameMetrics metrics)>> FetchNeighbors(
        VideoCapture capture,
        IEnumerable<FrameMetrics> selected,
        IReadOnlyList<int> offsets,
        double sampleRate,
        MetricsEngine metricsEngine,
        PresetDefinition preset,
        VideoMetadata metadata)
    {
        var result = new Dictionary<FrameMetrics, List<(int offset, FrameMetrics metrics)>>();
        var interval = 1.0 / Math.Max(sampleRate, 1e-6);

        foreach (var candidate in selected)
        {
            var neighbors = new List<(int offset, FrameMetrics metrics)>();
            foreach (var offset in offsets)
            {
                var ts = candidate.TimeSec + offset * interval;
                if (ts < 0)
                {
                    continue;
                }

                if (!TryReadFrameAt(capture, ts, out var frame))
                {
                    continue;
                }

                using var mat = frame;
                var neighborMetrics = metricsEngine.Evaluate(mat, metadata, ts, preset);
                neighbors.Add((offset, neighborMetrics));
            }

            result[candidate] = neighbors.OrderBy(n => n.offset).ToList();
        }

        return result;
    }

    private static bool TryReadFrameAt(VideoCapture capture, double timestamp, out Mat frame)
    {
        frame = new Mat();
        capture.Set(VideoCaptureProperties.PosMsec, timestamp * 1000.0);
        if (!capture.Read(frame) || frame.Empty())
        {
            frame.Dispose();
            return false;
        }

        return true;
    }
}
