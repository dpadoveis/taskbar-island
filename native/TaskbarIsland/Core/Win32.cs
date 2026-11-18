using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace TaskbarIsland.Core;

/// <summary>Small Win32 helpers.</summary>
public static partial class Win32
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct APPBARDATA { public int cbSize; public IntPtr hWnd; public uint uCallbackMessage, uEdge; public RECT rc; public IntPtr lParam; }

    [DllImport("shell32.dll")] static extern IntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);
    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowW(string? cls, string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowExW(IntPtr parent, IntPtr after, string? cls, string? name);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageNameW(IntPtr h, int flags, StringBuilder buf, ref int size);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("psapi.dll")] static extern bool EmptyWorkingSet(IntPtr h);
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);

    const int GWL_EXSTYLE = -20, GWLP_HWNDPARENT = -8;
    const long WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080, WS_EX_APPWINDOW = 0x00040000;
    static readonly IntPtr HWND_TOPMOST = new(-1);
    const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;

    /// <summary>Physical-pixel rect of the primary taskbar.</summary>
    public static RECT? TaskbarRect()
    {
        var abd = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() };
        return SHAppBarMessage(5 /*ABM_GETTASKBARPOS*/, ref abd) == IntPtr.Zero ? null : abd.rc;
    }

    public static IntPtr TrayHwnd() => FindWindowW("Shell_TrayWnd", null);

    /// <summary>Physical x where the notification area (clock/tray) starts.</summary>
    public static int? TrayLeft()
    {
        var tray = TrayHwnd();
        if (tray == IntPtr.Zero) return null;
        var notify = FindWindowExW(tray, IntPtr.Zero, "TrayNotifyWnd", null);
        if (notify == IntPtr.Zero || !GetWindowRect(notify, out var r) || r.Right <= r.Left) return null;
        return r.Left;
    }

    public static void MakeToolNoActivate(IntPtr hwnd)
    {
        long ex = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        ex = (ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)ex);
    }

    /// <summary>Make the taskbar the owner of our window: owned windows always sit above their
    /// owner, so we rise with the taskbar whenever the shell raises it (Start menu, Win key).</summary>
    public static bool OwnByTaskbar(IntPtr hwnd)
    {
        var tray = TrayHwnd();
        if (tray == IntPtr.Zero || !IsWindow(hwnd)) return false;
        SetWindowLongPtr(hwnd, GWLP_HWNDPARENT, tray);
        return GetWindowLongPtr(hwnd, GWLP_HWNDPARENT) == tray;
    }

    public static IntPtr OwnerOf(IntPtr hwnd) => GetWindowLongPtr(hwnd, GWLP_HWNDPARENT);

    public static void ForceTopmost(IntPtr hwnd) =>
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    /// <summary>Fullscreen app/game, D3D exclusive fullscreen or presentation mode.</summary>
    public static bool FullscreenAppRunning() =>
        SHQueryUserNotificationState(out int s) == 0 && s is 2 or 3 or 4;

    /// <summary>Lower-case exe name of the foreground window's process ("" if unknown).</summary>
    public static string ForegroundExe()
    {
        var h = GetForegroundWindow();
        if (h == IntPtr.Zero) return "";
        GetWindowThreadProcessId(h, out uint pid);
        var p = OpenProcess(0x1000 /*QUERY_LIMITED_INFORMATION*/, false, pid);
        if (p == IntPtr.Zero) return "";
        try
        {
            var sb = new StringBuilder(520); int size = sb.Capacity;
            return QueryFullProcessImageNameW(p, 0, sb, ref size) ? Path.GetFileName(sb.ToString()).ToLowerInvariant() : "";
        }
        finally { CloseHandle(p); }
    }

    public static bool TaskbarIsLight()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return k?.GetValue("SystemUsesLightTheme") is int v && v == 1;
    }

    /// <summary>Hand our idle pages back to Windows (shows up as a much smaller "Memory" in Task Manager).</summary>
    public static void TrimWorkingSet() => EmptyWorkingSet(GetCurrentProcess());

    // ---------- autostart: shortcut in the user's Startup folder ----------
    // (The HKCU\...\Run entry used before was silently skipped by Explorer at logon on some Windows 11 PCs,
    //  even with an "enabled" StartupApproved flag; the Startup folder is a separate, reliable path.)
    const string LinkName = "Taskbar Island.lnk";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", RunName = "TaskbarIsland"; // legacy
    const string ApprovedRoot = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    static string LinkPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), LinkName);

    /// <summary>Task Manager's on/off flag for a startup item: null = none, true = enabled, false = disabled.</summary>
    static bool? ApprovedState(string list, string name)
    {
        using var k = Registry.CurrentUser.OpenSubKey(ApprovedRoot + list);
        return k?.GetValue(name) is byte[] { Length: > 0 } b ? (b[0] & 1) == 0 : null;
    }

    static void ClearApproved(string list, string name)
    {
        using var k = Registry.CurrentUser.OpenSubKey(ApprovedRoot + list, writable: true);
        k?.DeleteValue(name, throwOnMissingValue: false);
    }

    static string? LinkTarget()
    {
        if (!File.Exists(LinkPath)) return null;
        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            string target = shell.CreateShortcut(LinkPath).TargetPath;
            return string.IsNullOrEmpty(target) ? null : target;
        }
        catch { return null; }
    }

    static void CreateLink(string exe)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic link = shell.CreateShortcut(LinkPath);
        link.TargetPath = exe;
        link.WorkingDirectory = Path.GetDirectoryName(exe);
        link.Description = "Taskbar Island";
        link.Save();
    }

    static string? LegacyRunPath()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(RunName) is string cmd ? cmd.Trim().Trim('"') : null;
    }

    static void RemoveLegacyRun()
    {
        using (var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)) k?.DeleteValue(RunName, throwOnMissingValue: false);
        ClearApproved("Run", RunName);
    }

    /// <summary>Starts with Windows: shortcut present and not disabled in Task Manager's Startup apps.</summary>
    public static bool AutostartEnabled() =>
        File.Exists(LinkPath) && ApprovedState("StartupFolder", LinkName) != false;

    /// <summary>
    /// Moves an old "Run" registry autostart to the Startup-folder shortcut (keeps a user's "disabled" choice by
    /// not re-enabling). Safe to call on every start.
    /// </summary>
    public static void MigrateAutostart()
    {
        try
        {
            var legacy = LegacyRunPath();
            if (legacy == null) return;
            bool userDisabled = ApprovedState("Run", RunName) == false;
            if (!userDisabled && File.Exists(legacy) && !File.Exists(LinkPath)) CreateLink(legacy);
            RemoveLegacyRun();
        }
        catch { }
    }

    /// <summary>Installed exe registered to start with Windows (shortcut, or a legacy Run entry), if it exists.</summary>
    public static string? AutostartPath()
    {
        var p = LinkTarget() ?? LegacyRunPath();
        return p != null && File.Exists(p) ? p : null;
    }

    public static void SetAutostart(bool on, string? exePath = null)
    {
        try { RemoveLegacyRun(); } catch { }
        if (on)
        {
            CreateLink(exePath ?? Environment.ProcessPath!);
            ClearApproved("StartupFolder", LinkName); // an old "disabled" flag would otherwise keep it off
        }
        else if (File.Exists(LinkPath)) File.Delete(LinkPath);
    }
}