using CommandLine;

namespace ThumbPick.Configuration;

public class AppOptions
{
    [Option('i', "input", Required = true, HelpText = "Input video file path.")]
    public string InputPath { get; init; } = string.Empty;

    [Option('p', "preset", Required = true, HelpText = "Preset name.")]
    public string Preset { get; init; } = string.Empty;

    [Option("fps", HelpText = "Frames per second to sample.")]
    public double? FramesPerSecond { get; init; }

    [Option("fpm", HelpText = "Frames per minute to sample.")]
    public double? FramesPerMinute { get; init; }

    [Option('t', "top", Default = 6, HelpText = "Number of top candidates to select.")]
    public int Top { get; init; }

    [Option('n', "neighbors", Default = 2, HelpText = "Neighbor count per candidate.")]
    public int Neighbors { get; init; }

    [Option('o', "out", Required = true, HelpText = "Output directory.")]
    public string OutputDirectory { get; init; } = string.Empty;

    [Option('c', "config", HelpText = "Path to preset override JSON.")]
    public string? ConfigOverridePath { get; init; }

    [Option("preset-dir", HelpText = "Directory containing preset JSON files.")]
    public string? PresetDirectory { get; init; }

    [Option("neighbor-frames", HelpText = "Override neighbor frame indices.")]
    public IEnumerable<int>? NeighborOffsets { get; init; }

    [Option("weights", HelpText = "Inline JSON weights override.")]
    public string? InlineWeightsJson { get; init; }

    [Option("ffmpeg", HelpText = "Path to ffmpeg executable when not co-located with the app.")]
    public string? FfmpegPath { get; init; }

    public string? ResolvedFfmpegPath { get; private set; }

    public double? ExplicitSampleRate => FramesPerSecond ?? (FramesPerMinute.HasValue ? FramesPerMinute / 60.0 : null);

    public void Validate()
    {
        if (!File.Exists(InputPath))
        {
            throw new FileNotFoundException($"Video file not found: {InputPath}");
        }

        if (FramesPerSecond.HasValue && FramesPerMinute.HasValue)
        {
            throw new ArgumentException("Specify either fps or fpm, not both.");
        }

        if (ExplicitSampleRate.HasValue && ExplicitSampleRate <= 0)
        {
            throw new ArgumentException("Sampling rate must be positive.");
        }

        if (Top <= 0)
        {
            throw new ArgumentException("Top must be greater than zero.");
        }

        if (Neighbors < 0)
        {
            throw new ArgumentException("Neighbors cannot be negative.");
        }

        if (!string.IsNullOrWhiteSpace(PresetDirectory) && !Directory.Exists(PresetDirectory))
        {
            throw new DirectoryNotFoundException($"Preset directory not found: {PresetDirectory}");
        }

        if (!Directory.Exists(OutputDirectory))
        {
            Directory.CreateDirectory(OutputDirectory);
        }

        ResolveFfmpeg();
    }

    private void ResolveFfmpeg()
    {
        if (!string.IsNullOrWhiteSpace(FfmpegPath))
        {
            var ffmpegFullPath = Path.GetFullPath(FfmpegPath);
            if (!File.Exists(ffmpegFullPath))
            {
                throw new FileNotFoundException($"ffmpeg not found at {ffmpegFullPath}");
            }

            ResolvedFfmpegPath = ffmpegFullPath;
            return;
        }

        var exeName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var coLocated = Path.Combine(AppContext.BaseDirectory, exeName);
        if (File.Exists(coLocated))
        {
            ResolvedFfmpegPath = coLocated;
            return;
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            return;
        }

        foreach (var segment in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(segment.Trim(), exeName);
                if (File.Exists(candidate))
                {
                    ResolvedFfmpegPath = candidate;
                    return;
                }
            }
            catch
            {
                // ignore malformed path entries
            }
        }
    }
}
