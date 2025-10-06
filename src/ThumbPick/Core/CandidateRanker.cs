using OpenCvSharp;
using ThumbPick.Configuration;
using ThumbPick.Models;

namespace ThumbPick.Core;

public sealed class CandidateRanker
{
    public IReadOnlyList<FrameMetrics> SelectTopCandidates(IEnumerable<FrameMetrics> frames, PresetDefinition preset, int limit)
    {
        var ordered = frames
            .OrderByDescending(f => f.FinalScore)
            .ToList();

        var selected = new List<FrameMetrics>();
        foreach (var candidate in ordered)
        {
            if (selected.Count >= limit)
            {
                break;
            }

            if (ViolatesTemporalSpacing(candidate, selected, preset.Thresholds.TemporalMinGapSec))
            {
                continue;
            }

            if (ViolatesAppearanceDiversity(candidate, selected, preset.Thresholds.AppearanceMinDist))
            {
                continue;
            }

            selected.Add(candidate);
        }

        return selected;
    }

    private static bool ViolatesTemporalSpacing(FrameMetrics candidate, IEnumerable<FrameMetrics> selected, double minGap)
    {
        return selected.Any(s => Math.Abs(s.TimeSec - candidate.TimeSec) < minGap);
    }

    private static bool ViolatesAppearanceDiversity(FrameMetrics candidate, IEnumerable<FrameMetrics> selected, double minDist)
    {
        foreach (var existing in selected)
        {
            var dist = ComputeAppearanceDistance(candidate.Downscaled, existing.Downscaled, candidate.Faces, existing.Faces);
            if (dist < minDist)
            {
                return true;
            }
        }

        return false;
    }

    private static double ComputeAppearanceDistance(Mat? a, Mat? b, Rect[] facesA, Rect[] facesB)
    {
        if (a == null || b == null)
        {
            return 1.0;
        }

        using var resizedA = ResizeToHistogramInput(a);
        using var resizedB = ResizeToHistogramInput(b);
        using var ycrcbA = new Mat();
        using var ycrcbB = new Mat();
        Cv2.CvtColor(resizedA, ycrcbA, ColorConversionCodes.BGR2YCrCb);
        Cv2.CvtColor(resizedB, ycrcbB, ColorConversionCodes.BGR2YCrCb);

        var channelsA = ycrcbA.Split();
        var channelsB = ycrcbB.Split();

        var distance = 0.0;
        for (var i = 0; i < channelsA.Length; i++)
        {
            using var histA = new Mat();
            using var histB = new Mat();
            int[] histSize = { 32 };
            Rangef[] ranges = { new(0, 256) };
            Cv2.CalcHist(new[] { channelsA[i] }, new[] { 0 }, null, histA, 1, histSize, ranges);
            Cv2.CalcHist(new[] { channelsB[i] }, new[] { 0 }, null, histB, 1, histSize, ranges);
            Cv2.Normalize(histA, histA, 1, 0, NormTypes.L1);
            Cv2.Normalize(histB, histB, 1, 0, NormTypes.L1);
            distance += 1.0 - Cv2.CompareHist(histA, histB, HistCompMethods.Correl);
        }

        foreach (var ch in channelsA) ch.Dispose();
        foreach (var ch in channelsB) ch.Dispose();

        var faceOverlap = ComputeFaceOverlap(facesA, facesB);
        return (distance / channelsA.Length + (1.0 - faceOverlap)) / 2.0;
    }

    private static Mat ResizeToHistogramInput(Mat input)
    {
        var size = new Size(64, 64);
        var dst = new Mat();
        Cv2.Resize(input, dst, size);
        return dst;
    }

    private static double ComputeFaceOverlap(Rect[] facesA, Rect[] facesB)
    {
        if (facesA.Length == 0 || facesB.Length == 0)
        {
            return 0;
        }

        var bestA = facesA.OrderByDescending(f => f.Width * f.Height).First();
        var bestB = facesB.OrderByDescending(f => f.Width * f.Height).First();
        var intersection = bestA & bestB;
        if (intersection.Width <= 0 || intersection.Height <= 0)
        {
            return 0;
        }

        var union = bestA.Width * bestA.Height + bestB.Width * bestB.Height - intersection.Width * intersection.Height;
        return (intersection.Width * intersection.Height) / (double)union;
    }
}
