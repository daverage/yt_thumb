namespace ThumbPick.Metrics;

public class MetricsConfiguration
{
    public string CascadeDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "cascades");
    public string FrontalCascadeName { get; set; } = "haarcascade_frontalface_default.xml";
    public string ProfileCascadeName { get; set; } = "haarcascade_profileface.xml";
    public int AnalysisWidth { get; set; } = 640;
    public double OverlayPenaltyPower { get; set; } = 1.0;
}
