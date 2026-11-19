using System.Windows;
using System.Windows.Media.Animation;

namespace TaskbarIsland;

public partial class App : Application
{
    static Mutex? single;

    protected override void OnStartup(StartupEventArgs e)
    {
        // a downloaded exe installs / updates itself, then hands over to the installed copy
        if (Core.Installer.Run()) { Shutdown(); return; }

        single = new Mutex(true, @"Local\TaskbarIsland", out bool first);
        if (!first) { Shutdown(); return; }

        // safety net: a UI glitch is logged and skipped instead of closing the widget
        DispatcherUnhandledException += (_, ex) => { Log(ex.Exception); ex.Handled = true; };
        TaskScheduler.UnobservedTaskException += (_, ex) => { Log(ex.Exception); ex.SetObserved(); };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Log(ex.ExceptionObject as Exception);

        // Measured: a transparent (layered) window repaints entirely every frame. Software rendering skips the
        // GPU->CPU copy of each frame. Measured while playing: 24 fps ~15%, 20 fps ~10%, 15 fps ~6% of one core;
        // 20 is the chosen balance. TI_FPS / TI_HW env vars override for experiments.
        int fps = int.TryParse(Environment.GetEnvironmentVariable("TI_FPS"), out var f) ? f : 20;
        Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(Timeline), new FrameworkPropertyMetadata(fps));
        // (software rendering is now set per window: the transparent island uses it, the native card uses the GPU)
        base.OnStartup(e);
        new MainWindow().Show();
    }

    /// <summary>Appends to %APPDATA%\TaskbarIsland\error.log (kept under ~200 KB).</summary>
    static void Log(Exception? e)
    {
        if (e == null) return;
        try
        {
            var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskbarIsland");
            System.IO.Directory.CreateDirectory(dir);
            var file = System.IO.Path.Combine(dir, "error.log");
            if (System.IO.File.Exists(file) && new System.IO.FileInfo(file).Length > 200_000) System.IO.File.Delete(file);
            System.IO.File.AppendAllText(file, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] v{typeof(App).Assembly.GetName().Version}\n{e}\n\n");
        }
        catch { }
    }
}
