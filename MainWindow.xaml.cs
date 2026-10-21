using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ProtopteraPlayer;

public partial class MainWindow : Window
{
    /// <summary>Форматы, которые умеет встроенный MediaPlayer (FLAC — через Media Foundation, Windows 10+).</summary>
    public static readonly string[] SupportedExtensions = { ".mp3", ".flac", ".wav" };

    public static bool IsSupported(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private readonly MediaPlayer _player = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    private bool _hasMedia;
    private bool _isPlaying;
    private bool _isDraggingSeek;
    private bool _pinned;

    public MainWindow()
    {
        InitializeComponent();

        _player.Volume = 1.0;
        _player.MediaOpened += (_, __) => { UpdateProgress(); UpdateButtons(); };
        _player.MediaEnded  += Player_MediaEnded;
        _player.MediaFailed += Player_MediaFailed;

        _timer.Tick += (_, __) => { if (!_isDraggingSeek) UpdateProgress(); };
        SeekTrack.SizeChanged += (_, __) => UpdateProgress(reset: !_hasMedia);

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Space) { TogglePlayPause(); e.Handled = true; }
        };

        UpdateButtons();
    }

    /// <summary>Открыть и сразу воспроизвести файл. Open() заменяет текущий трек.</summary>
    public void OpenFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        try
        {
            _player.Open(new Uri(path, UriKind.Absolute));
            TrackTitle.Text = Path.GetFileName(path);
            _hasMedia = true;
            LoadTags(path);
            Play();
        }
        catch
        {
            TrackTitle.Text = "Ошибка открытия файла";
            _hasMedia = false;
            ShowNoCover();
            ShowFormat(null, "");
            UpdateButtons();
        }
    }

    /// <summary>
    /// Читает теги через TagLib: формат/параметры потока и вшитую обложку
    /// (ID3 APIC у mp3/wav, METADATA_BLOCK_PICTURE у flac).
    /// </summary>
    private void LoadTags(string path)
    {
        ShowFormat(Path.GetExtension(path).TrimStart('.').ToUpperInvariant(), "");
        try
        {
            using var tf = TagLib.File.Create(path);
            ShowFormat(Path.GetExtension(path).TrimStart('.').ToUpperInvariant(), DescribeAudio(tf.Properties));

            var pics = tf.Tag.Pictures;
            if (pics != null && pics.Length > 0)
            {
                var pic = pics.FirstOrDefault(p => p.Type == TagLib.PictureType.FrontCover) ?? pics[0];
                byte[] bytes = pic.Data.Data;

                if (bytes != null && bytes.Length > 0)
                {
                    var bmp = new BitmapImage();
                    using var ms = new MemoryStream(bytes);
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;   // грузим сразу, поток можно закрыть
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();

                    CoverBrush.ImageSource = bmp;
                    CoverPlaceholder.Visibility = Visibility.Collapsed;
                    return;
                }
            }
        }
        catch
        {
            // повреждённый/неподдерживаемый тег — просто покажем плейсхолдер
        }

        ShowNoCover();
    }

    /// <summary>«44.1 kHz · 16 bit» для lossless, «44.1 kHz · 320 kbps» для mp3.</summary>
    private static string DescribeAudio(TagLib.Properties? p)
    {
        if (p == null || p.AudioSampleRate <= 0) return "";
        string rate = $"{p.AudioSampleRate / 1000.0:0.#} kHz".Replace(',', '.');
        if (p.BitsPerSample > 0) return $"{rate} · {p.BitsPerSample} bit";
        if (p.AudioBitrate > 0)  return $"{rate} · {p.AudioBitrate} kbps";
        return rate;
    }

    private void ShowFormat(string? format, string info)
    {
        FormatBadge.Visibility = string.IsNullOrEmpty(format) ? Visibility.Collapsed : Visibility.Visible;
        FormatText.Text = format ?? "";
        AudioInfo.Text = info;
    }

    private void ShowNoCover()
    {
        CoverBrush.ImageSource = null;
        CoverPlaceholder.Visibility = Visibility.Visible;
    }

    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = _pinned;
    }

    // ---- воспроизведение ----
    private void Play()
    {
        if (!_hasMedia) return;
        _player.Play();
        _isPlaying = true;
        _timer.Start();
        UpdateButtons();
    }

    private void Pause()
    {
        if (!_hasMedia) return;
        _player.Pause();
        _isPlaying = false;
        UpdateButtons();
    }

    private void TogglePlayPause()
    {
        if (_isPlaying) Pause(); else Play();
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e) => TogglePlayPause();

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (!_hasMedia) return;
        _player.Stop();
        _isPlaying = false;
        _timer.Stop();
        UpdateProgress(reset: true);
        UpdateButtons();
    }

    private void Player_MediaEnded(object? sender, EventArgs e)
    {
        _player.Stop();
        _isPlaying = false;
        _timer.Stop();
        UpdateProgress(reset: true);
        UpdateButtons();
    }

    private void Player_MediaFailed(object? sender, ExceptionEventArgs e)
    {
        TrackTitle.Text = "Не удалось воспроизвести файл";
        _hasMedia = false;
        _isPlaying = false;
        _timer.Stop();
        ShowNoCover();
        ShowFormat(null, "");
        UpdateProgress(reset: true);
        UpdateButtons();
    }

    // ---- прогресс ----
    private void UpdateProgress(bool reset = false)
    {
        TimeSpan pos = TimeSpan.Zero, dur = TimeSpan.Zero;
        double ratio = 0;

        if (!reset && _hasMedia && _player.NaturalDuration.HasTimeSpan)
        {
            dur = _player.NaturalDuration.TimeSpan;
            pos = _player.Position;
            if (dur.TotalSeconds > 0)
                ratio = Math.Clamp(pos.TotalSeconds / dur.TotalSeconds, 0, 1);
        }

        SetSeekVisual(ratio);
        TimeText.Text = $"{Fmt(pos)} / {Fmt(dur)}";
    }

    private void SetSeekVisual(double ratio)
    {
        double x = ratio * SeekTrack.ActualWidth;
        SeekFill.Width = x;
        SeekThumb.Margin = new Thickness(x - SeekThumb.Width / 2, 0, 0, 0);
        SeekThumb.Visibility = _hasMedia ? Visibility.Visible : Visibility.Hidden;
    }

    private static string Fmt(TimeSpan t) => $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";

    // ---- перемотка ----
    private void SeekTrack_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_hasMedia) return;
        _isDraggingSeek = true;
        SeekTrack.CaptureMouse();
        SeekToX(e.GetPosition(SeekTrack).X);
    }

    private void SeekTrack_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isDraggingSeek && e.LeftButton == MouseButtonState.Pressed)
            SeekToX(e.GetPosition(SeekTrack).X);
    }

    private void SeekTrack_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingSeek) return;
        _isDraggingSeek = false;
        SeekTrack.ReleaseMouseCapture();
    }

    private void SeekToX(double x)
    {
        if (!_hasMedia || !_player.NaturalDuration.HasTimeSpan) return;
        double w = SeekTrack.ActualWidth;
        if (w <= 0) return;

        double ratio = Math.Clamp(x / w, 0, 1);
        TimeSpan dur = _player.NaturalDuration.TimeSpan;
        _player.Position = TimeSpan.FromSeconds(dur.TotalSeconds * ratio);

        SetSeekVisual(ratio);
        TimeText.Text = $"{Fmt(_player.Position)} / {Fmt(dur)}";
    }

    private void UpdateButtons()
    {
        PlayPauseButton.IsEnabled = _hasMedia;
        StopButton.IsEnabled      = _hasMedia;
        PlayIcon.Visibility  = _isPlaying ? Visibility.Collapsed : Visibility.Visible;
        PauseIcon.Visibility = _isPlaying ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- окно ----
    private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        _pinned = !_pinned;
        Topmost = _pinned;
        var brush = (Brush)FindResource(_pinned ? "AccentBrush" : "MutedBrush");
        PinHead.Fill = brush;
        PinNeedle.Fill = brush;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ---- drag & drop ----
    private void Window_DragOver(object sender, DragEventArgs e)
    {
        bool ok = e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Any(IsSupported);
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        var file = files.FirstOrDefault(IsSupported);
        if (file != null) OpenFile(file);
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        _player.Close();
        base.OnClosed(e);
    }
}
