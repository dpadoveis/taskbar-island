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
    double settingsHeight;

    /// <summary>Short UI transitions run at 60 fps (the widget's steady animations run at 20).</summary>
    static T Smooth<T>(T a) where T : Timeline { Timeline.SetDesiredFrameRate(a, 60); return a; }

    void OnGear(object sender, RoutedEventArgs e)
    {
        settingsOpen = !settingsOpen;
        var d = TimeSpan.FromMilliseconds(300);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        // the window is already tall enough: only the panel inside grows, the window never resizes
        SettingsPanel.BeginAnimation(HeightProperty, Smooth(new DoubleAnimation(settingsOpen ? settingsHeight : 0, d) { EasingFunction = ease }));
        SettingsPanel.BeginAnimation(OpacityProperty, Smooth(new DoubleAnimation(settingsOpen ? 1 : 0, TimeSpan.FromMilliseconds(settingsOpen ? 300 : 160))
            { BeginTime = TimeSpan.FromMilliseconds(settingsOpen ? 60 : 0) }));
        GearSpin.BeginAnimation(RotateTransform.AngleProperty, Smooth(new DoubleAnimation(settingsOpen ? 90 : 0, d) { EasingFunction = ease }));
    }

    /// <summary>Is this screen point (physical px) on the visible card itself (not the transparent space above)?</summary>
    public bool CardContains(int x, int y)
    {
        try
        {
            var p = Card.PointFromScreen(new Point(x, y));
            return p.X >= 0 && p.Y >= 0 && p.X <= Card.ActualWidth && p.Y <= Card.ActualHeight;
        }
        catch { return false; }
    }

    public CardWindow()
    {
        InitializeComponent();
        TextLook.Apply(Title); TextLook.Apply(Sub); TextLook.Apply(Elapsed); TextLook.Apply(Remaining);
        Eq1.Fill = Eq2.Fill = Eq3.Fill = Eq4.Fill = accent;
        ProgressFill.Background = accent;
        PlayPause.Data = MainWindow.PauseGeo;
        PrevButton.ToolTip = S.Prev; ToggleButton.ToolTip = S.PlayPause; NextButton.ToolTip = S.Next;
        Cover.ToolTip = S.OpenSpotify;
        Cover.MouseLeftButtonUp += (_, _) => OpenSpotify?.Invoke();

        ShuffleButton.ToolTip = S.Shuffle; RepeatButton.ToolTip = S.Repeat;
        ShuffleDot.Fill = RepeatDot.Fill = accent;
        WireSeek();

        GameLabel.Text = S.GameMode;
        ColorLabel.Text = S.Color;
        GearButton.ToolTip = S.SettingsTip;
        GearButton.Foreground = new SolidColorBrush(Color.FromArgb(0x80, 255, 255, 255));
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

        // shuffle / repeat: hidden if Spotify doesn't expose them; accent + dot when on
        ShuffleButton.Visibility = m.Shuffle == null ? Visibility.Collapsed : Visibility.Visible;
        SetToggleLook(ShuffleButton, ShuffleDot, m.Shuffle == true);
        RepeatButton.Visibility = m.Repeat == null ? Visibility.Collapsed : Visibility.Visible;
        RepeatGlyph.Text = m.Repeat == RepeatMode.Track ? "" : ""; // repeat one / repeat all
        SetToggleLook(RepeatButton, RepeatDot, m.Repeat is RepeatMode.List or RepeatMode.Track);

        canSeek = m.CanSeek;
        SeekArea.Cursor = canSeek ? Cursors.Hand : Cursors.Arrow;
        duration = m.Duration;
        SetProgress(m.Position, m.Duration);
    }

    void SetToggleLook(Button b, Ellipse dot, bool on)
    {
        b.Foreground = on ? accent : new SolidColorBrush(Color.FromArgb(0xB3, 255, 255, 255));
        dot.Visibility = on ? Visibility.Visible : Visibility.Hidden;
    }

    public void SetProgress(double pos, double dur)
    {
        if (seeking) return; // the user is dragging: don't fight them
        ShowProgress(dur > 0 ? Math.Clamp(pos / dur, 0, 1) : 0, pos, dur);
    }

    void ShowProgress(double frac, double pos, double dur)
    {
        double w = ProgressTrack.ActualWidth > 0 ? ProgressTrack.ActualWidth : Card.Width - 28;
        ProgressFill.Width = w * frac;
        SeekKnob.Margin = new Thickness(Math.Clamp(w * frac - 6, -6, w - 6), 0, 0, 0);
        Elapsed.Text = Fmt(pos);
        Remaining.Text = dur > 0 ? "-" + Fmt(Math.Max(0, dur - pos)) : "";
    }

    // ---------- click / drag to seek ----------
    public event Action<double>? Seek;
    bool canSeek, seeking;
    double duration;

    void WireSeek()
    {
        double FracAt(MouseEventArgs e) => ProgressTrack.ActualWidth > 0
            ? Math.Clamp(e.GetPosition(ProgressTrack).X / ProgressTrack.ActualWidth, 0, 1) : 0;

        SeekArea.MouseEnter += (_, _) => { if (canSeek) SeekKnob.Opacity = 1; };
        SeekArea.MouseLeave += (_, _) => { if (!seeking) SeekKnob.Opacity = 0; };
        SeekArea.MouseLeftButtonDown += (_, e) =>
        {
            if (!canSeek || duration <= 0) return;
            seeking = true;
            SeekArea.CaptureMouse();
            SeekKnob.Opacity = 1;
            var f = FracAt(e); ShowProgress(f, f * duration, duration);
            e.Handled = true;
        };
        SeekArea.MouseMove += (_, e) =>
        {
            if (!seeking) return;
            var f = FracAt(e); ShowProgress(f, f * duration, duration);
        };
        SeekArea.MouseLeftButtonUp += (_, e) =>
        {
            if (!seeking) return;
            seeking = false;
            SeekArea.ReleaseMouseCapture();
            if (!SeekArea.IsMouseOver) SeekKnob.Opacity = 0;
            Seek?.Invoke(FracAt(e) * duration);
        };
        SeekArea.LostMouseCapture += (_, _) => seeking = false;
    }

    /// <summary>True while the user drags the seek bar (keeps the card open even if the cursor leaves it).</summary>
    public bool IsSeeking => seeking;

    static string Fmt(double s) { var t = TimeSpan.FromSeconds(Math.Max(0, s)); return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : $"{(int)t.TotalMinutes}:{t.Seconds:00}"; }

    /// <summary>Pop the card up, horizontally centred on centerX (DIP), its bottom edge at bottomY (DIP).</summary>
    public void ShowAt(double centerX, double bottomY, Rect workArea)
    {
        if (Height is double.NaN || settingsHeight == 0)
        {
            // size the window once for "settings open": card (closed) + settings panel + 10px margins
            SettingsContent.Measure(new Size(Card.Width - 28, double.PositiveInfinity));
            settingsHeight = Math.Ceiling(SettingsContent.DesiredSize.Height);
            Card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Height = Math.Ceiling(Card.DesiredSize.Height) + settingsHeight + 2;
        }
        if (!IsVisible) Show();
        UpdateLayout();
        double left = Math.Clamp(centerX - ActualWidth / 2, workArea.Left + 4, workArea.Right - ActualWidth - 4);
        Left = Math.Round(left);
        Top = Math.Round(bottomY - Height);
        if (open) return;
        open = true;
        var spring = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut };
        var d = TimeSpan.FromMilliseconds(340);
        BeginAnimation(OpacityProperty, Smooth(new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))));
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, Smooth(new DoubleAnimation(0.9, 1, d) { EasingFunction = spring }));
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, Smooth(new DoubleAnimation(0.9, 1, d) { EasingFunction = spring }));
        CardShift.BeginAnimation(TranslateTransform.YProperty, Smooth(new DoubleAnimation(12, 0, d) { EasingFunction = spring }));
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
