using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Forms = System.Windows.Forms;
using Microsoft.Win32;
using ThumbPick.Configuration;
using ThumbPick.Core;
using ThumbPick.IO;
using ThumbPick.Metrics;

namespace ThumbPick.Gui;

public partial class MainWindow : Window
{
    private readonly PresetProvider _presetProvider;
    private bool _isProcessing;

    public MainWindow()
    {
        InitializeComponent();
        _presetProvider = new PresetProvider(null);
        LoadPresets();
        InitializeDefaults();
    }

    private void LoadPresets()
    {
        var presets = _presetProvider.ListPresets();
        if (presets.Count == 0)
        {
            StatusTextBlock.Text = "No presets found. Ensure the presets directory is present.";
            RunButton.IsEnabled = false;
            return;
        }

        PresetComboBox.ItemsSource = presets;
        PresetComboBox.SelectedIndex = 0;
    }

    private void InitializeDefaults()
    {
        try
        {
            var defaultOutput = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ThumbPick");
            OutputDirectoryTextBox.Text = defaultOutput;
            if (!Directory.Exists(defaultOutput))
            {
                Directory.CreateDirectory(defaultOutput);
            }
        }
        catch
        {
            // ignore if we cannot create the default directory
        }
    }

    private void Window_DragEnter(object sender, DragEventArgs e) => HandleDragEnter(e);

    private void DropZone_DragEnter(object sender, DragEventArgs e) => HandleDragEnter(e);

    private void HandleDragEnter(DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e) => HandleDrop(e);

    private void DropZone_Drop(object sender, DragEventArgs e) => HandleDrop(e);

    private void HandleDrop(DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
        {
            return;
        }

        var file = files.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(file))
        {
            return;
        }

        VideoPathTextBox.Text = file;
        StatusTextBlock.Text = $"Selected {Path.GetFileName(file)}";
    }

    private void BrowseVideo_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Video Files (*.mp4;*.mov;*.mkv)|*.mp4;*.mov;*.mkv|All Files (*.*)|*.*",
            Title = "Select video file"
        };

        if (dialog.ShowDialog() == true)
        {
            VideoPathTextBox.Text = dialog.FileName;
            StatusTextBlock.Text = $"Selected {Path.GetFileName(dialog.FileName)}";
        }
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Select output directory",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(OutputDirectoryTextBox.Text)
                ? OutputDirectoryTextBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            OutputDirectoryTextBox.Text = dialog.SelectedPath;
        }
    }

    private void BrowseFfmpeg_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = OperatingSystem.IsWindows() ? "ffmpeg executable (ffmpeg.exe)|ffmpeg.exe|All Files (*.*)|*.*" : "ffmpeg|ffmpeg",
            Title = "Locate ffmpeg executable"
        };

        if (dialog.ShowDialog() == true)
        {
            FfmpegPathTextBox.Text = dialog.FileName;
        }
    }

    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isProcessing)
        {
            return;
        }

        try
        {
            _isProcessing = true;
            RunButton.IsEnabled = false;
            StatusTextBlock.Text = "Preparing...";

            AppOptions options;
            try
            {
                options = BuildOptions();
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
                return;
            }

            string? manifestPath = null;

            await Task.Run(() =>
            {
                options.Validate();
                if (!string.IsNullOrWhiteSpace(options.ResolvedFfmpegPath))
                {
                    UpdateFfmpegText(options.ResolvedFfmpegPath!);
                }

                var presetProvider = new PresetProvider(options.PresetDirectory);
                var preset = presetProvider.ResolvePreset(options.Preset, options.ConfigOverridePath);

                var sampler = new VideoSampler();
                var metricsEngine = new MetricsEngine(new MetricsConfiguration());
                var ranker = new CandidateRanker();
                var neighborFetcher = new NeighborFetcher();
                var writer = new ManifestWriter();

                using var session = new PipelineSession(options, preset, sampler, metricsEngine, ranker, neighborFetcher, writer);
                session.Execute();
                manifestPath = session.ManifestPath;
            });

            StatusTextBlock.Text = manifestPath == null
                ? "Processing completed."
                : $"Processing completed. Manifest: {manifestPath}";
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            RunButton.IsEnabled = true;
            _isProcessing = false;
        }
    }

    private AppOptions BuildOptions()
    {
        if (string.IsNullOrWhiteSpace(VideoPathTextBox.Text))
        {
            throw new InvalidOperationException("Select a video file before running the pipeline.");
        }

        if (PresetComboBox.SelectedItem is not PresetDefinition preset)
        {
            throw new InvalidOperationException("Choose a preset before running the pipeline.");
        }

        if (string.IsNullOrWhiteSpace(OutputDirectoryTextBox.Text))
        {
            throw new InvalidOperationException("Select an output directory.");
        }

        if (!double.TryParse(SamplingValueTextBox.Text, out var samplingValue) || samplingValue <= 0)
        {
            throw new InvalidOperationException("Sampling value must be a positive number.");
        }

        if (!int.TryParse(TopCountTextBox.Text, out var top) || top <= 0)
        {
            throw new InvalidOperationException("Top picks must be a positive integer.");
        }

        if (!int.TryParse(NeighborCountTextBox.Text, out var neighbors) || neighbors < 0)
        {
            throw new InvalidOperationException("Neighbor count must be zero or greater.");
        }

        var mode = (SamplingModeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "fps";
        var ffmpegPath = string.IsNullOrWhiteSpace(FfmpegPathTextBox.Text) ? null : FfmpegPathTextBox.Text;

        return new AppOptions
        {
            InputPath = VideoPathTextBox.Text,
            Preset = preset.Name,
            FramesPerSecond = mode == "fps" ? samplingValue : null,
            FramesPerMinute = mode == "fpm" ? samplingValue : null,
            Top = top,
            Neighbors = neighbors,
            OutputDirectory = OutputDirectoryTextBox.Text,
            PresetDirectory = _presetProvider.PresetDirectoryPath,
            FfmpegPath = ffmpegPath
        };
    }

    private void ShowError(string message)
    {
        Dispatcher.Invoke(() =>
        {
            StatusTextBlock.Text = message;
            MessageBox.Show(this, message, "ThumbPick", MessageBoxButton.OK, MessageBoxImage.Error);
        });
    }

    private void UpdateFfmpegText(string path)
    {
        Dispatcher.Invoke(() =>
        {
            if (string.IsNullOrWhiteSpace(FfmpegPathTextBox.Text))
            {
                FfmpegPathTextBox.Text = path;
            }
        });
    }
}
