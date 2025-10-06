using CommandLine;
using ThumbPick.Configuration;
using ThumbPick.Core;
using ThumbPick.IO;
using ThumbPick.Metrics;
using ThumbPick.Models;

namespace ThumbPick;

public static class Program
{
    public static int Main(string[] args)
    {
        return Parser.Default.ParseArguments<AppOptions>(args)
            .MapResult(Run, errs => 1);
    }

    private static int Run(AppOptions options)
    {
        try
        {
            options.Validate();

            var presetProvider = new PresetProvider(options.PresetDirectory);
            var preset = presetProvider.ResolvePreset(options.Preset, options.ConfigOverridePath);
            if (!string.IsNullOrWhiteSpace(options.InlineWeightsJson))
            {
                presetProvider.ApplyInlineWeights(preset, options.InlineWeightsJson);
            }

            var sampler = new VideoSampler();
            var metricsEngine = new MetricsEngine(new MetricsConfiguration());
            var ranker = new CandidateRanker();
            var neighborFetcher = new NeighborFetcher();
            var writer = new ManifestWriter();

            using var session = new PipelineSession(options, preset, sampler, metricsEngine, ranker, neighborFetcher, writer);
            session.Execute();

            Console.WriteLine($"Processing complete. Manifest written to {session.ManifestPath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }
}
