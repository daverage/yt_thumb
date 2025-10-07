using System.IO;
using System;
using System.Collections.Generic;
using OpenCvSharp;
using ThumbPick.Configuration;
using ThumbPick.Models;

namespace ThumbPick.Metrics;

public sealed class MetricsEngine : IDisposable
{
    private readonly MetricsConfiguration _config;
    private readonly CascadeClassifier? _frontalCascade;
    private readonly CascadeClassifier? _profileCascade;
    private readonly CascadeClassifier? _glassesCascade;
    private readonly CascadeClassifier? _smileCascade;
    private readonly List<string> _warnings = new();
    private Mat? _previousGray;
    private readonly object _lock = new();

    public IReadOnlyList<string> Warnings => _warnings;

    public MetricsEngine(MetricsConfiguration config)
    {
        _config = config;
        _frontalCascade = LoadCascade(config.FrontalCascadeName);
        _profileCascade = LoadCascade(config.ProfileCascadeName);
        _glassesCascade = LoadCascade(config.GlassesCascadeName);
        _smileCascade = LoadCascade(config.SmileCascadeName);
    }

    private CascadeClassifier? LoadCascade(string fileName)
    {
        var path = Path.Combine(_config.CascadeDirectory, fileName);
        if (!File.Exists(path))
        {
            RecordWarning($"Cascade classifier '{fileName}' was not found in '{_config.CascadeDirectory}'. Face detection accuracy may be reduced.");
            return null;
        }

        try
        {
            return new CascadeClassifier(path);
        }
        catch (Exception ex)
        {
            RecordWarning($"Failed to load cascade '{fileName}': {ex.Message}");
            return null;
        }
    }

    private void RecordWarning(string message)
    {
        _warnings.Add(message);
        Console.Error.WriteLine($"[ThumbPick] Warning: {message}");
    }

    public FrameMetrics Evaluate(Mat frame, VideoMetadata metadata, double timestamp, PresetDefinition preset)
    {
        var metrics = new FrameMetrics
        {
            TimeSec = timestamp,
            Frame = frame.Clone()
        };

        using var downscaled = ResizeForAnalysis(frame, _config.AnalysisWidth);
        metrics.Downscaled = downscaled.Clone();
        using var gray = new Mat();
        Cv2.CvtColor(downscaled, gray, ColorConversionCodes.BGR2GRAY);

        var sharp = ComputeSharpness(gray);
        var (exposure, contrast) = ComputeExposureAndContrast(downscaled);
        var color = ComputeColorfulness(downscaled);

        metrics.Faces = DetectFaces(gray);
        var faceScore = ComputeFaceScore(metrics.Faces, gray.Size());
        var centrality = ComputeCentrality(metrics.Faces, gray.Size());
        var clutter = ComputeClutter(gray, metrics.Faces);
        var overlay = ComputeOverlaySafety(gray, metrics.Faces, preset.OverlayZones);
        var motion = ComputeMotion(gray);
        var timePrior = ComputeTimestampPrior(timestamp, metadata.DurationSeconds);

        metrics.RawSharpness = sharp;
        metrics.RawExposure = exposure;
        metrics.RawContrast = contrast;
        metrics.RawColorfulness = color;
        metrics.RawFaceScore = faceScore;
        metrics.RawCentrality = centrality;
        metrics.RawClutter = clutter;
        metrics.RawOverlaySafe = overlay;
        metrics.RawMotion = motion;
        metrics.RawTimePrior = timePrior;

        metrics.Sharpness = sharp;
        metrics.Exposure = exposure;
        metrics.Contrast = contrast;
        metrics.Colorfulness = color;
        metrics.FaceScore = faceScore;
        metrics.Centrality = centrality;
        metrics.Clutter = clutter;
        metrics.OverlaySafe = overlay;
        metrics.Motion = motion;
        metrics.TimePrior = timePrior;

        return metrics;
    }

    public void Normalize(List<FrameMetrics> frames)
    {
        if (frames.Count == 0)
        {
            return;
        }

        var pairs = new List<(Func<FrameMetrics, double> getter, Action<FrameMetrics, double> setter)>
        {
            (f => f.RawSharpness, (f, v) => f.Sharpness = v),
            (f => f.RawExposure, (f, v) => f.Exposure = v),
            (f => f.RawContrast, (f, v) => f.Contrast = v),
            (f => f.RawColorfulness, (f, v) => f.Colorfulness = v),
            (f => f.RawFaceScore, (f, v) => f.FaceScore = v),
            (f => f.RawCentrality, (f, v) => f.Centrality = v),
            (f => f.RawClutter, (f, v) => f.Clutter = v),
            (f => f.RawOverlaySafe, (f, v) => f.OverlaySafe = v),
            (f => f.RawMotion, (f, v) => f.Motion = v),
            (f => f.RawTimePrior, (f, v) => f.TimePrior = v)
        };

        foreach (var (getter, setter) in pairs)
        {
            var min = frames.Min(getter);
            var max = frames.Max(getter);
            var range = Math.Max(max - min, 1e-6);
            foreach (var frame in frames)
            {
                var value = (getter(frame) - min) / range;
                setter(frame, value);
            }
        }
    }

    public void ComputeFinalScore(FrameMetrics metrics, WeightConfig weights)
    {
        metrics.FinalScore =
            weights.Sharp * metrics.Sharpness +
            weights.Exposure * metrics.Exposure +
            weights.Contrast * metrics.Contrast +
            weights.Color * metrics.Colorfulness +
            weights.Face * metrics.FaceScore +
            weights.Centrality * metrics.Centrality +
            weights.Clutter * (1.0 - metrics.Clutter) +
            weights.Overlay * metrics.OverlaySafe +
            weights.Motion * (1.0 - metrics.Motion) +
            weights.Time * metrics.TimePrior;
    }

    public bool IsHardRejected(FrameMetrics metrics, PresetDefinition preset)
    {
        if (metrics.RawSharpness < preset.Thresholds.SharpMin)
        {
            return true;
        }

        if (metrics.RawExposure < preset.Thresholds.LMin || metrics.RawExposure > preset.Thresholds.LMax)
        {
            return true;
        }

        if (preset.RequireFaceResolved && metrics.RawFaceScore <= 0)
        {
            return true;
        }

        return false;
    }

    private static Mat ResizeForAnalysis(Mat source, int targetWidth)
    {
        if (source.Width <= targetWidth)
        {
            return source.Clone();
        }

        var scale = targetWidth / (double)source.Width;
        var height = (int)Math.Round(source.Height * scale);
        var dst = new Mat();
        Cv2.Resize(source, dst, new Size(targetWidth, height));
        return dst;
    }

    private static double ComputeSharpness(Mat gray)
    {
        using var lap = new Mat();
        Cv2.Laplacian(gray, lap, MatType.CV_64F);
        Cv2.MeanStdDev(lap, out _, out var std);
        var sigma = std.Val0; // Correct way to access the value from Scalar
        return sigma * sigma;
    }

    private static (double exposure, double contrast) ComputeExposureAndContrast(Mat bgr)
    {
        using var lab = new Mat();
        Cv2.CvtColor(bgr, lab, ColorConversionCodes.BGR2Lab);
        var channels = lab.Split();
        using var l = channels[0];
        Cv2.MeanStdDev(l, out var mean, out var std);
        // Correct access using Val0 property
        var exposure = mean.Val0;
        var contrast = std.Val0;
        foreach (var ch in channels)
        {
            ch.Dispose();
        }
        return (exposure, contrast);
    }


    private static double ComputeColorfulness(Mat bgr)
    {
        var channels = bgr.Split();
        using var b = channels[0];
        using var g = channels[1];
        using var r = channels[2];
        using var rg = new Mat();
        Cv2.Absdiff(r, g, rg);
        using var yb = new Mat();
        using var y = new Mat();
        Cv2.Add(r, g, y);
        Cv2.Divide(y, 2, y);
        Cv2.Absdiff(y, b, yb);
        Cv2.MeanStdDev(rg, out _, out var s1);
        Cv2.MeanStdDev(yb, out _, out var s2);
        var colorfulness = s1.Val0 + 0.3 * s2.Val0; // Corrected access to Scalar values
        foreach (var ch in channels)
        {
            ch.Dispose();
        }

        return colorfulness;
    }



    private Rect[] DetectFaces(Mat gray)
    {
        if (_frontalCascade == null && _profileCascade == null && _glassesCascade == null)
        {
            return Array.Empty<Rect>();
        }

        var faces = new List<Rect>();
        lock (_lock)
        {
            switch (_config.FaceDetector)
            {
                case FaceDetectionMode.Default:
                    if (_frontalCascade != null)
                    {
                        faces.AddRange(_frontalCascade.DetectMultiScale(gray, 1.1, 5, HaarDetectionTypes.ScaleImage, new Size(60, 60)));
                    }

                    if (_profileCascade != null)
                    {
                        faces.AddRange(_profileCascade.DetectMultiScale(gray, 1.1, 4, HaarDetectionTypes.ScaleImage, new Size(60, 60)));
                    }
                    break;
                case FaceDetectionMode.Glasses:
                    if (_glassesCascade != null)
                    {
                        var glassesHits = _glassesCascade.DetectMultiScale(gray, 1.05, 3, HaarDetectionTypes.ScaleImage, new Size(30, 30));
                        foreach (var eyeRegion in glassesHits)
                        {
                            faces.Add(ExpandEyeRegionToFace(eyeRegion, gray.Size()));
                        }
                    }
                    break;
                case FaceDetectionMode.Smile:
                    if (_smileCascade != null)
                    {
                        faces.AddRange(_smileCascade.DetectMultiScale(gray, 1.1, 20, HaarDetectionTypes.ScaleImage, new Size(30, 30)));
                    }
                    break;
            }
        }

        return faces
            .Select(face => ClampRectToBounds(face, gray.Size()))
            .Distinct()
            .ToArray();
    }

    private static Rect ClampRectToBounds(Rect rect, Size bounds)
    {
        var x = Math.Clamp(rect.X, 0, Math.Max(0, bounds.Width - 1));
        var y = Math.Clamp(rect.Y, 0, Math.Max(0, bounds.Height - 1));
        var width = Math.Clamp(rect.Width, 1, bounds.Width - x);
        var height = Math.Clamp(rect.Height, 1, bounds.Height - y);
        return new Rect(x, y, width, height);
    }

    private static Rect ExpandEyeRegionToFace(Rect eyeRegion, Size bounds)
    {
        var width = (int)Math.Round(eyeRegion.Width * 2.2);
        var height = (int)Math.Round(eyeRegion.Height * 3.2);
        var x = (int)Math.Round(eyeRegion.X - eyeRegion.Width * 0.6);
        var y = (int)Math.Round(eyeRegion.Y - eyeRegion.Height * 1.2);

        return ClampRectToBounds(new Rect(x, y, width, height), bounds);
    }

    private static double ComputeFaceScore(Rect[] faces, Size frameSize)
    {
        if (faces.Length == 0)
        {
            return 0;
        }

        var frameArea = frameSize.Width * frameSize.Height;
        var best = faces.Max(face => face.Width * face.Height);
        return best / (double)Math.Max(1, frameArea);
    }

    private static double ComputeCentrality(Rect[] faces, Size frameSize)
    {
        if (faces.Length == 0)
        {
            return 0.5;
        }

        var centerX = frameSize.Width / 2.0;
        var centerY = frameSize.Height / 2.0;
        var targetPoints = new[]
        {
            new Point2d(frameSize.Width * 1.0 / 3.0, frameSize.Height * 1.0 / 3.0),
            new Point2d(frameSize.Width * 2.0 / 3.0, frameSize.Height * 1.0 / 3.0),
            new Point2d(frameSize.Width * 1.0 / 3.0, frameSize.Height * 2.0 / 3.0),
            new Point2d(frameSize.Width * 2.0 / 3.0, frameSize.Height * 2.0 / 3.0)
        };

        var bestFace = faces.OrderByDescending(f => f.Width * f.Height).First();
        var faceCenter = new Point2d(bestFace.X + bestFace.Width / 2.0, bestFace.Y + bestFace.Height / 2.0);
        var minDistance = targetPoints.Min(p => Math.Sqrt(Math.Pow(p.X - faceCenter.X, 2) + Math.Pow(p.Y - faceCenter.Y, 2)));
        var maxDistance = Math.Sqrt(centerX * centerX + centerY * centerY);
        var score = 1.0 - Math.Clamp(minDistance / Math.Max(1e-5, maxDistance), 0, 1);
        return score;
    }

    private static double ComputeClutter(Mat gray, Rect[] faces)
    {
        using var edges = new Mat();
        Cv2.Canny(gray, edges, 100, 200);
        if (faces.Length > 0)
        {
            foreach (var face in faces)
            {
                var roi = new Rect(Math.Max(face.X - 5, 0), Math.Max(face.Y - 5, 0), Math.Min(face.Width + 10, gray.Width), Math.Min(face.Height + 10, gray.Height));
                Cv2.Rectangle(edges, roi, Scalar.Black, -1);
            }
        }

        var nonZero = Cv2.CountNonZero(edges);
        return nonZero / (double)(edges.Rows * edges.Cols);
    }

    private double ComputeOverlaySafety(Mat gray, Rect[] faces, IEnumerable<OverlayZone> zones)
    {
        if (zones == null)
        {
            return 1.0;
        }

        double penalty = 0.0;
        foreach (var zone in zones)
        {
            var rect = ToPixelRect(gray.Size(), zone);
            penalty += ComputeZonePenalty(gray, rect, faces);
        }

        var normalizedPenalty = Math.Min(1.0, penalty / Math.Max(1, zones.Count()));
        return Math.Pow(1.0 - normalizedPenalty, _config.OverlayPenaltyPower);
    }

    private static double ComputeZonePenalty(Mat gray, Rect roi, Rect[] faces)
    {
        using var zone = new Mat(gray, roi);
        using var sobel = new Mat();
        Cv2.Sobel(zone, sobel, MatType.CV_16S, 1, 1);
        Cv2.MeanStdDev(sobel, out _, out var std);
        var edgesStd = std.Val0; // Correct way to access the value

        var faceOverlap = faces.Any(face => IntersectionOverUnion(face, roi) > 0.1) ? 1.0 : 0.0;
        var busyScore = Math.Min(1.0, edgesStd / 100.0);
        return (busyScore + faceOverlap) / 2.0;
    }


    private static double IntersectionOverUnion(Rect a, Rect b)
    {
        var intersection = a & b;
        var intersectionArea = (double)(intersection.Width * intersection.Height);
        if (intersectionArea <= 0)
        {
            return 0;
        }

        var unionArea = a.Width * a.Height + b.Width * b.Height - intersectionArea;
        return intersectionArea / Math.Max(1, unionArea);
    }

    private static Rect ToPixelRect(Size size, OverlayZone zone)
    {
        var x = (int)Math.Round(zone.X * size.Width);
        var y = (int)Math.Round(zone.Y * size.Height);
        var width = (int)Math.Round(zone.Width * size.Width);
        var height = (int)Math.Round(zone.Height * size.Height);
        x = Math.Clamp(x, 0, Math.Max(0, size.Width - 1));
        y = Math.Clamp(y, 0, Math.Max(0, size.Height - 1));
        width = Math.Clamp(width, 1, size.Width - x);
        height = Math.Clamp(height, 1, size.Height - y);
        return new Rect(x, y, width, height);
    }

    private double ComputeMotion(Mat gray)
    {
        if (_previousGray == null)
        {
            _previousGray = gray.Clone();
            return 0;
        }

        using var diff = new Mat();
        Cv2.Absdiff(gray, _previousGray, diff);
        Cv2.MeanStdDev(diff, out _, out var std);

        // Dispose of the previous frame before replacing it
        _previousGray.Dispose();
        _previousGray = gray.Clone();

        // Access the value correctly using Val0
        return std.Val0;
    }


    private static double ComputeTimestampPrior(double timestamp, double duration)
    {
        if (duration <= 0)
        {
            return 0.5;
        }

        var normalized = Math.Clamp(timestamp / duration, 0, 1);
        var penalty = Math.Max(0, 1 - Math.Abs(normalized - 0.5) * 2);
        return penalty;
    }

    public Dictionary<double, List<FrameMetrics>> GroupByTimestamp(IEnumerable<FrameMetrics> frames) => frames
        .GroupBy(f => f.TimeSec)
        .ToDictionary(g => g.Key, g => g.ToList());

    public void Dispose()
    {
        _frontalCascade?.Dispose();
        _profileCascade?.Dispose();
        _glassesCascade?.Dispose();
        _smileCascade?.Dispose();
        _previousGray?.Dispose();
    }
}
