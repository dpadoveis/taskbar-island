using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
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
    const double TextW = 180, TextWHover = 131, SlotW = 26, SlotWHover = 75, EqLo = 5.0 / 18;

    static readonly Geometry PlayGeo = Geometry.Parse("M5,2.9 L5,13.1 C5,13.9 5.9,14.4 6.6,13.9 L13.8,8.8 C14.4,8.4 14.4,7.6 13.8,7.2 L6.6,2.1 C5.9,1.6 5,2.1 5,2.9 Z");
    static readonly Geometry PauseGeo = Geometry.Parse("M3.6,2.6 L6.7,2.6 L6.7,13.4 L3.6,13.4 Z M9.3,2.6 L12.4,2.6 L12.4,13.4 L9.3,13.4 Z");
    static readonly Geometry BoltGeo = Geometry.Parse("M10.7,0.3 L8.2,4.6 L9.9,4.6 L9.2,9.2 L12.1,4.1 L10.4,4.1 Z");

    static readonly Dictionary<DeviceKind, string> Glyphs = new()
    {
        [DeviceKind.Keyboard] = "", [DeviceKind.Mouse] = "",
        [DeviceKind.Headset] = "", [DeviceKind.Other] = "",
    };

    readonly CancellationTokenSource cts = new();
    readonly SpotifySource spotify;
    readonly SolidColorBrush accent = new(Color.FromRgb(0xE8, 0xE8, 0xE8));
    readonly SolidColorBrush fg = new(Colors.White);
    readonly SolidColorBrush lowBrush = new(Color.FromRgb(0xFF, 0x99, 0xA4));
    readonly SolidColorBrush boltCut = new(Color.FromRgb(0x1F, 0x1F, 0x1F));
    readonly Dictionary<string, DeviceView> devices = new();
    readonly DispatcherTimer tick;

    IntPtr hwnd;
    MediaInfo? media;
    bool light, inGame, marqueeOverflow, hovering;
    int gameStreak, ticks;
    HashSet<string> games = new();
    DateTime gamesStamp;
    public static readonly string DataDir = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskbarIsland");
    readonly string gamesFile = System.IO.Path.Combine(DataDir, "games.txt");

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
        PlayPause.Data = PauseGeo;
        PrevButton.ToolTip = S.Prev; ToggleButton.ToolTip = S.PlayPause; NextButton.ToolTip = S.Next;

        // hover is polled from the real cursor position: enter/leave events are unreliable on a
        // window that never activates (a highlight could get stuck)
        new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Input, (_, _) => PollHover(), Dispatcher).Start();
        Island.MouseWheel += (_, e) => spotify!.Command(e.Delta < 0 ? "next" : "prev");
        Art.MouseLeftButtonUp += (_, _) => spotify!.Command("toggle");
        SizeChanged += (_, _) => Place();
        BuildMenu();
        UpdateEmpty();

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
        tick = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => OnTick(), Dispatcher);
        tick.Start();
        // once everything is loaded and drawn, give idle startup memory back to Windows
        DelayedTrim(8);
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

    // ===================== data =====================
    void OnDevice(DeviceState s)
    {
        if (!devices.TryGetValue(s.Id, out var v))
        {
            v = new DeviceView(s.Kind, s.Name, Glyphs[s.Kind], fg, lowBrush, boltCut);
            devices[s.Id] = v;
            // keep a stable order: keyboard, mouse, headset, others; then by name
            int at = 0;
            foreach (Border b in Batteries.Children)
            {
                var o = (DeviceView)b.Tag;
                if (o.Kind > s.Kind || (o.Kind == s.Kind && string.CompareOrdinal(o.Name, s.Name) > 0)) break;
                at++;
            }
            Batteries.Children.Insert(at, v.Root);
            UpdateEmpty();
        }
        v.Set(s.Battery);
    }

    void OnDeviceRemoved(string id)
    {
        if (!devices.Remove(id, out var v)) return;
        Batteries.Children.Remove(v.Root);
        UpdateEmpty();
    }

    /// <summary>Nothing to show (no Spotify, no device found) -> the widget takes no space at all.</summary>
    void UpdateEmpty() =>
        Root.Visibility = Island.Visibility == Visibility.Visible || devices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    void OnMedia(MediaInfo? m)
    {
        var prev = media;
        media = m;
        if (m == null)
        {
            Island.Visibility = Visibility.Collapsed;
            StopAnimations();
            UpdateEmpty();
            return;
        }
        Island.Visibility = Visibility.Visible;
        UpdateEmpty();
        bool trackChanged = prev == null || prev.Title != m.Title || prev.Artist != m.Artist;
        if (trackChanged) SetText(m);
        if (prev == null || prev.ThumbId != m.ThumbId) SetCover(m.Thumbnail);
        if (trackChanged || prev!.Playing != m.Playing) ApplyPlayState();
        UpdateProgress();
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
        marqueeOverflow = T1.DesiredSize.Width > TextW - 12;
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
        if (bytes == null) { ArtCircle.Fill = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)); accent.Color = Color.FromRgb(0xE8, 0xE8, 0xE8); return; }
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.DecodePixelWidth = 64; // drawn at 24px: never keep the full-size cover in memory
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            var brush = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
            brush.Freeze();
            ArtCircle.Fill = brush;
            accent.Color = Accent.FromCover(bmp);
        }
        catch { }
    }

    void UpdateProgress()
    {
        var m = media;
        double w = ProgressTrack.ActualWidth;
        ProgressFill.Width = m != null && m.Duration > 0 && w > 0 ? w * Math.Clamp(m.Position / m.Duration, 0, 1) : 0;
    }

    // ===================== animations =====================
    void ApplyPlayState()
    {
        StopAnimations();
        if (media == null || inGame) return;
        PlayPause.Data = media.Playing ? PauseGeo : PlayGeo;
        Eq.BeginAnimation(OpacityProperty, null); // release any value held by the hover fade
        Eq.Opacity = hovering ? 0 : (media.Playing ? 1 : 0.45);
        ArtCircle.Opacity = media.Playing ? 1 : 0.7;
        if (!media.Playing) return;

        // same rhythm as the original (Electron) version: 5px -> 18px, full cycles of 1.0 / 0.8 / 1.15 / 0.9 s,
        // phase-shifted; tuple = (bar, half-cycle s, low scale, high scale, start offset s)
        var bars = new[] { (Eq1, 0.50, EqLo, 1.0, 0.0), (Eq2, 0.40, EqLo, 1.0, 0.45), (Eq3, 0.575, EqLo, 1.0, 0.55), (Eq4, 0.45, EqLo, 1.0, 0.75) };
        foreach (var (r, secs, lo, hi, delay) in bars)
            ((ScaleTransform)r.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(lo, hi, TimeSpan.FromSeconds(secs))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(delay),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            });
        if (marqueeOverflow)
        {
            T1.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
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

    void SetHover(bool on)
    {
        hovering = on;
        var d = new Duration(TimeSpan.FromMilliseconds(220));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Slot.BeginAnimation(WidthProperty, new DoubleAnimation(on ? SlotWHover : SlotW, d) { EasingFunction = ease });
        TextHost.BeginAnimation(WidthProperty, new DoubleAnimation(on ? TextWHover : TextW, d) { EasingFunction = ease });
        Controls.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0, d));
        Eq.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 0 : (media?.Playing == true ? 1 : 0.45), d));
        Controls.IsHitTestVisible = on;
    }

    void PollHover()
    {
        if (!IsVisible || hwnd == IntPtr.Zero || !Win32.GetCursorPos(out var c)) return;
        Point p;
        try { p = PointFromScreen(new Point(c.X, c.Y)); } catch { return; }
        bool inWindow = p.X >= 0 && p.Y >= 0 && p.X < ActualWidth && p.Y < ActualHeight;
        bool onIsland = inWindow && Island.IsVisible && Contains(Island, p);
        if (onIsland != hovering) SetHover(onIsland);
        foreach (var v in devices.Values) v.Highlight(inWindow && Contains(v.Root, p));
    }

    bool Contains(FrameworkElement e, Point p) =>
        e.TransformToAncestor(this).TransformBounds(new Rect(e.RenderSize)).Contains(p);

    void OnControl(object sender, RoutedEventArgs e) => spotify.Command((string)((Button)sender).Tag);

    // ===================== devices =====================
    sealed class DeviceView
    {
        public readonly Border Root;
        public readonly DeviceKind Kind;
        public readonly string Name;
        readonly TextBlock icon;
        readonly Rectangle body, cap, fill;
        readonly Path bolt, boltOutline;
        readonly SolidColorBrush fg, low;

        public DeviceView(DeviceKind kind, string name, string glyph, SolidColorBrush fg, SolidColorBrush low, SolidColorBrush cut)
        {
            Kind = kind; Name = name; this.fg = fg; this.low = low;
            icon = new TextBlock
            {
                Text = glyph, FontFamily = (FontFamily)Application.Current.Resources["IconFont"], FontSize = 16,
                Width = 16, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = fg,
            };
            // Windows 11 tray battery redrawn at its real pixel size: 18x10 body, 1.3px cap, bolt through the top edge
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
            Root = new Border { Height = 44, Padding = new Thickness(7, 0, 7, 0), CornerRadius = new CornerRadius(5), Child = row, Background = Brushes.Transparent, Opacity = 0.35, Tag = this };
            Root.ToolTip = name;
        }

        static readonly SolidColorBrush HoverBrush = new(Color.FromArgb(0x0F, 255, 255, 255));
        bool lit;
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
            var showBolt = online && r!.Charging ? Visibility.Visible : Visibility.Collapsed;
            bolt.Visibility = boltOutline.Visibility = showBolt;
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
        bool gameNow = Win32.FullscreenAppRunning() || (games.Count > 0 && games.Contains(Win32.ForegroundExe()));
        gameStreak = gameNow != inGame ? gameStreak + 1 : 0;
        if (gameStreak >= 2) { gameStreak = 0; if (gameNow) EnterGameMode(); else ExitGameMode(); }
        if (inGame) return;

        if (hwnd != IntPtr.Zero)
        {
            if (Win32.OwnerOf(hwnd) != Win32.TrayHwnd()) Win32.OwnByTaskbar(hwnd); // explorer restarted
            Win32.ForceTopmost(hwnd);
        }
        Place();
        if (media is { Playing: true, Duration: > 0 })
        {
            media = media with { Position = Math.Min(media.Duration, media.Position + 1) };
            UpdateProgress();
        }
    }

    void EnterGameMode()
    {
        inGame = true;
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
        var games = new MenuItem { Header = S.EditGames };
        games.Click += (_, _) => { LoadGames(); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(gamesFile) { UseShellExecute = true }); };
        var quit = new MenuItem { Header = S.Quit };
        quit.Click += (_, _) => { cts.Cancel(); Application.Current.Shutdown(); };
        var menu = new ContextMenu();
        menu.Items.Add(auto); menu.Items.Add(games); menu.Items.Add(new Separator()); menu.Items.Add(quit);
        menu.Opened += (_, _) => auto.IsChecked = Win32.AutostartEnabled();
        Root.ContextMenu = menu;
    }
}
