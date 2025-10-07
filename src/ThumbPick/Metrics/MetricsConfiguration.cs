using System.IO;

namespace ThumbPick.Metrics;

public enum FaceDetectionMode
{
    Default,
    Glasses,
    Smile
}

public class MetricsConfiguration
{
    public string CascadeDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "cascades");
    public string FrontalCascadeName { get; set; } = "haarcascade_frontalface_default.xml";
    public string ProfileCascadeName { get; set; } = "haarcascade_profileface.xml";
    public string GlassesCascadeName { get; set; } = "haarcascade_eye_tree_eyeglasses.xml";
    public string SmileCascadeName { get; set; } = "haarcascade_smile.xml";
    public FaceDetectionMode FaceDetector { get; set; } = FaceDetectionMode.Default;
    public int AnalysisWidth { get; set; } = 640;
    public double OverlayPenaltyPower { get; set; } = 1.0;
}
