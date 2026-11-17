using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using TaskbarIsland.Core;
using Path = System.Windows.Shapes.Path;

namespace TaskbarIsland;

public partial class MainWindow : Window
{
    enum Mode { Closed, Compact, Normal }

    const double TextW = 190;          // 10px gap + 180px text
    const double SlotW = 36;           // 10px gap + equalizer
    const int CompactAfterSeconds = 30;
    internal const double EqLo = 5.0 / 18;

    internal static readonly Geometry PlayGeo = Geometry.Parse("M5,2.9 L5,13.1 C5,13.9 5.9,14.4 6.6,13.9 L13.8,8.8 C14.4,8.4 14.4,7.6 13.8,7.2 L6.6,2.1 C5.9,1.6 5,2.1 5,2.9 Z");
    internal static readonly Geometry PauseGeo = Geometry.Parse("M3.6,2.6 L6.7,2.6 L6.7,13.4 L3.6,13.4 Z M9.3,2.6 L12.4,2.6 L12.4,13.4 L9.3,13.4 Z");
    static readonly Geometry BoltGeo = Geometry.Parse("M10.7,0.3 L8.2,4.6 L9.9,4.6 L9.2,9.2 L12.1,4.1 L10.4,4.1 Z");

    static readonly Dictionary<DeviceKind, string> Glyphs = new()
    {
        [DeviceKind.Keyboard] = "", [DeviceKind.Mouse] = "",
        [DeviceKind.Headset] = "", [DeviceKind.Other] = "",
    };

    public static readonly string DataDir = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskbarIsland");

    readonly CancellationTokenSource cts = new();
    readonly SpotifySource spotify;
    CardWindow? card; // created only while it is shown, then released (memory)
    bool CardOpen => card?.IsOpen == true;
    readonly SolidColorBrush accent = new(Accent.NeutralAccent);
    readonly SolidColorBrush fg = new(Colors.White);
    readonly SolidColorBrush lowBrush = new(Color.FromRgb(0xFF, 0x99, 0xA4));
    readonly SolidColorBrush boltCut = new(Color.FromRgb(0x1F, 0x1F, 0x1F));
    readonly Dictionary<string, DeviceView> devices = new();
    readonly DispatcherTimer tick;
    readonly string gamesFile = System.IO.Path.Combine(DataDir, "games.txt");

    IntPtr hwnd;
    MediaInfo? media;
    Brush? coverBrush;
    readonly Settings settings = Settings.Load();
    Color coverAccent = Accent.NeutralAccent, coverTint = Accent.NeutralTint;   // derived from the cover
    Color accentColor = Accent.NeutralAccent, tintColor = Accent.NeutralTint;   // what is shown (cover or preset)
    Mode mode = Mode.Closed;
    bool modeApplied, light, inGame, marqueeOverflow, hoverIsland, hoverCard;
    DateTime pausedSince = DateTime.Now, hoverStarted, hoverEnded;
    int gameStreak, ticks;
    HashSet<string> games = new();
    DateTime gamesStamp;

    /// <summary>Forwards provider reports (background threads) to the UI thread.</summary>
    sealed class Sink(MainWindow w) : IDeviceSink
    {
        public void Report(DeviceState s) => w.Dispatcher.BeginInvoke(() => w.OnDevice(s));
        public void Remove(string id) => w.Dispatcher.BeginInvoke(() => w.OnDeviceRemoved(id));
    }

    public MainWindow()
    {
        InitializeComponent();
        Directory.CreateDirectory(DataDir);
        Eq1.Fill = Eq2.Fill = Eq3.Fill = Eq4.Fill = accent;
        ProgressFill.Background = accent;

        // hover is polled from the real cursor position: enter/leave events are unreliable on a
        // window that never activates (a highlight could get stuck)
        new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Input, (_, _) => PollHover(), Dispatcher).Start();
        Island.MouseWheel += (_, e) => { if (media != null) spotify!.Command(e.Delta < 0 ? "next" : "prev"); };
        Art.MouseLeftButtonUp += (_, _) => { if (media == null) OpenSpotify(); else spotify!.Command("toggle"); };
        SizeChanged += (_, _) => Place();
        ProgressTrack.SizeChanged += (_, _) => UpdateProgress(); // width springs between modes
        BuildMenu();


        // automatic detection: each provider reports the devices it finds (nothing to configure)
        var sink = new Sink(this);
        new CompxProvider(sink).Start(cts.Token);   // AULA keyboards (Compx receiver)
        new XentaProvider(sink).Start(cts.Token);   // Attack Shark mice
        new GHubProvider(sink).Start(cts.Token);    // any Logitech device with a battery, via G HUB
        spotify = new SpotifySource(m => Dispatcher.BeginInvoke(() => OnMedia(m)));
        spotify.Start(cts.Token);

        FirstRun();
        ApplyTheme(Win32.TaskbarIsLight());
        LoadGames();
        UpdateMode();
        tick = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => OnTick(), Dispatcher);
        tick.Start();
        DelayedTrim(8); // once everything is loaded and drawn, give idle startup memory back to Windows
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        hwnd = new WindowInteropHelper(this).Handle;
        Win32.MakeToolNoActivate(hwnd);
        Win32.OwnByTaskbar(hwnd); // rides along with the taskbar: no more vanishing on Start / Win key
        Place();
    }

    protected override void OnClosed(EventArgs e) { cts.Cancel(); base.OnClosed(e); }

    /// <summary>On the very first launch, turn on "start with Windows" (the menu can turn it off).</summary>
    void FirstRun()
    {
        var marker = System.IO.Path.Combine(DataDir, "installed");
        if (File.Exists(marker)) return;
        try { Win32.SetAutostart(true); File.WriteAllText(marker, DateTime.Now.ToString("O")); } catch { }
    }

    static void OpenSpotify()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("spotify:") { UseShellExecute = true }); } catch { }
    }

    // ===================== devices =====================
    void OnDevice(DeviceState s)
    {
        if (!devices.TryGetValue(s.Id, out var v))
        {
            v = new DeviceView(s.Kind, s.Name, Glyphs[s.Kind], fg, lowBrush, boltCut);
            devices[s.Id] = v;
            int at = 0; // stable order: keyboard, mouse, headset, others; then by name
            foreach (Border b in Batteries.Children)
            {
                var o = (DeviceView)b.Tag;
                if (o.Kind > s.Kind || (o.Kind == s.Kind && string.CompareOrdinal(o.Name, s.Name) > 0)) break;
                at++;
            }
            Batteries.Children.Insert(at, v.Root);
        }
        v.Set(s.Battery);
    }

    void OnDeviceRemoved(string id)
    {
        if (!devices.Remove(id, out var v)) return;
        Batteries.Children.Remove(v.Root);
    }

    // ===================== media =====================
    void OnMedia(MediaInfo? m)
    {
        var prev = media;
        media = m;
        if (m == null) { UpdateMode(); return; }

        bool trackChanged = prev == null || prev.Title != m.Title || prev.Artist != m.Artist;
        if (trackChanged) SetText(m);
        if (prev == null || prev.ThumbId != m.ThumbId) SetCover(m.Thumbnail);
        if (prev == null || prev.Playing != m.Playing)
        {
            if (!m.Playing) pausedSince = DateTime.Now;
        }
        if (trackChanged || prev!.Playing != m.Playing) { UpdateMode(); ApplyPlayState(); }
        UpdateProgress();
        if (CardOpen) card!.SetMedia(m, coverBrush, accentColor, tintColor);
    }

    void SetText(MediaInfo m)
    {
        foreach (var t in new[] { T1, T2 })
        {
            t.Inlines.Clear();
            t.Inlines.Add(new Run(m.Title) { FontWeight = FontWeights.SemiBold });
            if (m.Artist.Length > 0) t.Inlines.Add(new Run(" — " + m.Artist) { Foreground = new SolidColorBrush(Color.FromArgb(0x94, 255, 255, 255)) });
        }
        T1.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        marqueeOverflow = T1.DesiredSize.Width > TextHost.Width - 12;
        T2.Visibility = marqueeOverflow ? Visibility.Visible : Visibility.Collapsed;
        TextHost.OpacityMask = marqueeOverflow ? FadeMask() : null;
        TrackShift.BeginAnimation(TranslateTransform.XProperty, null);
        TrackShift.X = 0;
    }

    static Brush FadeMask()
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        g.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
        g.GradientStops.Add(new GradientStop(Colors.Black, 0.03));
        g.GradientStops.Add(new GradientStop(Colors.Black, 0.9));
        g.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        g.Freeze();
        return g;
    }

    void SetCover(byte[]? bytes)
    {
        coverBrush = null;
        coverAccent = Accent.NeutralAccent; coverTint = Accent.NeutralTint;
        if (bytes != null)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = new MemoryStream(bytes);
                bmp.DecodePixelWidth = 128; // enough for the 64px card cover, never the full-size image
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                var brush = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
                brush.Freeze();
                coverBrush = brush;
                (coverAccent, coverTint) = Accent.FromCover(bmp);
            }
            catch { }
        }
        ArtCircle.Fill = coverBrush ?? new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
        ArtGlyph.Visibility = coverBrush == null ? Visibility.Visible : Visibility.Collapsed;
        ApplyColors();
    }

    /// <summary>Cover colour ("auto") or the user's preset; fades the island, equalizer and card to it.</summary>
    void ApplyColors()
    {
        (accentColor, tintColor) = settings.Color == "auto"
            ? (coverAccent, coverTint)
            : Accent.FromPreset((Color)ColorConverter.ConvertFromString(settings.Color));
        var d = TimeSpan.FromMilliseconds(600);
        accent.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(accentColor, d));
        if (mode != Mode.Closed) IslandBg.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(tintColor, d));
        if (media != null && CardOpen) card!.SetMedia(media, coverBrush, accentColor, tintColor);
    }

    void SetGameModeEnabled(bool on)
    {
        settings.GameMode = on;
        settings.Save();
        card?.SetGameMode(on);
        if (!on && inGame) ExitGameMode();
    }

    void UpdateProgress()
    {
        var m = media;
        double w = ProgressTrack.ActualWidth;
        ProgressFill.Width = m != null && m.Duration > 0 && w > 0 ? w * Math.Clamp(m.Position / m.Duration, 0, 1) : 0;
        if (m != null && CardOpen) card!.SetProgress(m.Position, m.Duration);
    }

    // ===================== island modes =====================
    Mode Wanted()
    {
        if (media == null) return Mode.Closed;
        // paused for a while -> stays compact (hovering opens the card but doesn't stretch the island)
        if (!media.Playing && (DateTime.Now - pausedSince).TotalSeconds >= CompactAfterSeconds)
            return Mode.Compact;
        return Mode.Normal;
    }

    void UpdateMode()
    {
        var want = Wanted();
        if (modeApplied && want == mode) return;
        mode = want;
        bool first = !modeApplied;
        modeApplied = true;

        // springy morph between sizes
        var d = TimeSpan.FromMilliseconds(first ? 0 : 480);
        var spring = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut };
        void To(FrameworkElement e, DependencyProperty p, double v) => e.BeginAnimation(p, new DoubleAnimation(v, d) { EasingFunction = spring });

        To(TextWrap, WidthProperty, mode == Mode.Normal ? TextW : 0);
        To(SlotWrap, WidthProperty, mode == Mode.Closed ? 0 : SlotW);
        ProgressTrack.BeginAnimation(OpacityProperty, new DoubleAnimation(mode == Mode.Normal ? 1 : 0, TimeSpan.FromMilliseconds(200)));
        Island.BeginAnimation(PaddingProperty, new ThicknessAnimation(new Thickness(5, 0, mode == Mode.Closed ? 5 : 12, 0), d) { EasingFunction = spring });

        if (mode == Mode.Closed)
        {
            ArtCircle.Fill = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
            ArtGlyph.Visibility = Visibility.Visible;
            Island.ToolTip = S.OpenSpotify;
            IslandBg.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Accent.NeutralTint, TimeSpan.FromMilliseconds(400)));
            StopAnimations();
            card?.Collapse();
        }
        else
        {
            Island.ToolTip = null;
            if (media != null && ArtGlyph.Visibility == Visibility.Visible && coverBrush != null) { ArtCircle.Fill = coverBrush; ArtGlyph.Visibility = Visibility.Collapsed; }
        }
    }

    void ApplyPlayState()
    {
        StopAnimations();
        if (media == null || inGame) return;
        Eq.Opacity = media.Playing ? 1 : 0.45;
        ArtCircle.Opacity = media.Playing ? 1 : 0.75;
        if (!media.Playing) return;

        // same rhythm as the original version: full cycles of 1.0 / 0.8 / 1.15 / 0.9 s, phase-shifted
        var bars = new[] { (Eq1, 0.50, 0.0), (Eq2, 0.40, 0.45), (Eq3, 0.575, 0.55), (Eq4, 0.45, 0.75) };
        foreach (var (r, secs, delay) in bars)
            ((ScaleTransform)r.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(EqLo, 1, TimeSpan.FromSeconds(secs))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(delay),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            });
        if (marqueeOverflow)
        {
            // calm marquee: rest 3 s at the start, then glide at 14 px/s
            double shift = T1.DesiredSize.Width + 32, rest = 3, secs = rest + shift / 14;
            var a = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = TimeSpan.FromSeconds(secs) };
            a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(rest))));
            a.KeyFrames.Add(new LinearDoubleKeyFrame(-shift, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(secs))));
            TrackShift.BeginAnimation(TranslateTransform.XProperty, a);
        }
    }

    void StopAnimations()
    {
        foreach (var r in new[] { Eq1, Eq2, Eq3, Eq4 })
        {
            var st = (ScaleTransform)r.RenderTransform;
            st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            st.ScaleY = EqLo;
        }
        TrackShift.BeginAnimation(TranslateTransform.XProperty, null);
        TrackShift.X = 0;
    }

    // ===================== hover + card =====================
    void PollHover()
    {
        if (!IsVisible || hwnd == IntPtr.Zero || !Win32.GetCursorPos(out var c)) return;
        Point p;
        try { p = PointFromScreen(new Point(c.X, c.Y)); } catch { return; }
        bool inWindow = p.X >= 0 && p.Y >= 0 && p.X < ActualWidth && p.Y < ActualHeight;
        bool onIsland = inWindow && Island.IsVisible && Contains(Island, p);
        bool onCard = false;
        if (CardOpen)
        {
            onCard = card!.CardContains(c.X, c.Y); // the visible card only, not the transparent space above it
        }
        foreach (var v in devices.Values) v.Highlight(inWindow && Contains(v.Root, p));

        if (onIsland && !hoverIsland) hoverStarted = DateTime.Now;
        if (hoverIsland || hoverCard) if (!onIsland && !onCard) hoverEnded = DateTime.Now;
        bool changed = onIsland != hoverIsland;
        hoverIsland = onIsland; hoverCard = onCard;
        if (changed) UpdateMode(); // hovering a compact island expands it

        // card: opens after a short dwell on the island, closes shortly after leaving island + card
        if (mode != Mode.Closed && media != null && !CardOpen && hoverIsland && (DateTime.Now - hoverStarted).TotalMilliseconds > 350)
            OpenCard();
        else if (CardOpen && !hoverIsland && !hoverCard && (DateTime.Now - hoverEnded).TotalMilliseconds > 300)
        {
            card?.Collapse();
            UpdateMode();
        }
    }

    void OpenCard()
    {
        if (media == null) return;
        EnsureCard();
        card!.SetMedia(media, coverBrush, accentColor, tintColor);
        var tb = Win32.TaskbarRect();
        if (tb == null) return;
        double s = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var islandCenter = Island.TransformToAncestor(this).Transform(new Point(Island.ActualWidth / 2, 0));
        card.ShowAt(Left + islandCenter.X, tb.Value.Top / s + 4, SystemParameters.WorkArea);
    }

    void EnsureCard()
    {
        if (card != null) return;
        var c = new CardWindow();
        c.Command += cmd => spotify!.Command(cmd);
        c.OpenSpotify += OpenSpotify;
        c.SetGameMode(settings.GameMode);
        c.SetColor(settings.Color);
        c.GameModeChanged += SetGameModeEnabled;
        c.ColorChosen += key => { settings.Color = key; settings.Save(); ApplyColors(); };
        // once hidden, throw the window away a bit later and hand its memory back to Windows
        c.Collapsed += () =>
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                if (card != c || c.IsOpen) return;
                card = null;
                c.Close();
                DelayedTrim(1);
            };
            t.Start();
        };
        card = c;
    }

    bool Contains(FrameworkElement e, Point p) =>
        e.TransformToAncestor(this).TransformBounds(new Rect(e.RenderSize)).Contains(p);

    // ===================== device view =====================
    sealed class DeviceView
    {
        public readonly Border Root;
        public readonly DeviceKind Kind;
        public readonly string Name;
        readonly Rectangle body, cap, fill;
        readonly Path bolt, boltOutline;
        readonly SolidColorBrush fg, low;
        static readonly SolidColorBrush HoverBrush = new(Color.FromArgb(0x0F, 255, 255, 255));
        bool lit;

        public DeviceView(DeviceKind kind, string name, string glyph, SolidColorBrush fg, SolidColorBrush low, SolidColorBrush cut)
        {
            Kind = kind; Name = name; this.fg = fg; this.low = low;
            var icon = new TextBlock
            {
                Text = glyph, FontFamily = (FontFamily)Application.Current.Resources["IconFont"], FontSize = 16,
                Width = 16, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = fg,
            };
            // Windows 11 tray battery redrawn at its real pixel size: 18x10 body, 1.4px cap, bolt through the top edge
            body = new Rectangle { Width = 18, Height = 10, RadiusX = 3, RadiusY = 3, StrokeThickness = 1, Stroke = fg };
            cap = new Rectangle { Width = 1.4, Height = 3.2, RadiusX = 0.6, RadiusY = 0.6, Fill = fg };
            fill = new Rectangle { Height = 6, Width = 0, RadiusX = 1.2, RadiusY = 1.2, Fill = fg };
            boltOutline = new Path { Data = BoltGeo, Stroke = cut, StrokeThickness = 2.6, StrokeLineJoin = PenLineJoin.Round, Visibility = Visibility.Collapsed };
            bolt = new Path { Data = BoltGeo, Fill = fg, Visibility = Visibility.Collapsed };
            var canvas = new Canvas { Width = 20, Height = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, -3, 0, 0) };
            Canvas.SetLeft(body, 0); Canvas.SetTop(body, 3);
            Canvas.SetLeft(cap, 18.5); Canvas.SetTop(cap, 6.4);
            Canvas.SetLeft(fill, 2); Canvas.SetTop(fill, 5);
            foreach (var el in new UIElement[] { body, cap, fill, boltOutline, bolt }) canvas.Children.Add(el);

            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(icon); row.Children.Add(canvas);
            Root = new Border { Height = 44, Padding = new Thickness(7, 0, 7, 0), CornerRadius = new CornerRadius(5), Child = row, Background = Brushes.Transparent, Opacity = 0.35, Tag = this, ToolTip = name };
        }

        public void Highlight(bool on)
        {
            if (on == lit) return;
            lit = on;
            Root.Background = on ? HoverBrush : Brushes.Transparent;
        }

        public void Set(BatteryReading? r)
        {
            bool online = r != null;
            double lvl = !online ? 0 : r!.Level is int l ? l / 100.0 : (r.Charging ? 1 : 0);
            bool isLow = online && !r!.Charging && r.Level is int lv && lv <= 20;
            var color = isLow ? low : fg;
            body.Stroke = color; cap.Fill = color; fill.Fill = color;
            fill.Width = Math.Round(14 * lvl * 2) / 2;
            bolt.Visibility = boltOutline.Visibility = online && r!.Charging ? Visibility.Visible : Visibility.Collapsed;
            Root.Opacity = online ? 1 : 0.35;
            string state = !online ? S.Asleep
                : r!.Level == null ? S.ChargingCable
                : $"{r.Level}%" + (r.Charging ? " · " + S.Charging : "");
            Root.ToolTip = $"{Name} — {state}";
        }
    }

    // ===================== placement / theme / game mode =====================
    void Place()
    {
        var tb = Win32.TaskbarRect();
        if (tb == null || ActualWidth <= 0) return;
        double s = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var r = tb.Value;
        double right = (Win32.TrayLeft() ?? (r.Right - 240 * s)) / s - 4;
        double top = r.Top / s + ((r.Bottom - r.Top) / s - Height) / 2;
        Left = Math.Round(right - ActualWidth);
        Top = Math.Round(top);
    }

    void ApplyTheme(bool isLight)
    {
        light = isLight;
        fg.Color = isLight ? Color.FromRgb(0x11, 0x11, 0x11) : Colors.White;
        lowBrush.Color = isLight ? Color.FromRgb(0xC4, 0x2B, 0x1C) : Color.FromRgb(0xFF, 0x99, 0xA4);
        boltCut.Color = isLight ? Color.FromRgb(0xEE, 0xEE, 0xEE) : Color.FromRgb(0x1F, 0x1F, 0x1F);
    }

    void OnTick()
    {
        ticks++;
        if (ticks % 10 == 0) LoadGames();
        if (ticks % 15 == 0) { bool l = Win32.TaskbarIsLight(); if (l != light) ApplyTheme(l); }

        // game mode: fullscreen app, or a listed game in the foreground (catches borderless windows)
        bool gameNow = settings.GameMode
            && (Win32.FullscreenAppRunning() || (games.Count > 0 && games.Contains(Win32.ForegroundExe())));
        gameStreak = gameNow != inGame ? gameStreak + 1 : 0;
        if (gameStreak >= 2) { gameStreak = 0; if (gameNow) EnterGameMode(); else ExitGameMode(); }
        if (inGame) return;

        if (hwnd != IntPtr.Zero)
        {
            if (Win32.OwnerOf(hwnd) != Win32.TrayHwnd()) Win32.OwnByTaskbar(hwnd); // explorer restarted
            Win32.ForceTopmost(hwnd);
        }
        Place();
        UpdateMode(); // paused long enough -> compact
        if (media is { Playing: true, Duration: > 0 })
        {
            media = media with { Position = Math.Min(media.Duration, media.Position + 1) };
            UpdateProgress();
        }
    }

    void EnterGameMode()
    {
        inGame = true;
        card?.Collapse(instant: true);
        Hide();
        StopAnimations();
        ArtCircle.Fill = null;
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        Win32.TrimWorkingSet();
    }

    void ExitGameMode()
    {
        inGame = false;
        Show();
        if (media != null) { SetCover(media.Thumbnail); ApplyPlayState(); }
        modeApplied = false; UpdateMode();
        Place();
        DelayedTrim(6);
    }

    void DelayedTrim(int seconds)
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            Win32.TrimWorkingSet();
        };
        t.Start();
    }

    void LoadGames()
    {
        try
        {
            if (!File.Exists(gamesFile)) File.WriteAllText(gamesFile, S.GamesFileHeader);
            var stamp = File.GetLastWriteTimeUtc(gamesFile);
            if (stamp == gamesStamp) return;
            gamesStamp = stamp;
            games = File.ReadAllLines(gamesFile)
                .Select(l => l.Trim().ToLowerInvariant())
                .Where(l => l.Length > 0 && !l.StartsWith('#'))
                .ToHashSet();
        }
        catch { }
    }

    // ===================== menu =====================
    void BuildMenu()
    {
        var auto = new MenuItem { Header = S.StartWithWindows, IsCheckable = true };
        auto.Click += (_, _) => Win32.SetAutostart(auto.IsChecked);
        var gameMode = new MenuItem { Header = S.GameMode, IsCheckable = true, ToolTip = S.GameModeTip };
        gameMode.Click += (_, _) => SetGameModeEnabled(gameMode.IsChecked);
        var gamesItem = new MenuItem { Header = S.EditGames };
        gamesItem.Click += (_, _) => { LoadGames(); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(gamesFile) { UseShellExecute = true }); };
        // Explorer opens with TaskbarIsland.exe selected: handy when updating (replace this file)
        var folder = new MenuItem { Header = S.OpenFolder };
        folder.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{Environment.ProcessPath}\""); } catch { }
        };
        var quit = new MenuItem { Header = S.Quit };
        quit.Click += (_, _) => { cts.Cancel(); Application.Current.Shutdown(); };
        var menu = new ContextMenu();
        menu.Items.Add(auto); menu.Items.Add(gameMode); menu.Items.Add(gamesItem); menu.Items.Add(folder); menu.Items.Add(new Separator()); menu.Items.Add(quit);
        menu.Opened += (_, _) => { auto.IsChecked = Win32.AutostartEnabled(); gameMode.IsChecked = settings.GameMode; };
        Root.ContextMenu = menu;
    }
}
