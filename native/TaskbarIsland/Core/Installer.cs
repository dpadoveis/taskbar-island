using System.Diagnostics;
using System.IO;
using System.Windows;

namespace TaskbarIsland.Core;

/// <summary>
/// The exe installs and updates itself, so nobody has to move files around:
/// - an installation exists elsewhere (found through "start with Windows" or a running copy):
///   close it, copy this exe over it, start it -> update done;
/// - nothing installed yet: copy to %LOCALAPPDATA%\Programs\TaskbarIsland, enable autostart, start it.
/// Never downgrades: if the installed copy is newer, that one is simply started.
/// </summary>
public static class Installer
{
    public static readonly string DefaultExe = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "TaskbarIsland", "TaskbarIsland.exe");

    static bool Quiet => Environment.GetEnvironmentVariable("TI_QUIET") != null;

    /// <summary>Returns true when this process handled an install/update and must exit.</summary>
    public static bool Run()
    {
        if (Environment.GetEnvironmentVariable("TI_NO_INSTALLER") != null) return false;
        var self = Environment.ProcessPath;
        if (self == null) return false;

        var installed = Win32.AutostartPath() ?? RunningCopyPath();
        if (installed == null)
        {
            if (Same(self, DefaultExe)) return false; // already running from the install folder
            return Install(self, DefaultExe, fresh: true);
        }
        if (Same(self, installed)) return false;     // normal start of the installed copy

        var mine = VersionOf(self);
        var theirs = VersionOf(installed);
        if (theirs > mine)
        {
            Start(installed);
            Say(S.NewerInstalled(Short(theirs)), MessageBoxImage.Information);
            return true;
        }
        return Install(self, installed, fresh: false);
    }

    static bool Install(string self, string target, bool fresh)
    {
        try
        {
            CloseRunningCopies();
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            for (int attempt = 0; ; attempt++)
            {
                try { File.Copy(self, target, overwrite: true); break; }
                catch (IOException) when (attempt < 20) { Thread.Sleep(250); } // old exe still releasing its file
            }
            if (fresh) Win32.SetAutostart(true, target);
            Start(target);
            Say(fresh ? S.Installed : S.Updated(Short(VersionOf(self))), MessageBoxImage.Information);
        }
        catch (Exception e)
        {
            Say(S.UpdateFailed(e.Message), MessageBoxImage.Warning);
        }
        return true;
    }

    static void CloseRunningCopies()
    {
        int me = Environment.ProcessId;
        foreach (var p in Process.GetProcessesByName("TaskbarIsland"))
        {
            if (p.Id == me) continue;
            try { p.Kill(); p.WaitForExit(5000); } catch { }
        }
    }

    static string? RunningCopyPath()
    {
        int me = Environment.ProcessId;
        foreach (var p in Process.GetProcessesByName("TaskbarIsland"))
        {
            if (p.Id == me) continue;
            try { if (p.MainModule?.FileName is string f && File.Exists(f)) return f; } catch { }
        }
        return null;
    }

    static void Start(string exe)
    {
        try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) }); } catch { }
    }

    static bool Same(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    static Version VersionOf(string exe)
    {
        try { return Version.TryParse(FileVersionInfo.GetVersionInfo(exe).FileVersion, out var v) ? v : new Version(0, 0); }
        catch { return new Version(0, 0); }
    }

    static string Short(Version v) => $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";

    static void Say(string text, MessageBoxImage icon)
    {
        if (!Quiet) MessageBox.Show(text, "Taskbar Island", MessageBoxButton.OK, icon);
    }
}
