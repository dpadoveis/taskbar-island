using HidSharp;

namespace TaskbarIsland.Core;

public enum DeviceKind { Keyboard, Mouse, Headset, Other }

/// <summary>A detected device. Battery == null means "known but asleep / not answering right now".</summary>
public sealed record DeviceState(string Id, DeviceKind Kind, string Name, BatteryReading? Battery);

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
