using System.Text.Json;
using System.Text.Json.Serialization;

namespace ThumbPick.Configuration;

public class PresetDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("requireFace")]
    public bool? RequireFace { get; set; }

    [JsonIgnore]
    public bool RequireFaceResolved => RequireFace ?? false;

    [JsonPropertyName("sampling")]
    public SamplingPolicy Sampling { get; set; } = new();

    [JsonPropertyName("weights")]
    public WeightConfig Weights { get; set; } = new();

    [JsonPropertyName("thresholds")]
    public ThresholdConfig Thresholds { get; set; } = new();

    [JsonPropertyName("overlayZones")]
    public List<OverlayZone> OverlayZones { get; set; } = new();
}

public class SamplingPolicy
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "fps";

    [JsonPropertyName("value")]
    public double Value { get; set; } = 2.0;
}

public class WeightConfig
{
    [JsonPropertyName("sharp")]
    public double Sharp { get; set; } = 0.2;

    [JsonPropertyName("exposure")]
    public double Exposure { get; set; } = 0.1;

    [JsonPropertyName("contrast")]
    public double Contrast { get; set; } = 0.1;

    [JsonPropertyName("color")]
    public double Color { get; set; } = 0.1;

    [JsonPropertyName("face")]
    public double Face { get; set; } = 0.2;

    [JsonPropertyName("centrality")]
    public double Centrality { get; set; } = 0.1;

    [JsonPropertyName("clutter")]
    public double Clutter { get; set; } = 0.05;

    [JsonPropertyName("overlay")]
    public double Overlay { get; set; } = 0.05;

    [JsonPropertyName("motion")]
    public double Motion { get; set; } = 0.05;

    [JsonPropertyName("time")]
    public double Time { get; set; } = 0.05;
}

public class ThresholdConfig
{
    [JsonPropertyName("sharpMin")]
    public double SharpMin { get; set; } = 50.0;

    [JsonPropertyName("Lmin")]
    public double LMin { get; set; } = 15.0;

    [JsonPropertyName("Lmax")]
    public double LMax { get; set; } = 240.0;

    [JsonPropertyName("temporalMinGapSec")]
    public double TemporalMinGapSec { get; set; } = 2.0;

    [JsonPropertyName("appearanceMinDist")]
    public double AppearanceMinDist { get; set; } = 0.15;
}

public class OverlayZone
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("w")]
    public double Width { get; set; }

    [JsonPropertyName("h")]
    public double Height { get; set; }

    [JsonPropertyName("anchor")]
    public string Anchor { get; set; } = "absolute";
}

public sealed class PresetProvider
{
    private readonly string _presetDirectory;
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public PresetProvider(string? presetDirectory)
    {
        _presetDirectory = presetDirectory ?? Path.Combine(AppContext.BaseDirectory, "presets");
    }

    public PresetDefinition ResolvePreset(string presetName, string? overridePath)
    {
        var preset = LoadPresetFromFile(presetName);

        if (!string.IsNullOrEmpty(overridePath))
        {
            if (!File.Exists(overridePath))
            {
                throw new FileNotFoundException($"Override file not found: {overridePath}");
            }

            using var overrideStream = File.OpenRead(overridePath);
            var overridePreset = JsonSerializer.Deserialize<PresetDefinition>(overrideStream, _serializerOptions);
            if (overridePreset != null)
            {
                MergePreset(preset, overridePreset);
            }
        }

        return preset;
    }

    public void ApplyInlineWeights(PresetDefinition preset, string weightsJson)
    {
        if (string.IsNullOrWhiteSpace(weightsJson))
        {
            return;
        }

        var overrides = JsonSerializer.Deserialize<Dictionary<string, double>>(weightsJson, _serializerOptions);
        if (overrides == null)
        {
            return;
        }

        foreach (var kvp in overrides)
        {
            var key = kvp.Key.ToLowerInvariant();
            var value = kvp.Value;
            switch (key)
            {
                case "sharp":
                    preset.Weights.Sharp = value;
                    break;
                case "exposure":
                    preset.Weights.Exposure = value;
                    break;
                case "contrast":
                    preset.Weights.Contrast = value;
                    break;
                case "color":
                    preset.Weights.Color = value;
                    break;
                case "face":
                    preset.Weights.Face = value;
                    break;
                case "centrality":
                    preset.Weights.Centrality = value;
                    break;
                case "clutter":
                    preset.Weights.Clutter = value;
                    break;
                case "overlay":
                    preset.Weights.Overlay = value;
                    break;
                case "motion":
                    preset.Weights.Motion = value;
                    break;
                case "time":
                    preset.Weights.Time = value;
                    break;
            }
        }
    }

    private PresetDefinition LoadPresetFromFile(string presetName)
    {
        var presetPath = Path.Combine(_presetDirectory, $"{presetName.ToLowerInvariant()}.json");
        if (!File.Exists(presetPath))
        {
            throw new FileNotFoundException($"Preset file not found: {presetPath}");
        }

        using var stream = File.OpenRead(presetPath);
        var preset = JsonSerializer.Deserialize<PresetDefinition>(stream, _serializerOptions);
        if (preset == null)
        {
            throw new InvalidOperationException($"Failed to deserialize preset {presetName}.");
        }

        if (string.IsNullOrWhiteSpace(preset.Name))
        {
            preset.Name = presetName;
        }

        return preset;
    }

    private static void MergePreset(PresetDefinition target, PresetDefinition source)
    {
        if (source.RequireFace.HasValue)
        {
            target.RequireFace = source.RequireFace;
        }
        if (source.Sampling != null)
        {
            target.Sampling = source.Sampling;
        }
        if (source.Weights != null)
        {
            MergeWeights(target.Weights, source.Weights);
        }
        if (source.Thresholds != null)
        {
            MergeThresholds(target.Thresholds, source.Thresholds);
        }
        if (source.OverlayZones?.Count > 0)
        {
            target.OverlayZones = source.OverlayZones;
        }
    }

    private static void MergeWeights(WeightConfig target, WeightConfig source)
    {
        target.Sharp = source.Sharp != default ? source.Sharp : target.Sharp;
        target.Exposure = source.Exposure != default ? source.Exposure : target.Exposure;
        target.Contrast = source.Contrast != default ? source.Contrast : target.Contrast;
        target.Color = source.Color != default ? source.Color : target.Color;
        target.Face = source.Face != default ? source.Face : target.Face;
        target.Centrality = source.Centrality != default ? source.Centrality : target.Centrality;
        target.Clutter = source.Clutter != default ? source.Clutter : target.Clutter;
        target.Overlay = source.Overlay != default ? source.Overlay : target.Overlay;
        target.Motion = source.Motion != default ? source.Motion : target.Motion;
        target.Time = source.Time != default ? source.Time : target.Time;
    }

    private static void MergeThresholds(ThresholdConfig target, ThresholdConfig source)
    {
        target.SharpMin = source.SharpMin != default ? source.SharpMin : target.SharpMin;
        target.LMin = source.LMin != default ? source.LMin : target.LMin;
        target.LMax = source.LMax != default ? source.LMax : target.LMax;
        target.TemporalMinGapSec = source.TemporalMinGapSec != default ? source.TemporalMinGapSec : target.TemporalMinGapSec;
        target.AppearanceMinDist = source.AppearanceMinDist != default ? source.AppearanceMinDist : target.AppearanceMinDist;
    }
}
