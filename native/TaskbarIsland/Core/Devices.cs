using System.Runtime.InteropServices;
using HidSharp;
using Microsoft.Win32.SafeHandles;

namespace TaskbarIsland.Core;

public enum DeviceKind { Keyboard, Mouse, Headset, Other }

/// <summary>A detected device. Battery == null means "known but asleep / not answering right now".</summary>
/// <param name="Hint">optional advice shown in the bubble while there is no reading (e.g. "update the firmware").</param>
public sealed record DeviceState(string Id, DeviceKind Kind, string Name, BatteryReading? Battery, string? Hint = null);

/// <summary>Where providers report what they find. Called from background threads.</summary>
public interface IDeviceSink
{
    void Report(DeviceState state);
    void Remove(string id);
}

static class Hid
{
    /// <summary>All HID collections of a vendor (optionally a product) with the given usage page/usage.</summary>
    public static List<HidDevice> FindAll(int vid, int? pid, int page, int? usage = null)
    {
        var found = new List<HidDevice>();
        foreach (var d in DeviceList.Local.GetHidDevices(vid, pid))
        {
            try
            {
                foreach (var item in d.GetReportDescriptor().DeviceItems)
                    foreach (uint u in item.Usages.GetAllValues())
                        if ((u >> 16) == page && (usage == null || (u & 0xFFFF) == usage)) { found.Add(d); goto next; }
            }
            catch { /* collection without a readable descriptor */ }
            next:;
        }
        return found;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr sec, uint disposition, uint flags, IntPtr template);
    [DllImport("hid.dll", SetLastError = true)]
    static extern bool HidD_SetOutputReport(SafeFileHandle h, byte[] report, int length);

    /// <summary>Sends an output report over the control pipe (SET_REPORT), for devices that ignore
    /// interrupt-OUT writes. False when the device refused it.</summary>
    public static bool SetOutputReport(HidDevice d, byte[] report)
    {
        using var h = CreateFileW(d.DevicePath, 0xC0000000 /* read|write */, 3 /* share read|write */, IntPtr.Zero, 3 /* open existing */, 0, IntPtr.Zero);
        return !h.IsInvalid && HidD_SetOutputReport(h, report, report.Length);
    }

    public static string ProductName(HidDevice d, string fallback)
    {
        try
        {
            var n = d.GetProductName()?.Trim();
            return string.IsNullOrEmpty(n) || n.Contains("2.4G", StringComparison.OrdinalIgnoreCase) && n.Length < 12 ? fallback : n;
        }
        catch { return fallback; }
    }
}
