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

    // ---------- autostart (HKCU Run + the "approved" flag Task Manager uses) ----------
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", RunName = "TaskbarIsland";
    const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    // first byte 02 = enabled, 03 = disabled by the user in Task Manager; the rest is a timestamp (zero is fine)
    static readonly byte[] Approved = [0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    /// <summary>null = no flag yet, true = enabled, false = disabled in Task Manager.</summary>
    static bool? ApprovedState()
    {
        using var k = Registry.CurrentUser.OpenSubKey(ApprovedKey);
        return k?.GetValue(RunName) is byte[] { Length: > 0 } b ? (b[0] & 1) == 0 : null;
    }

    static void SetApproved(bool on)
    {
        using var k = Registry.CurrentUser.CreateSubKey(ApprovedKey, writable: true);
        if (on) k.SetValue(RunName, Approved, RegistryValueKind.Binary);
        else k.DeleteValue(RunName, throwOnMissingValue: false);
    }

    /// <summary>Registered AND not disabled in Task Manager's Startup apps.</summary>
    public static bool AutostartEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(RunName) is string && ApprovedState() != false;
    }

    /// <summary>
    /// Self-heal: some Windows 11 builds skipped our Run entry at logon while it had no StartupApproved flag
    /// (apps that have the flag started fine). If autostart is on but the flag is missing, write "enabled".
    /// Never overrides a user's "disabled" choice.
    /// </summary>
    public static void EnsureAutostartApproved()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            if (k?.GetValue(RunName) is string && ApprovedState() == null) SetApproved(true);
        }
        catch { }
    }

    /// <summary>Exe path registered to start with Windows, if it still exists.</summary>
    public static string? AutostartPath()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        if (k?.GetValue(RunName) is not string cmd) return null;
        var path = cmd.Trim().Trim('"');
        return File.Exists(path) ? path : null;
    }

    public static void SetAutostart(bool on, string? exePath = null)
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (k == null) return;
        if (on) k.SetValue(RunName, $"\"{exePath ?? Environment.ProcessPath}\"");
        else k.DeleteValue(RunName, throwOnMissingValue: false);
        try { SetApproved(on); } catch { }
    }
}
