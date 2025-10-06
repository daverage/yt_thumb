using OpenCvSharp;

namespace ThumbPick.Models;

public readonly record struct VideoMetadata(
    string Path,
    double DurationSeconds,
    double FrameRate,
    int Width,
    int Height)
{
    public static VideoMetadata FromCapture(string path, VideoCapture capture)
    {
        var frameCount = capture.Get(VideoCaptureProperties.FrameCount);
        var fps = capture.Get(VideoCaptureProperties.Fps);
        if (fps <= 0)
        {
            fps = 30.0;
        }

        var duration = frameCount > 0 ? frameCount / fps : 0.0;
        var width = (int)capture.Get(VideoCaptureProperties.FrameWidth);
        var height = (int)capture.Get(VideoCaptureProperties.FrameHeight);

        return new VideoMetadata(path, duration, fps, width, height);
    }
}
