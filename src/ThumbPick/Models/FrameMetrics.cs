using OpenCvSharp;

namespace ThumbPick.Models;

public class FrameMetrics : IDisposable
{
    public double TimeSec { get; set; }
    public Mat? Frame { get; set; }
    public Mat? Downscaled { get; set; }

    public double RawSharpness { get; set; }
    public double RawExposure { get; set; }
    public double RawContrast { get; set; }
    public double RawColorfulness { get; set; }
    public double RawFaceScore { get; set; }
    public double RawCentrality { get; set; }
    public double RawClutter { get; set; }
    public double RawOverlaySafe { get; set; }
    public double RawMotion { get; set; }
    public double RawTimePrior { get; set; }

    public double Sharpness { get; set; }
    public double Exposure { get; set; }
    public double Contrast { get; set; }
    public double Colorfulness { get; set; }
    public double FaceScore { get; set; }
    public double Centrality { get; set; }
    public double Clutter { get; set; }
    public double OverlaySafe { get; set; }
    public double Motion { get; set; }
    public double TimePrior { get; set; }
    public double FinalScore { get; set; }

    public Rect[] Faces { get; set; } = Array.Empty<Rect>();

    public string? SavedPath { get; set; }

    public bool HasFrame => Frame is not null;

    public void Dispose() => Dispose(true);

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        Frame?.Dispose();
        Frame = null;
        Downscaled?.Dispose();
        Downscaled = null;
    }
}
