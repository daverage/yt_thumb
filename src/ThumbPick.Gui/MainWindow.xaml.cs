using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Reflection;
using Forms = System.Windows.Forms;
using Microsoft.Win32;
using ThumbPick.Configuration;
using ThumbPick.Core;
using ThumbPick.IO;
using ThumbPick.Metrics;
using OpenCvSharp;

namespace ThumbPick.Gui;

public enum ThumbnailSelectionState
{
    Undecided,
    Good,
    Bad
}

public partial class MainWindow : System.Windows.Window
{
    private readonly string _logPath;
    private readonly string _presetDirectory;
    private readonly PresetProvider _presetProvider;
    private readonly Dictionary<string, CascadeClassifier> _faceDetectors = new();
    private ObservableCollection<ThumbnailViewModel> _thumbnails = new();
    public ObservableCollection<ThumbnailViewModel> Thumbnails
    {
        get => _thumbnails;
        set
        {
            if (_thumbnails != value)
            {
                _thumbnails = value;
            }
        }
    }
    private bool _isProcessing;
    private string? _currentManifestPath;

    private sealed class PresetListItem
    {
        public string Key { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
    }

    public sealed class ThumbnailViewModel : INotifyPropertyChanged
    {
        private ThumbnailSelectionState _selection;

        private ThumbnailViewModel(string imagePath, double score)
        {
            ImagePath = imagePath;
            Score = score;
            Thumbnail = LoadBitmap(imagePath);
            _selection = ThumbnailSelectionState.Undecided;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string ImagePath { get; }

        public double Score { get; }

        public string DisplayName => Path.GetFileName(ImagePath) ?? ImagePath;

        public string ScoreText => $"Score: {Score:F3}";

        public BitmapImage Thumbnail { get; }

        public ThumbnailSelectionState Selection
        {
            get => _selection;
            set
            {
                if (_selection != value)
                {
                    _selection = value;
                    OnPropertyChanged();
                }
            }
        }

        public static ThumbnailViewModel FromManifest(string imagePath, double score)
        {
            return new ThumbnailViewModel(imagePath, score);
        }

        private static BitmapImage LoadBitmap(string path)
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                image.DecodePixelWidth = 200;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch
            {
                return new BitmapImage();
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public MainWindow()
    {
        _logPath = Path.Combine(AppContext.BaseDirectory, "startup.log");

        try
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            ContentRendered += MainWindow_ContentRendered;
            DataContext = this;
            ThumbnailListView.ItemsSource = _thumbnails;
            TrySetFfmpegPath();

            _presetDirectory = "d:\\GitHub\\yt_thumb\\src\\ThumbPick.Gui\\bin\\Debug\\net8.0-windows\\presets";
            Directory.CreateDirectory(_presetDirectory);
            _presetProvider = new PresetProvider(_presetDirectory);

            LoadPresets();
            LoadFaceDetectors();
            File.AppendAllText(_logPath, $"[{DateTime.Now:O}] MainWindow..ctor exited\n");
        }
        catch (Exception ex)
        {
            File.AppendAllText(_logPath, $"[{DateTime.Now:O}] Exception in MainWindow constructor: {ex.ToString()}\n");
            System.Windows.Application.Current.Shutdown();
        }
    }

    private void TrySetFfmpegPath()
    {
        var ffmpegPath = FindExecutable("ffmpeg.exe");
        if (ffmpegPath is not null)
        {
            FfmpegPathTextBox.Text = ffmpegPath;
            FfmpegPathTextBox.IsEnabled = false;
        }
    }

    private static string? FindExecutable(string executableName)
    {
        var paths = Environment.GetEnvironmentVariable("PATH");
        if (paths is null)
        {
            return null;
        }
        foreach (var path in paths.Split(Path.PathSeparator))
        {
            var fullPath = Path.Combine(path, executableName);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }
        return null;
    }

    private void MainWindow_ContentRendered(object? sender, EventArgs e)
    {
        File.AppendAllText(_logPath, $"[{DateTime.Now:O}] MainWindow_ContentRendered entered\n");
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        File.AppendAllText(_logPath, $"[{DateTime.Now:O}] MainWindow_Loaded entered\n");
    }

    private void LoadFaceDetectors()
    {
        var haarCascadesDir = ResolveHaarCascadesDirectory();
        if (haarCascadesDir is null || !Directory.Exists(haarCascadesDir))
        {
            return;
        }

        var cascadeFiles = Directory.GetFiles(haarCascadesDir, "*.xml");

        foreach (var cascadeFile in cascadeFiles)
        {
            var key = Path.GetFileNameWithoutExtension(cascadeFile);
            var success = _faceDetectors.TryAdd(key, new CascadeClassifier(cascadeFile));
        }

        Dispatcher.Invoke(() =>
        {
            FaceDetectorComboBox.ItemsSource = _faceDetectors.Keys;
            if (_faceDetectors.Any())
            {
                FaceDetectorComboBox.SelectedIndex = 0;
            }
        });
    }

    private string ResolveHaarCascadesDirectory()
    {
        var cascadeDir = "d:\\GitHub\\yt_thumb\\src\\ThumbPick.Gui\\bin\\Debug\\net8.0-windows\\cascades";
        Directory.CreateDirectory(cascadeDir);
        return cascadeDir;
    }

    private void LoadPresets()
    {
        var presetFiles = _presetProvider.GetPresetFiles();
        var presets = new List<PresetListItem>();

        foreach (var presetFile in presetFiles)
        {
            try
            {
                var json = File.ReadAllText(presetFile);
                var preset = JsonSerializer.Deserialize<PresetDefinition>(json);
                if (preset != null)
                {
                    presets.Add(new PresetListItem { Key = Path.GetFileNameWithoutExtension(presetFile), Name = preset.Name });
                }
            }
            catch (Exception)
            {
                // Log or handle preset loading errors
            }
        }

        Dispatcher.Invoke(() =>
        {
            PresetComboBox.ItemsSource = presets;
            PresetComboBox.DisplayMemberPath = "Name";
            PresetComboBox.SelectedValuePath = "Key";

            if (presets.Any())
            {
                PresetComboBox.SelectedIndex = 0;
            }
        });
    }

    private string ResolvePresetDirectory()
    {
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        var path = Path.GetDirectoryName(assemblyLocation);
        if (string.IsNullOrEmpty(path))
        {
            throw new DirectoryNotFoundException("Could not determine the assembly path.");
        }

        var searchPaths = new[]
        {
            Path.Combine(path, "..", "..", "..", "presets"),
            Path.Combine(path, "..", "presets"),
            Path.Combine(path, "presets")
        };

        foreach (var searchPath in searchPaths)
        {
            if (Directory.Exists(searchPath))
            {
                return searchPath;
            }
        }

        throw new DirectoryNotFoundException("Could not resolve the 'presets' directory.");
    }

    private void Window_DragEnter(object sender, System.Windows.DragEventArgs e) => HandleDragEnter(e);

    private void DropZone_DragEnter(object sender, System.Windows.DragEventArgs e) => HandleDragEnter(e);

    private void HandleDragEnter(System.Windows.DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(System.Windows.DataFormats.FileDrop) == true)
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
        }
        else
        {
            e.Effects = System.Windows.DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void Window_Drop(object sender, System.Windows.DragEventArgs e) => HandleDrop(e);

    private void DropZone_Drop(object sender, System.Windows.DragEventArgs e) => HandleDrop(e);

    private void HandleDrop(System.Windows.DragEventArgs e)
    {
        if (e.Data?.GetData(System.Windows.DataFormats.FileDrop) is not string[] files || files.Length == 0)
        {
            return;
        }

        var file = files.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(file))
        {
            return;
        }

        VideoPathTextBox.Text = file;
        if (File.Exists(file))
        {
            OutputDirectoryTextBox.Text = Path.GetDirectoryName(file);
        }
        StatusTextBlock.Text = $"Selected {Path.GetFileName(file)}";
    }

    private void BrowseVideo_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Video Files (*.mp4;*.mov;*.mkv)|*.mp4;*.mov;*.mkv|All Files (*.*)|*.*",
            Title = "Select video file"
        };

        if (dialog.ShowDialog() == true)
        {
            VideoPathTextBox.Text = dialog.FileName;
            OutputDirectoryTextBox.Text = Path.GetDirectoryName(dialog.FileName);
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
        var dialog = new Microsoft.Win32.OpenFileDialog
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

        ClearThumbnails();

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

        try
        {
            _isProcessing = true;
            RunButton.IsEnabled = false;
            DownloadButton.IsEnabled = false;
            StatusTextBlock.Text = "Processing...";
            ProgressDetailTextBlock.Text = string.Empty;
            ProcessingProgressBar.Visibility = Visibility.Visible;
            ProcessingProgressBar.IsIndeterminate = true;
            ProcessingProgressBar.Value = 0;

            IProgress<PipelineProgress> progress = new Progress<PipelineProgress>(ReportProgress);
            string? manifestPath = null;
            var faceDetectionMode = ParseFaceDetectionMode((string)FaceDetectorComboBox.SelectedValue);

            await Task.Run(() =>
            {
                ClearOutputDirectory(options.OutputDirectory);
                options.Validate();

                var presetProvider = new PresetProvider(options.PresetDirectory);
                var preset = presetProvider.ResolvePreset(options.Preset, options.ConfigOverridePath);

                var metricsConfig = new MetricsConfiguration();
                metricsConfig.FaceDetector = faceDetectionMode;

                var sampler = new VideoSampler();
                var metricsEngine = new MetricsEngine(metricsConfig);
                if (metricsEngine.Warnings.Count > 0)
                {
                    foreach (var warning in metricsEngine.Warnings)
                    {
                        progress.Report(new PipelineProgress("Configuration warning", 0, 1, warning));
                    }
                }

                var ranker = new CandidateRanker();
                var neighborFetcher = new NeighborFetcher();
                var writer = new ManifestWriter();

                using var session = new PipelineSession(options, preset, sampler, metricsEngine, ranker, neighborFetcher, writer);
                session.Execute(progress);
                manifestPath = session.ManifestPath;
            });

            _currentManifestPath = manifestPath;
            StatusTextBlock.Text = manifestPath == null
                ? "Processing completed."
                : $"Processing completed. Manifest: {manifestPath}";

            LoadThumbnails(manifestPath);
        }
        catch (Exception ex)
        {
            ClearThumbnails();
            ShowError(ex.Message);
        }
        finally
        {
            ResetProgress();
            RunButton.IsEnabled = true;
            DownloadButton.IsEnabled = _thumbnails.Count > 0;
            _isProcessing = false;
        }
    }

    private void ClearThumbnails()
    {
        _thumbnails.Clear();
        _currentManifestPath = null;
        DownloadButton.IsEnabled = false;
    }

    private void LoadThumbnails(string? manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        {
            ProgressDetailTextBlock.Text = "Manifest not found.";
            DownloadButton.IsEnabled = false;
            return;
        }

        try
        {
            using var stream = File.OpenRead(manifestPath);
            var manifest = JsonSerializer.Deserialize<Manifest>(stream);
            if (manifest?.Top == null || manifest.Top.Count == 0)
            {
                ProgressDetailTextBlock.Text = "No candidate thumbnails were produced.";
                DownloadButton.IsEnabled = false;
                return;
            }

            var newThumbnails = new ObservableCollection<ThumbnailViewModel>();
            foreach (var entry in manifest.Top)
            {
                if (string.IsNullOrWhiteSpace(entry.Path) || !File.Exists(entry.Path))
                {
                    continue;
                }

                newThumbnails.Add(ThumbnailViewModel.FromManifest(entry.Path, entry.Score));
            }

            _thumbnails = newThumbnails;
            ThumbnailListView.ItemsSource = _thumbnails;

            ProgressDetailTextBlock.Text = _thumbnails.Count > 0
                ? $"{_thumbnails.Count} candidate thumbnails ready."
                : "No candidate thumbnails were produced.";
            DownloadButton.IsEnabled = _thumbnails.Count > 0;
        }
        catch (Exception ex)
        {
            ProgressDetailTextBlock.Text = "Failed to load manifest.";
            ShowError($"Failed to load manifest: {ex.Message}");
        }
    }

    private void ReportProgress(PipelineProgress progress)
    {
        Dispatcher.Invoke(() =>
        {
            ProcessingProgressBar.Visibility = Visibility.Visible;
            if (progress.Maximum <= 0)
            {
                ProcessingProgressBar.IsIndeterminate = true;
            }
            else
            {
                ProcessingProgressBar.IsIndeterminate = false;
                var ratio = Math.Clamp(progress.Value / progress.Maximum, 0, 1);
                ProcessingProgressBar.Value = ratio;
            }

            StatusTextBlock.Text = progress.Stage;
            if (!string.IsNullOrWhiteSpace(progress.Detail))
            {
                ProgressDetailTextBlock.Text = progress.Detail;
            }
            else if (progress.Maximum > 0)
            {
                ProgressDetailTextBlock.Text = $"{progress.Stage} ({progress.Value:0}/{progress.Maximum:0})";
            }
            else
            {
                ProgressDetailTextBlock.Text = progress.Stage;
            }
        });
    }

    private void ResetProgress()
    {
        ProcessingProgressBar.Visibility = Visibility.Collapsed;
        ProcessingProgressBar.IsIndeterminate = false;
        ProcessingProgressBar.Value = 0;
        ProgressDetailTextBlock.Text = string.Empty;
    }

    private static void ClearOutputDirectory(string outputDirectory)
    {
        if (!Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(outputDirectory))
        {
            if (Directory.Exists(entry))
            {
                Directory.Delete(entry, true);
            }
            else
            {
                File.Delete(entry);
            }
        }

        Directory.CreateDirectory(outputDirectory);
    }

    private void DownloadSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = _thumbnails.Where(t => t.Selection == ThumbnailSelectionState.Good).ToList();
        if (selected.Count == 0)
        {
            StatusTextBlock.Text = "Mark at least one thumbnail as Good before downloading.";
            return;
        }

        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Choose a folder to copy selected thumbnails",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() != Forms.DialogResult.OK)
        {
            return;
        }

        try
        {
            var targetDirectory = dialog.SelectedPath;
            Directory.CreateDirectory(targetDirectory);

            foreach (var item in selected)
            {
                var destination = Path.Combine(targetDirectory, Path.GetFileName(item.ImagePath) ?? "thumbnail.png");
                File.Copy(item.ImagePath, destination, overwrite: true);
            }

            StatusTextBlock.Text = $"Copied {selected.Count} thumbnail(s) to {targetDirectory}.";
        }
        catch (Exception ex)
        {
            ShowError($"Failed to copy thumbnails: {ex.Message}");
        }
    }

    private void OpenThumbnail_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button)
        {
            return;
        }

        if (button.CommandParameter is not ThumbnailViewModel viewModel)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = viewModel.ImagePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowError($"Unable to open thumbnail: {ex.Message}");
        }
    }

    private AppOptions BuildOptions()
    {
        if (string.IsNullOrWhiteSpace(VideoPathTextBox.Text))
        {
            throw new InvalidOperationException("Select a video file before running the pipeline.");
        }

        if (PresetComboBox.SelectedItem is not PresetListItem preset)
        {
            throw new InvalidOperationException("Choose a preset before running the pipeline.");
        }

        if (string.IsNullOrWhiteSpace(OutputDirectoryTextBox.Text))
        {
            throw new InvalidOperationException("Select an output directory.");
        }

        var videoFileName = Path.GetFileNameWithoutExtension(VideoPathTextBox.Text);
        var outputDirectory = Path.Combine(OutputDirectoryTextBox.Text, "thumbpick", videoFileName);
        Directory.CreateDirectory(outputDirectory);

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

        var options = new AppOptions
        {
            InputPath = VideoPathTextBox.Text,
            Preset = preset.Key,
            FramesPerSecond = mode == "fps" ? samplingValue : null,
            FramesPerMinute = mode == "fpm" ? samplingValue : null,
            Top = top,
            Neighbors = neighbors,
            OutputDirectory = outputDirectory,
            PresetDirectory = _presetDirectory,
            ConfigOverridePath = null
        };

        try
        {
            File.AppendAllText("path-log.txt", $"Video Path: {options.InputPath}\n");
        }
        catch (Exception ex)
        {
            File.AppendAllText("path-log.txt", $"Error logging path: {ex.Message}\n");
        }

        return options;
    }

    private void ShowError(string message)
    {
        Dispatcher.Invoke(() =>
        {
            StatusTextBlock.Text = message;
            System.Windows.MessageBox.Show(this, message, "ThumbPick", MessageBoxButton.OK, MessageBoxImage.Error);
        });
    }

    private FaceDetectionMode ParseFaceDetectionMode(string selectedValue)
    {
        if (selectedValue.Contains("frontalface", StringComparison.OrdinalIgnoreCase))
        {
            return FaceDetectionMode.Default;
        }
        if (selectedValue.Contains("eye_tree_eyeglasses", StringComparison.OrdinalIgnoreCase))
        {
            return FaceDetectionMode.Glasses;
        }
        if (selectedValue.Contains("smile", StringComparison.OrdinalIgnoreCase))
        {
            return FaceDetectionMode.Smile;
        }
        return FaceDetectionMode.Default; // Fallback
    }
}
