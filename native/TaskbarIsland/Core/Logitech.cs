using System.Text;
using HidSharp;

namespace TaskbarIsland.Core;

/// <summary>
/// Logitech without G HUB. Only runs while G HUB isn't connected (otherwise G HUB already reports everything):
///  - headsets on "Centurion" dongles (G522 and similar): bridge → headset battery feature 0x0104;
///  - HID++ 2.0 receivers (Unifying / Lightspeed / Bolt) and cabled devices: battery feature 0x1004 or 0x1000.
/// Only read-only queries (feature lookup, battery, name/type) are ever sent.
/// </summary>
public sealed class LogitechProvider(IDeviceSink sink)
{
    readonly HashSet<string> seen = new();
    readonly AutoResetEvent rescan = new(false);
    readonly Dictionary<string, (byte Bridge, byte Battery)> centurion = new(); // per dongle path
    readonly Dictionary<string, HidppInfo> hidpp = new();                       // per path + device index
    readonly Dictionary<string, DeviceState> last = new();

    sealed record HidppInfo(byte BatteryIdx, bool Unified, DeviceKind Kind, string Name);

    public void Start(CancellationToken ct)
    {
        DeviceList.Local.Changed += (_, _) => rescan.Set();
        GHubProvider.ConnectionChanged += () => rescan.Set();
        new Thread(() =>
        {
            Thread.Sleep(3000); // give G HUB's websocket a head start, so devices don't show twice at startup
            while (!ct.IsCancellationRequested)
            {
                try { Scan(); } catch { }
                WaitHandle.WaitAny(new[] { ct.WaitHandle, rescan }, TimeSpan.FromSeconds(60));
                Thread.Sleep(500);
            }
        }) { IsBackground = true, Name = "logitech" }.Start();
    }

    void Scan()
    {
        var present = new HashSet<string>();
        if (!GHubProvider.Connected && Environment.GetEnvironmentVariable("TI_NO_LOGI_DIRECT") == null)
        {
            foreach (var dev in Hid.FindAll(Protocols.LogiVid, null, Protocols.CenturionPage))
            {
                if (SafeOut(dev) < 64) continue;
                string id = $"logi-c:{dev.DevicePath.GetHashCode():x8}";
                bool known = false;
                BatteryReading? r = null;
                try { r = QueryCenturion(dev, out known); } catch { }
                if (!known && !seen.Contains(id)) continue;
                present.Add(id); seen.Add(id);
                sink.Report(new DeviceState(id, DeviceKind.Headset, CleanName(Hid.ProductName(dev, "Logitech")), r));
            }
            foreach (var dev in Hid.FindAll(Protocols.LogiVid, null, Protocols.HidppPage, Protocols.HidppLongUsage))
            {
                if (SafeOut(dev) != Protocols.HidppLongLen) continue;
                try { ScanHidpp(dev, present); } catch { }
            }
        }
        foreach (var gone in seen.Where(id => !present.Contains(id)).ToList()) { seen.Remove(gone); sink.Remove(gone); }
    }

    static int SafeOut(HidDevice d) { try { return d.GetMaxOutputReportLength(); } catch { return 0; } }

    static string CleanName(string n)
    {
        int dash = n.IndexOf(" - ", StringComparison.Ordinal); // "G522 LIGHTSPEED - Wireless Mode"
        return dash > 0 ? n[..dash] : n;
    }

    static void ReadUntil(HidStream s, byte[] buf, int ms, Func<byte[], bool> done)
    {
        var deadline = Environment.TickCount64 + ms;
        while (Environment.TickCount64 < deadline)
        {
            try
            {
                int n = s.Read(buf, 0, buf.Length);
                if (n > 0 && done(buf[..n])) return;
            }
            catch (TimeoutException) { }
        }
    }

    // ---------- Centurion headsets ----------
    BatteryReading? QueryCenturion(HidDevice dev, out bool known)
    {
        known = false;
        if (!dev.TryOpen(out HidStream s)) return null;
        using (s)
        {
            s.ReadTimeout = 200;
            var buf = new byte[Math.Max(64, dev.GetMaxInputReportLength())];
            if (!centurion.TryGetValue(dev.DevicePath, out var idx))
            {
                byte? bridge = null;
                s.Write(Protocols.CenturionGetFeature(Protocols.CenturionBridge));
                ReadUntil(s, buf, 800, f => (bridge = Protocols.CenturionFeatureIndex(f)) != null);
                if (bridge is not byte b || b == 0) return null;
                known = true;
                byte[]? data = null;
                s.Write(Protocols.CenturionBridged(b, 0x00, [0x01, Protocols.CenturionBattery]));
                ReadUntil(s, buf, 1500, f => (data = Protocols.CenturionBridgedData(f, b, 0x00)) != null || Protocols.CenturionHeadsetGone(f, b));
                if (data is not { Length: > 0 } || data[0] == 0) return null; // headset off: look it up next time
                centurion[dev.DevicePath] = idx = (b, data[0]);
            }
            known = true;
            byte[]? batt = null;
            bool gone = false;
            s.Write(Protocols.CenturionBridged(idx.Bridge, idx.Battery, []));
            ReadUntil(s, buf, 1500, f => (batt = Protocols.CenturionBridgedData(f, idx.Bridge, idx.Battery)) != null
                                         || (gone = Protocols.CenturionHeadsetGone(f, idx.Bridge)));
            return gone || batt == null ? null : Protocols.ParseCenturionBattery(batt);
        }
    }

    // ---------- HID++ 2.0 ----------
    void ScanHidpp(HidDevice dev, HashSet<string> present)
    {
        if (!dev.TryOpen(out HidStream s)) return;
        using (s)
        {
            s.ReadTimeout = 150;
            // Receivers carry up to 6 paired devices (index 1..6); a cabled device answers as index 0xFF.
            bool receiver = (dev.ProductID & 0xFF00) == 0xC500;
            byte[] indices = receiver ? [1, 2, 3, 4, 5, 6] : [0xFF];
            foreach (byte i in indices)
            {
                string key = $"{dev.DevicePath}|{i}";
                string id = $"logi-h:{dev.DevicePath.GetHashCode():x8}:{i}";
                if (!hidpp.TryGetValue(key, out var info))
                {
                    var found = DiscoverHidpp(s, i);
                    if (found == null)
                    {
                        // nothing answering at this slot now (asleep or empty): keep a known device as "asleep"
                        if (last.TryGetValue(id, out var prev)) { present.Add(id); sink.Report(prev with { Battery = null }); }
                        continue;
                    }
                    hidpp[key] = info = found;
                }
                var data = Ask(s, i, info.BatteryIdx, (byte)(info.Unified ? 1 : 0));
                var r = data is { Length: > 0 } ? (info.Unified ? Protocols.ParseUnifiedBattery(data) : Protocols.ParseBatteryStatus(data)) : null;
                present.Add(id); seen.Add(id);
                sink.Report(last[id] = new DeviceState(id, info.Kind, info.Name, r));
            }
        }
    }

    static HidppInfo? DiscoverHidpp(HidStream s, byte i)
    {
        byte idx = FeatureIndex(s, i, Protocols.HidppUnifiedBattery);
        bool unified = idx != 0;
        if (!unified) idx = FeatureIndex(s, i, Protocols.HidppBatteryStatus);
        if (idx == 0) return null;
        var kind = DeviceKind.Other;
        string name = "Logitech";
        byte nt = FeatureIndex(s, i, Protocols.HidppNameType);
        if (nt != 0)
        {
            if (Ask(s, i, nt, 2) is { Length: > 0 } t) kind = Protocols.HidppKind(t[0]);
            if (Ask(s, i, nt, 0) is { Length: > 0 } c && c[0] is > 0 and < 64)
            {
                var sb = new StringBuilder();
                while (sb.Length < c[0] && Ask(s, i, nt, 1, (byte)sb.Length) is { Length: > 0 } part)
                {
                    int take = Math.Min(part.Length, c[0] - sb.Length);
                    if (take <= 0) break;
                    sb.Append(Encoding.UTF8.GetString(part, 0, take).TrimEnd('\0'));
                }
                if (sb.Length > 0) name = sb.ToString().Trim();
            }
        }
        if (!name.StartsWith("Logitech", StringComparison.OrdinalIgnoreCase)) name = "Logitech " + name;
        return new HidppInfo(idx, unified, kind, name);
    }

    static byte FeatureIndex(HidStream s, byte dev, ushort feature) =>
        Ask(s, dev, 0x00, 0, (byte)(feature >> 8), (byte)feature) is { Length: > 0 } r ? r[0] : (byte)0;

    static byte[]? Ask(HidStream s, byte dev, byte featIdx, byte fn, params byte[] pars)
    {
        s.Write(Protocols.HidppRequest(dev, featIdx, fn, pars));
        var buf = new byte[64];
        var deadline = Environment.TickCount64 + 400;
        while (Environment.TickCount64 < deadline)
        {
            try
            {
                int n = s.Read(buf, 0, buf.Length);
                var r = Protocols.HidppReply(buf.AsSpan(0, n), dev, featIdx, fn);
                if (r != null) return r;
            }
            catch (TimeoutException) { }
        }
        return null;
    }
}
