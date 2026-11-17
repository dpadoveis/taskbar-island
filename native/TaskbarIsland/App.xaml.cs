using System.Windows;
using System.Windows.Media.Animation;

namespace TaskbarIsland;

public partial class App : Application
{
    static Mutex? single;

    protected override void OnStartup(StartupEventArgs e)
    {
        single = new Mutex(true, @"Local\TaskbarIsland", out bool first);
        if (!first) { Shutdown(); return; }
        // Measured: a transparent (layered) window repaints entirely every frame. Software rendering skips the
        // GPU->CPU copy of each frame. Measured while playing: 24 fps ~15%, 20 fps ~10%, 15 fps ~6% of one core;
        // 20 is the chosen balance. TI_FPS / TI_HW env vars override for experiments.
        int fps = int.TryParse(Environment.GetEnvironmentVariable("TI_FPS"), out var f) ? f : 20;
        Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(Timeline), new FrameworkPropertyMetadata(fps));
        if (Environment.GetEnvironmentVariable("TI_HW") == null)
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        base.OnStartup(e);
        new MainWindow().Show();
    }
}
