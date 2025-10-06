using OpenCvSharp;
using ThumbPick.Models;

namespace ThumbPick.Core;

public sealed class VideoSampler
{
    public IReadOnlyList<double> GenerateTimestamps(double durationSeconds, double sampleRate)
    {
        if (durationSeconds <= 0 || sampleRate <= 0)
        {
            return Array.Empty<double>();
        }

        var interval = 1.0 / sampleRate;
        var timestamps = new List<double>();
        for (var t = 0.0; t <= durationSeconds; t += interval)
        {
            timestamps.Add(t);
        }

        return timestamps;
    }

    public bool TryReadFrameAt(VideoCapture capture, double timestamp, out Mat frame)
    {
        frame = new Mat();
        var positionMsec = timestamp * 1000.0;
        capture.Set(VideoCaptureProperties.PosMsec, positionMsec);
        if (!capture.Read(frame) || frame.Empty())
        {
            frame.Dispose();
            return false;
        }

        return true;
    }
}
