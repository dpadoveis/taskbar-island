using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using TaskbarIsland.Core;

namespace TaskbarIsland;

/// <summary>Expanded "now playing" card that pops above the taskbar while the island is hovered.</summary>
public partial class CardWindow : Window
{
    public event Action<string>? Command;
    public event Action? OpenSpotify;
    public event Action<bool>? GameModeChanged;
    public event Action<string>? ColorChosen;
    /// <summary>Raised once the card has finished hiding.</summary>
    public event Action? Collapsed;

    readonly SolidColorBrush accent = new(Accent.NeutralAccent);
    readonly Dictionary<string, Ellipse> rings = new();
    bool open, playing, gameMode = true, settingsOpen;
    double anchorBottom;

    void OnGear(object sender, RoutedEventArgs e)
    {
        settingsOpen = !settingsOpen;
        SettingsContent.Measure(new Size(Card.Width - 28, double.PositiveInfinity));
        var d = TimeSpan.FromMilliseconds(320);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        SettingsPanel.BeginAnimation(HeightProperty, new DoubleAnimation(settingsOpen ? SettingsContent.DesiredSize.Height : 0, d) { EasingFunction = ease });
        SettingsPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(settingsOpen ? 1 : 0, TimeSpan.FromMilliseconds(settingsOpen ? 320 : 150)));
        GearSpin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(settingsOpen ? 90 : 0, d) { EasingFunction = ease });
    }

    public CardWindow()
    {
        InitializeComponent();
        Eq1.Fill = Eq2.Fill = Eq3.Fill = Eq4.Fill = accent;
        ProgressFill.Background = accent;
        PlayPause.Data = MainWindow.PauseGeo;
        PrevButton.ToolTip = S.Prev; ToggleButton.ToolTip = S.PlayPause; NextButton.ToolTip = S.Next;
        Cover.ToolTip = S.OpenSpotify;
        Cover.MouseLeftButtonUp += (_, _) => OpenSpotify?.Invoke();

        GameLabel.Text = S.GameMode;
        ColorLabel.Text = S.Color;
        GearButton.ToolTip = S.SettingsTip;
        GearButton.Foreground = new SolidColorBrush(Color.FromArgb(0x80, 255, 255, 255));
        SizeChanged += (_, _) => { if (open) Top = Math.Round(anchorBottom - ActualHeight); }; // grow upwards
        GameRow.ToolTip = S.GameModeTip;
        GameRow.MouseLeftButtonUp += (_, _) => { SetGameMode(!gameMode, animate: true); GameModeChanged?.Invoke(gameMode); };

        // "auto" swatch: a little rainbow = colour follows the cover
        var rainbow = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        foreach (var (c, o) in new[] { ("#FF7A59", 0.0), ("#C2349B", 0.5), ("#2B59C3", 1.0) })
            rainbow.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(c), o));
        AddSwatch("auto", rainbow, S.ColorAuto);
        for (int i = 0; i < Settings.Presets.Length; i++)
            AddSwatch(Settings.Presets[i], new SolidColorBrush((Color)ColorConverter.ConvertFromString(Settings.Presets[i])), S.ColorNames[i]);
    }

    void AddSwatch(string key, Brush fill, string tip)
    {
        var ring = new Ellipse { Width = 18, Height = 18, Stroke = Brushes.White, StrokeThickness = 1.5, Opacity = 0 };
        var dot = new Ellipse { Width = 12, Height = 12, Fill = fill };
        var g = new Grid { Width = 20, Height = 20, Margin = new Thickness(1, 0, 1, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = tip };
        g.Children.Add(ring); g.Children.Add(dot);
        g.MouseLeftButtonUp += (_, _) => { SetColor(key); ColorChosen?.Invoke(key); };
        rings[key] = ring;
        Swatches.Children.Add(g);
    }

    public void SetGameMode(bool on, bool animate = false)
    {
        gameMode = on;
        var d = TimeSpan.FromMilliseconds(animate ? 220 : 0);
        KnobShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(on ? 14 : 0, d) { EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut } });
        Switch.Background = on ? accent : new SolidColorBrush(Color.FromArgb(0x33, 255, 255, 255));
    }

    public void SetColor(string key)
    {
        foreach (var (k, r) in rings) r.Opacity = string.Equals(k, key, StringComparison.OrdinalIgnoreCase) ? 0.9 : 0;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Win32.MakeToolNoActivate(new WindowInteropHelper(this).Handle);
    }

    public bool IsOpen => open;

    void OnControl(object sender, RoutedEventArgs e) => Command?.Invoke((string)((Button)sender).Tag);

    public void SetMedia(MediaInfo m, Brush? cover, Color accentColor, Color tint)
    {
        Title.Text = m.Title;
        Sub.Text = m.Album.Length > 0 && m.Album != m.Title ? $"{m.Artist} · {m.Album}" : m.Artist;
        PlayPause.Data = m.Playing ? MainWindow.PauseGeo : MainWindow.PlayGeo;
        Cover.Background = cover ?? new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
        accent.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(accentColor, TimeSpan.FromMilliseconds(500)));
        TintStop.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(tint, TimeSpan.FromMilliseconds(500)));
        if (m.Playing != playing) { playing = m.Playing; if (open) Animate(); }
        SetProgress(m.Position, m.Duration);
    }

    public void SetProgress(double pos, double dur)
    {
        double w = ProgressTrack.ActualWidth > 0 ? ProgressTrack.ActualWidth : Card.Width - 28;
        ProgressFill.Width = dur > 0 ? w * Math.Clamp(pos / dur, 0, 1) : 0;
        Elapsed.Text = Fmt(pos);
        Remaining.Text = dur > 0 ? "-" + Fmt(Math.Max(0, dur - pos)) : "";
    }

    static string Fmt(double s) { var t = TimeSpan.FromSeconds(Math.Max(0, s)); return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : $"{(int)t.TotalMinutes}:{t.Seconds:00}"; }

    /// <summary>Pop the card up, horizontally centred on centerX (DIP), its bottom edge at bottomY (DIP).</summary>
    public void ShowAt(double centerX, double bottomY, Rect workArea)
    {
        if (!IsVisible) Show();
        UpdateLayout();
        double left = Math.Clamp(centerX - ActualWidth / 2, workArea.Left + 4, workArea.Right - ActualWidth - 4);
        Left = Math.Round(left);
        anchorBottom = bottomY;
        Top = Math.Round(bottomY - ActualHeight);
        if (open) return;
        open = true;
        var spring = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut };
        var d = TimeSpan.FromMilliseconds(340);
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.9, 1, d) { EasingFunction = spring });
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.9, 1, d) { EasingFunction = spring });
        CardShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, d) { EasingFunction = spring });
        Animate();
    }

    public void Collapse(bool instant = false)
    {
        if (!open) return;
        open = false;
        StopEq();
        if (instant) { Hide(); Collapsed?.Invoke(); return; }
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(130));
        fade.Completed += (_, _) => { if (!open) { Hide(); Collapsed?.Invoke(); } };
        BeginAnimation(OpacityProperty, fade);
        CardShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(6, TimeSpan.FromMilliseconds(130)));
    }

    void Animate()
    {
        StopEq();
        if (!playing) return;
        var bars = new[] { (Eq1, 0.50, 0.0), (Eq2, 0.40, 0.45), (Eq3, 0.575, 0.55), (Eq4, 0.45, 0.75) };
        foreach (var (r, secs, delay) in bars)
            ((ScaleTransform)r.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(MainWindow.EqLo, 1, TimeSpan.FromSeconds(secs))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(delay),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            });
    }

    void StopEq()
    {
        foreach (var r in new[] { Eq1, Eq2, Eq3, Eq4 })
        {
            var st = (ScaleTransform)r.RenderTransform;
            st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            st.ScaleY = MainWindow.EqLo;
        }
    }
}
