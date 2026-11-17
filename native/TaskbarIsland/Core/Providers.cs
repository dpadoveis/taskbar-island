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

    public void Start(CancellationToken ct) => Task.Run(() => Run(ct));

    static DeviceKind KindOf(string type)
    {
        type = type.ToLowerInvariant();
        if (type.Contains("head") || type.Contains("audio") || type.Contains("speaker")) return DeviceKind.Headset;
        if (type.Contains("mouse")) return DeviceKind.Mouse;
        if (type.Contains("keyboard")) return DeviceKind.Keyboard;
        return DeviceKind.Other;
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
            RemoveAll(); // G HUB closed: its devices disappear from the widget
            try { await Task.Delay(15_000, ct); } catch { return; }
        }
    }
}
