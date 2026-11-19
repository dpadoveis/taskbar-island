using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using HidSharp;

namespace TaskbarIsland.Core;

/// <summary>
/// Compx 2.4 GHz receivers (VID 0x3554), used by AULA keyboards (F75, F87, F99 and others) and other brands.
/// Asks for the battery every 60 s with the documented read-only command 0x4A, opening/closing each time so
/// the vendor app is never blocked. A receiver only shows up after it answered once (receivers of other
/// devices that don't speak this command are simply ignored).
/// </summary>
public sealed class CompxProvider(IDeviceSink sink)
{
    static readonly Dictionary<int, (string Name, DeviceKind Kind)> Known = new()
    {
        [0xFA09] = ("AULA F75", DeviceKind.Keyboard),
    };

    readonly HashSet<string> seen = new();
    readonly AutoResetEvent rescan = new(false);

    public void Start(CancellationToken ct)
    {
        DeviceList.Local.Changed += (_, _) => rescan.Set(); // plugged / unplugged receiver
        new Thread(() => Run(ct)) { IsBackground = true, Name = "compx" }.Start();
    }

    void Run(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { Scan(); } catch { }
            WaitHandle.WaitAny(new[] { ct.WaitHandle, rescan }, TimeSpan.FromSeconds(60));
            Thread.Sleep(500); // let a freshly plugged receiver settle
        }
    }

    void Scan()
    {
        var present = new HashSet<string>();
        foreach (var dev in Hid.FindAll(Protocols.AulaVid, null, Protocols.AulaUsagePage, Protocols.AulaUsage))
        {
            string id = $"compx:{dev.ProductID:x4}:{dev.DevicePath.GetHashCode():x8}";
            present.Add(id);
            var (name, kind) = Known.TryGetValue(dev.ProductID, out var k) ? k : (Hid.ProductName(dev, "AULA"), DeviceKind.Keyboard);
            BatteryReading? r = null;
            try { r = QueryOnce(dev); } catch { }
            if (r != null) seen.Add(id);
            if (seen.Contains(id)) sink.Report(new DeviceState(id, kind, name, r));
        }
        foreach (var gone in seen.Where(id => !present.Contains(id)).ToList()) { seen.Remove(gone); sink.Remove(gone); }
    }

    static BatteryReading? QueryOnce(HidDevice dev)
    {
        if (!dev.TryOpen(out HidStream s)) return null;
        using (s)
        {
            var buf = new byte[Math.Max(64, dev.GetMaxInputReportLength())];
            for (int attempt = 0; attempt < 2; attempt++)
            {
                s.Write(Protocols.AulaRequest());
                var deadline = Environment.TickCount64 + 1200;
                s.ReadTimeout = 250;
                while (Environment.TickCount64 < deadline)
                {
                    try
                    {
                        int n = s.Read(buf, 0, buf.Length);
                        var r = Protocols.ParseAula(buf.AsSpan(0, n));
                        if (r != null) return r;
                    }
                    catch (TimeoutException) { }
                }
                Thread.Sleep(200);
            }
        }
        return null;
    }
}

/// <summary>
/// Shared shape for receivers that are asked for their battery every 60 s (and rescanned when USB devices
/// change). A device only appears after it answered once; afterwards a missing answer shows it as asleep.
/// </summary>
public abstract class PolledProvider(IDeviceSink sink, string name)
{
    readonly HashSet<string> seen = new();
    readonly AutoResetEvent rescan = new(false);

    public void Start(CancellationToken ct)
    {
        DeviceList.Local.Changed += (_, _) => rescan.Set();
        new Thread(() =>
        {
            while (!ct.IsCancellationRequested)
            {
                try { Scan(); } catch { }
                WaitHandle.WaitAny(new[] { ct.WaitHandle, rescan }, TimeSpan.FromSeconds(60));
                Thread.Sleep(500);
            }
        }) { IsBackground = true, Name = name }.Start();
    }

    /// <summary>The device collections to query, with id / kind / display name for each.</summary>
    protected abstract IEnumerable<(HidDevice Dev, string Id, DeviceKind Kind, string Name)> Find();

    /// <summary>Ask one device; null = no battery reading right now. <paramref name="known"/> = the device is
    /// certainly ours (it answered our protocol, or its IDs are unique), so it is shown even while asleep.</summary>
    protected abstract BatteryReading? Query(HidDevice dev, out bool known);

    void Scan()
    {
        var present = new HashSet<string>();
        foreach (var (dev, id, kind, devName) in Find())
        {
            present.Add(id);
            BatteryReading? r = null;
            bool known = false;
            try { r = Query(dev, out known); } catch { }
            if (r != null || known) seen.Add(id);
            if (seen.Contains(id)) sink.Report(new DeviceState(id, kind, devName, r, r == null ? HintFor(id) : null));
        }
        foreach (var gone in seen.Where(id => !present.Contains(id)).ToList()) { seen.Remove(gone); sink.Remove(gone); }
    }

    /// <summary>Advice for a device that has no reading right now.</summary>
    protected virtual string? HintFor(string id) => null;
}

/// <summary>
/// MCHOSE V9 PRO headset (C-Media dongle 291D:385D, shared with S9/G9/V9). Sends ONLY the documented battery
/// request (output report 55 65 01) on the FF90 collection; other report IDs on this dongle can brick it.
/// </summary>
public sealed class MchoseProvider(IDeviceSink sink) : PolledProvider(sink, "mchose")
{
    protected override IEnumerable<(HidDevice, string, DeviceKind, string)> Find() =>
        Hid.FindAll(Protocols.MchoseVid, Protocols.MchosePid, Protocols.MchoseUsagePage)
           .Where(d => d.GetMaxOutputReportLength() >= 3)
           .Select(d => (d, $"mchose:{d.DevicePath.GetHashCode():x8}", DeviceKind.Headset, Hid.ProductName(d, "MCHOSE V9 Pro")));

    protected override BatteryReading? Query(HidDevice dev, out bool known)
    {
        // The dongle's IDs are MCHOSE-only, so it is shown even when it never answers (headset off, or the
        // shipped dongle firmware 0004 that has no battery telemetry until updated in the MCHOSE app).
        known = true;
        if (!dev.TryOpen(out HidStream s)) return null;
        string id = $"mchose:{dev.DevicePath.GetHashCode():x8}";
        using (s)
        {
            s.Write(Protocols.MchoseRequest(dev.GetMaxOutputReportLength()));
            var buf = new byte[Math.Max(64, dev.GetMaxInputReportLength())];
            var deadline = Environment.TickCount64 + 2000;
            s.ReadTimeout = 300;
            while (Environment.TickCount64 < deadline)
            {
                try
                {
                    int n = s.Read(buf, 0, buf.Length);
                    var r = Protocols.ParseMchose(buf.AsSpan(0, n), out bool battery);
                    if (battery) { answered.Add(id); return r; } // media-key / other frames on the same interface are skipped
                }
                catch (TimeoutException) { }
            }
        }
        return null; // headset off: no answer
    }

    // Never answered since the app started: headset off, or a receiver firmware without battery telemetry.
    readonly HashSet<string> answered = new();
    protected override string? HintFor(string id) => answered.Contains(id) ? null : S.MchoseNoAnswer;
}

/// <summary>
/// VXE / ATK / Pulsar mice on Kysona-protocol dongles (VXE Dragonfly R1 / R1 Pro / R1 SE+, Pulsar X2 ...).
/// Collections with 17-byte input+output reports on VID 3554 (PIDs F5xx) or 373B. Only the read-only STATUS and
/// POWER queries are sent, and a collection only shows up after it answered with a valid checksum.
/// </summary>
public sealed class VxeProvider(IDeviceSink sink) : PolledProvider(sink, "vxe")
{
    static readonly Dictionary<int, string> Known = new()
    {
        [0xF58A] = "VXE Dragonfly R1 Pro", [0xF58C] = "VXE Dragonfly R1 Pro",
        [0xF58F] = "VXE R1 SE+", [0x1085] = "VXE R1 SE+",
        [0xF507] = "Pulsar", [0xF508] = "Pulsar", [0xF509] = "Pulsar",
    };

    protected override IEnumerable<(HidDevice, string, DeviceKind, string)> Find() =>
        DeviceList.Local.GetHidDevices(Protocols.VxeVid).Where(d => (d.ProductID & 0xFF00) == 0xF500)
           .Concat(DeviceList.Local.GetHidDevices(Protocols.AtkVid))
           .Where(d => SafeLen(d))
           .Select(d => (d, $"vxe:{d.VendorID:x4}:{d.ProductID:x4}:{d.DevicePath.GetHashCode():x8}", DeviceKind.Mouse, NameOf(d)));

    static bool SafeLen(HidDevice d)
    {
        try { return d.GetMaxOutputReportLength() == Protocols.VxeLen && d.GetMaxInputReportLength() == Protocols.VxeLen; }
        catch { return false; }
    }

    static string NameOf(HidDevice d)
    {
        var fallback = Known.TryGetValue(d.ProductID, out var k) ? k : "VXE";
        var n = Hid.ProductName(d, fallback);
        return n.Contains("receiver", StringComparison.OrdinalIgnoreCase) || n.Contains("dongle", StringComparison.OrdinalIgnoreCase) ? fallback : n;
    }

    protected override BatteryReading? Query(HidDevice dev, out bool known)
    {
        known = false;
        if (!dev.TryOpen(out HidStream s)) return null;
        using (s)
        {
            var status = Ask(dev, s, Protocols.VxeCmdStatus);
            if (status == null) return null;
            bool? linked = Protocols.ParseVxeStatus(status);
            known = linked != null;
            if (linked != true) return null; // mouse off / asleep
            var power = Ask(dev, s, Protocols.VxeCmdPower);
            return power == null ? null : Protocols.ParseVxePower(power);
        }
    }

    /// <summary>Sends one command and waits for the reply that echoes it (other input frames are skipped).</summary>
    static byte[]? Ask(HidDevice dev, HidStream s, byte cmd)
    {
        var buf = new byte[64];
        for (int attempt = 0; attempt < 2; attempt++)
        {
            // The dongle listens on the control pipe (SET_REPORT), like the Linux driver does; plain write as fallback.
            var req = Protocols.VxeRequest(cmd);
            if (!Hid.SetOutputReport(dev, req)) s.Write(req);
            var deadline = Environment.TickCount64 + 600;
            s.ReadTimeout = 200;
            while (Environment.TickCount64 < deadline)
            {
                try
                {
                    int n = s.Read(buf, 0, buf.Length);
                    if (Protocols.IsVxeReply(buf.AsSpan(0, n), cmd)) return buf[..n];
                }
                catch (TimeoutException) { }
            }
        }
        return null;
    }
}

/// <summary>
/// Delux M800 Pro mouse (Telink receiver 248A:5B2F, cable 248A:5B2E). Read-only status query: feature report
/// 0x0C opcode 0x20 with an incrementing sequence byte. Settings opcodes (DPI, polling, bind...) are never sent.
/// </summary>
public sealed class DeluxProvider(IDeviceSink sink) : PolledProvider(sink, "delux")
{
    static readonly Dictionary<int, string> Known = new() { [0x5B2F] = "Delux M800 Pro", [0x5B2E] = "Delux M800 Pro" };
    static byte seq;

    protected override IEnumerable<(HidDevice, string, DeviceKind, string)> Find() =>
        Known.Keys.SelectMany(pid => Hid.FindAll(Protocols.DeluxVid, pid, Protocols.DeluxUsagePage, Protocols.DeluxUsage))
           .Where(d => d.GetMaxFeatureReportLength() >= 33)
           .Select(d => (d, $"delux:{d.ProductID:x4}", DeviceKind.Mouse, Known[d.ProductID]));

    protected override BatteryReading? Query(HidDevice dev, out bool known)
    {
        known = false;
        if (!dev.TryOpen(out HidStream s)) return null;
        using (s)
        {
            int len = dev.GetMaxFeatureReportLength();
            for (int attempt = 0; attempt < 2; attempt++) // the receiver may need a second query to wake the link
            {
                s.SetFeature(Protocols.DeluxRequest(++seq, len));
                Thread.Sleep(20);
                var reply = new byte[len];
                reply[0] = 0x0C;
                s.GetFeature(reply);
                var r = Protocols.ParseDelux(reply);
                if (r != null) return r;
                Thread.Sleep(20);
            }
        }
        return null;
    }
}

/// <summary>
/// Attack Shark receivers (Xenta chip, VID 0x1D57): listen-only. The receiver volunteers
/// `03 55 40 state pct` every ~2 s. Never writes anything to the device.
/// </summary>
public sealed class XentaProvider(IDeviceSink sink)
{
    static readonly Dictionary<int, string> Known = new()
    {
        [0xFA60] = "Attack Shark X11",
        [0xFA55] = "Attack Shark X11",
    };

    public void Start(CancellationToken ct) => new Thread(() => Run(ct)) { IsBackground = true, Name = "xenta" }.Start();

    void Run(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HidDevice? dev = null;
            foreach (var pid in Known.Keys)
            {
                dev = Hid.FindAll(Protocols.X11Vid, pid, Protocols.X11UsagePage).FirstOrDefault();
                if (dev != null) break;
            }
            if (dev == null || !dev.TryOpen(out HidStream s)) { ct.WaitHandle.WaitOne(5000); continue; }

            string id = $"xenta:{dev.ProductID:x4}";
            string name = Known.TryGetValue(dev.ProductID, out var n) ? n : Hid.ProductName(dev, "Attack Shark");
            bool shown = false;
            try
            {
                using (s)
                {
                    s.ReadTimeout = 1000;
                    var buf = new byte[Math.Max(64, dev.GetMaxInputReportLength())];
                    bool skippedFirst = false;
                    while (!ct.IsCancellationRequested)
                    {
                        int len;
                        try { len = s.Read(buf, 0, buf.Length); } catch (TimeoutException) { continue; }
                        var r = Protocols.ParseX11(buf.AsSpan(0, len));
                        if (r == null) continue;
                        if (!skippedFirst) { skippedFirst = true; continue; } // gauge may report 100% before it settles
                        sink.Report(new DeviceState(id, DeviceKind.Mouse, name, r));
                        shown = true;
                    }
                }
            }
            catch { }
            if (shown) sink.Remove(id); // receiver unplugged
            ct.WaitHandle.WaitOne(3000);
        }
    }
}

/// <summary>Logitech G HUB local websocket API: every Logitech device that reports a battery.</summary>
public sealed class GHubProvider(IDeviceSink sink)
{
    readonly Dictionary<string, (DeviceKind Kind, string Name)> devices = new();

    public void Start(CancellationToken ct)
    {
        if (Environment.GetEnvironmentVariable("TI_NO_GHUB") == null) Task.Run(() => Run(ct)); // test hook: act as if G HUB were closed
    }

    static DeviceKind KindOf(string type)
    {
        type = type.ToLowerInvariant();
        if (type.Contains("head") || type.Contains("audio") || type.Contains("speaker")) return DeviceKind.Headset;
        if (type.Contains("mouse")) return DeviceKind.Mouse;
        if (type.Contains("keyboard")) return DeviceKind.Keyboard;
        return DeviceKind.Other;
    }

    /// <summary>True while G HUB's local API is reachable; the direct Logitech reader stands down meanwhile.</summary>
    public static volatile bool Connected;
    public static event Action? ConnectionChanged;
    static void SetConnected(bool on)
    {
        if (Connected == on) return;
        Connected = on;
        ConnectionChanged?.Invoke();
    }

    void RemoveAll()
    {
        foreach (var id in devices.Keys) sink.Remove("ghub:" + id);
        devices.Clear();
    }

    async Task Run(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var ws = new ClientWebSocket();
                ws.Options.AddSubProtocol("json");
                ws.Options.SetRequestHeader("Origin", "file://");
                await ws.ConnectAsync(new Uri("ws://localhost:9010"), ct);
                SetConnected(true);
                var sendLock = new SemaphoreSlim(1, 1);
                async Task Send(string verb, string path)
                {
                    var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { msgId = "", verb, path }));
                    await sendLock.WaitAsync(ct);
                    try { await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct); } finally { sendLock.Release(); }
                }
                await Send("SUBSCRIBE", "/battery/state/changed");
                await Send("SUBSCRIBE", "/devices/state/changed");
                await Send("GET", "/devices/list");
                using var refreshCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                _ = Task.Run(async () =>
                {
                    while (!refreshCts.IsCancellationRequested)
                    {
                        await Task.Delay(60_000, refreshCts.Token);
                        await Send("GET", "/devices/list");
                    }
                });

                var buffer = new byte[64 * 1024];
                var msg = new MemoryStream();
                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    msg.SetLength(0);
                    WebSocketReceiveResult res;
                    do
                    {
                        res = await ws.ReceiveAsync(buffer, ct);
                        if (res.MessageType == WebSocketMessageType.Close) throw new WebSocketException("closed");
                        msg.Write(buffer, 0, res.Count);
                    } while (!res.EndOfMessage);

                    using var doc = JsonDocument.Parse(msg.ToArray());
                    var root = doc.RootElement;
                    string path = root.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
                    var payload = root.TryGetProperty("payload", out var pl) ? pl : default;

                    if (path == "/devices/list")
                    {
                        var current = new Dictionary<string, (DeviceKind, string)>();
                        if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("deviceInfos", out var infos))
                            foreach (var d in infos.EnumerateArray())
                            {
                                bool hasBatt = d.TryGetProperty("capabilities", out var cap)
                                    && cap.TryGetProperty("hasBatteryStatus", out var hb) && hb.ValueKind == JsonValueKind.True;
                                if (!hasBatt || !d.TryGetProperty("id", out var idEl)) continue;
                                string id = idEl.GetString() ?? "";
                                string type = d.TryGetProperty("deviceType", out var dt) ? dt.GetString() ?? "" : "";
                                string name = d.TryGetProperty("extendedDisplayName", out var en) ? en.GetString() ?? "Logitech" : "Logitech";
                                current[id] = (KindOf(type), name);
                            }
                        foreach (var gone in devices.Keys.Where(id => !current.ContainsKey(id)).ToList())
                        {
                            devices.Remove(gone);
                            sink.Remove("ghub:" + gone);
                        }
                        foreach (var (id, info) in current)
                        {
                            devices[id] = info;
                            await Send("GET", $"/battery/{id}/state");
                        }
                    }
                    else if (path == "/devices/state/changed") await Send("GET", "/devices/list");
                    else if (path.StartsWith("/battery/") && payload.ValueKind == JsonValueKind.Object
                             && payload.TryGetProperty("deviceId", out var did) && did.GetString() is string devId
                             && devices.TryGetValue(devId, out var dinfo))
                    {
                        bool ok = !root.TryGetProperty("result", out var result) || !result.TryGetProperty("code", out var code) || code.GetString() == "SUCCESS";
                        sink.Report(new DeviceState("ghub:" + devId, dinfo.Kind, dinfo.Name, ok ? Protocols.ParseGHubBattery(payload) : null));
                    }
                }
                refreshCts.Cancel();
            }
            catch when (!ct.IsCancellationRequested) { }
            catch { return; }
            SetConnected(false);
            RemoveAll(); // G HUB closed: its devices disappear from the widget
            try { await Task.Delay(15_000, ct); } catch { return; }
        }
    }
}
