using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TaskbarIsland.Core;

public enum SpotifyInstall { None, Desktop, Store }

public enum VolumeState
{
    Off,            // switched off
    NotInstalled,   // no Spotify found
    Store,          // Microsoft Store Spotify: its files are sealed, the bridge can't be added
    Waiting,        // bridge added, waiting for Spotify to (re)open with it
    Connected,      // Spotify's volume is live in the card
    Failed,         // couldn't patch / port busy
}

/// <summary>
/// Optional "Spotify volume": adds a tiny script (Spotify/ti-volume.js) to the desktop Spotify's own UI package
/// (%APPDATA%\Spotify\Apps\xpui.spa, a zip) that moves Spotify's volume slider and reports it back over a local
/// WebSocket (127.0.0.1 only, Spotify's page origin only). Switching it off removes the script again. Spotify
/// updates replace the package; while on, the script is re-added automatically.
/// </summary>
public sealed class SpotifyVolume : IDisposable
{
    public const int Port = 47853;
    const string Marker = "<!--taskbar-island-->";
    const string ScriptName = "ti-volume.js";
    const string Origin = "https://xpui.app.spotify.com";

    public event Action? Changed;          // state or volume changed (background thread)
    public VolumeState State { get; private set; } = VolumeState.Off;
    public double Volume { get; private set; } = -1;

    TcpListener? listener;
    NetworkStream? client;
    readonly object sendLock = new();
    CancellationTokenSource? cts;
    readonly AutoResetEvent check = new(false);

    static string SpotifyDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Spotify");
    static string SpaPath => Path.Combine(SpotifyDir, "Apps", "xpui.spa");
    static string SpotifyExe => Path.Combine(SpotifyDir, "Spotify.exe");

    static string Script()
    {
        using var s = typeof(SpotifyVolume).Assembly.GetManifestResourceStream("TaskbarIsland.Spotify.ti-volume.js")!;
        return new StreamReader(s).ReadToEnd();
    }

    // ---------- install detection ----------
    public static SpotifyInstall Detect()
    {
        if (Environment.GetEnvironmentVariable("TI_FAKE_STORE") != null) return SpotifyInstall.Store; // test hook
        foreach (var p in Process.GetProcessesByName("Spotify"))
        {
            try
            {
                var path = p.MainModule?.FileName ?? "";
                if (path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)) return SpotifyInstall.Store;
                if (path.StartsWith(SpotifyDir, StringComparison.OrdinalIgnoreCase)) return SpotifyInstall.Desktop;
            }
            catch { /* Store app processes may refuse MainModule: decided below */ }
            finally { p.Dispose(); }
        }
        if (File.Exists(SpaPath)) return SpotifyInstall.Desktop;
        try
        {
            var pkgs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
            if (Directory.Exists(pkgs) && Directory.EnumerateDirectories(pkgs, "SpotifyAB.SpotifyMusic_*").Any()) return SpotifyInstall.Store;
        }
        catch { }
        return SpotifyInstall.None;
    }

    // ---------- patching ----------
    /// <summary>True when the package already carries the current script.</summary>
    static bool IsPatched(string script)
    {
        using var zip = ZipFile.OpenRead(SpaPath);
        var index = zip.GetEntry("index.html");
        var js = zip.GetEntry(ScriptName);
        if (index == null || js == null) return false;
        using (var r = new StreamReader(index.Open())) if (!r.ReadToEnd().Contains(Marker)) return false;
        using (var r = new StreamReader(js.Open())) return r.ReadToEnd() == script;
    }

    static void Patch(string script)
    {
        // safety copy of Spotify's untouched package (refreshed whenever Spotify brought a new one)
        bool clean;
        using (var zip = ZipFile.OpenRead(SpaPath)) clean = zip.GetEntry(ScriptName) == null;
        if (clean) File.Copy(SpaPath, SpaPath + ".taskbar-island.bak", overwrite: true);

        using var z = ZipFile.Open(SpaPath, ZipArchiveMode.Update);
        var index = z.GetEntry("index.html") ?? throw new InvalidDataException("index.html missing");
        string html;
        using (var r = new StreamReader(index.Open())) html = r.ReadToEnd();
        if (!html.Contains(Marker))
        {
            int at = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            if (at < 0) throw new InvalidDataException("</body> missing");
            html = html.Insert(at, $"<script defer=\"defer\" src=\"/{ScriptName}\"></script>{Marker}");
            index.Delete();
            using var w = new StreamWriter(z.CreateEntry("index.html").Open(), new UTF8Encoding(false));
            w.Write(html);
        }
        z.GetEntry(ScriptName)?.Delete();
        using (var w = new StreamWriter(z.CreateEntry(ScriptName).Open(), new UTF8Encoding(false))) w.Write(script);
    }

    /// <summary>Takes the script back out of Spotify's package (kept working even if Spotify updated meanwhile).</summary>
    public static void Unpatch()
    {
        if (!File.Exists(SpaPath)) return;
        using (var zip = ZipFile.OpenRead(SpaPath)) if (zip.GetEntry(ScriptName) == null) return;
        using var z = ZipFile.Open(SpaPath, ZipArchiveMode.Update);
        z.GetEntry(ScriptName)?.Delete();
        var index = z.GetEntry("index.html");
        if (index == null) return;
        string html;
        using (var r = new StreamReader(index.Open())) html = r.ReadToEnd();
        string tag = $"<script defer=\"defer\" src=\"/{ScriptName}\"></script>{Marker}";
        if (!html.Contains(tag)) return;
        index.Delete();
        using var w = new StreamWriter(z.CreateEntry("index.html").Open(), new UTF8Encoding(false));
        w.Write(html.Replace(tag, ""));
    }

    // ---------- lifecycle ----------
    public void Start()
    {
        if (cts != null) return;
        cts = new CancellationTokenSource();
        var ct = cts.Token;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, Port);
            listener.Start();
        }
        catch (Exception e) { App.Log(e); Set(VolumeState.Failed); return; }
        new Thread(() => AcceptLoop(ct)) { IsBackground = true, Name = "spotify-volume" }.Start();
        new Thread(() =>
        {
            string script = Script();
            while (!ct.IsCancellationRequested)
            {
                try { Ensure(script); } catch (Exception e) { App.Log(e); if (client == null) Set(VolumeState.Failed); }
                WaitHandle.WaitAny(new[] { ct.WaitHandle, check }, TimeSpan.FromSeconds(60));
            }
        }) { IsBackground = true, Name = "spotify-volume-patch" }.Start();
    }

    /// <summary>Re-checks the install / patch now (e.g. right after Spotify was reopened).</summary>
    public void Recheck() => check.Set();

    void Ensure(string script)
    {
        var kind = Detect();
        if (kind == SpotifyInstall.Store) { Set(VolumeState.Store); return; }
        if (kind == SpotifyInstall.None || !File.Exists(SpaPath)) { Set(VolumeState.NotInstalled); return; }
        if (!IsPatched(script)) Patch(script); // first time, or Spotify updated and replaced its package
        if (client == null) Set(VolumeState.Waiting);
    }

    public void Stop()
    {
        cts?.Cancel(); cts = null;
        try { listener?.Stop(); } catch { }
        listener = null;
        try { client?.Close(); } catch { }
        client = null;
        Volume = -1;
        Set(VolumeState.Off);
    }

    public void Dispose() => Stop();


    void Set(VolumeState s)
    {
        if (State == s) return;
        State = s;
        Changed?.Invoke();
    }

    /// <summary>Moves Spotify's own volume slider (0..1).</summary>
    public void SetVolume(double v)
    {
        v = Math.Clamp(v, 0, 1);
        Volume = v;
        SendText(JsonSerializer.Serialize(new { set = Math.Round(v, 3) }));
    }

    /// <summary>Closes and reopens the desktop Spotify so it loads the bridge.</summary>
    public static void RestartSpotify()
    {
        foreach (var p in Process.GetProcessesByName("Spotify")) { try { p.Kill(); } catch { } p.Dispose(); }
        Thread.Sleep(1200);
        try { Process.Start(new ProcessStartInfo(SpotifyExe) { UseShellExecute = true, WorkingDirectory = SpotifyDir }); } catch { }
    }

    // ---------- minimal WebSocket server (one client: Spotify's page) ----------
    void AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient tcp;
            try { tcp = listener!.AcceptTcpClient(); } catch { return; }
            new Thread(() => Serve(tcp, ct)) { IsBackground = true, Name = "spotify-volume-client" }.Start();
        }
    }

    void Serve(TcpClient tcp, CancellationToken ct)
    {
        using (tcp)
        {
            var s = tcp.GetStream();
            try
            {
                if (!Handshake(s)) return;
                try { client?.Close(); } catch { }
                client = s;
                Set(VolumeState.Connected);
                while (!ct.IsCancellationRequested)
                {
                    var (op, payload) = ReadFrame(s);
                    if (op == 8 || op < 0) break;                       // close
                    if (op == 9) { WriteFrame(s, 10, payload); continue; } // ping → pong
                    if (op != 1) continue;
                    using var doc = JsonDocument.Parse(payload);
                    if (doc.RootElement.TryGetProperty("v", out var v) && v.TryGetDouble(out double vol))
                    {
                        Volume = Math.Clamp(vol, 0, 1);
                        Changed?.Invoke();
                    }
                }
            }
            catch { }
            finally
            {
                if (client == s)
                {
                    client = null;
                    Volume = -1;
                    if (!ct.IsCancellationRequested) Set(VolumeState.Waiting);
                }
            }
        }
    }

    static bool Handshake(NetworkStream s)
    {
        var sb = new StringBuilder();
        var one = new byte[1];
        while (sb.Length < 8192 && !sb.ToString().EndsWith("\r\n\r\n"))
        {
            if (s.Read(one, 0, 1) <= 0) return false;
            sb.Append((char)one[0]);
        }
        var lines = sb.ToString().Split("\r\n");
        if (!lines[0].StartsWith("GET /spotify-volume ")) return false;
        string? key = null, origin = null;
        foreach (var l in lines.Skip(1))
        {
            int c = l.IndexOf(':');
            if (c <= 0) continue;
            var name = l[..c].Trim(); var value = l[(c + 1)..].Trim();
            if (name.Equals("Sec-WebSocket-Key", StringComparison.OrdinalIgnoreCase)) key = value;
            if (name.Equals("Origin", StringComparison.OrdinalIgnoreCase)) origin = value;
        }
        if (key == null || origin != Origin) return false; // only Spotify's own page may connect
        var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        var resp = Encoding.ASCII.GetBytes($"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n");
        s.Write(resp);
        return true;
    }

    static (int Op, byte[] Payload) ReadFrame(NetworkStream s)
    {
        var h = ReadExact(s, 2);
        if (h == null) return (-1, []);
        int op = h[0] & 0x0F;
        bool masked = (h[1] & 0x80) != 0;
        long len = h[1] & 0x7F;
        if (len == 126) { var e = ReadExact(s, 2); if (e == null) return (-1, []); len = (e[0] << 8) | e[1]; }
        else if (len == 127) { var e = ReadExact(s, 8); if (e == null) return (-1, []); len = 0; foreach (var b in e) len = (len << 8) | b; }
        if (len > 65536) return (-1, []);
        var mask = masked ? ReadExact(s, 4) : null;
        var data = ReadExact(s, (int)len) ?? [];
        if (mask != null) for (int i = 0; i < data.Length; i++) data[i] ^= mask[i % 4];
        return (op, data);
    }

    static byte[]? ReadExact(NetworkStream s, int n)
    {
        var buf = new byte[n];
        int got = 0;
        while (got < n)
        {
            int r = s.Read(buf, got, n - got);
            if (r <= 0) return null;
            got += r;
        }
        return buf;
    }

    static void WriteFrame(NetworkStream s, int op, byte[] payload)
    {
        var head = payload.Length < 126
            ? new byte[] { (byte)(0x80 | op), (byte)payload.Length }
            : new byte[] { (byte)(0x80 | op), 126, (byte)(payload.Length >> 8), (byte)payload.Length };
        s.Write(head); s.Write(payload);
    }

    void SendText(string text)
    {
        var s = client;
        if (s == null) return;
        lock (sendLock)
        {
            try { WriteFrame(s, 1, Encoding.UTF8.GetBytes(text)); } catch { }
        }
    }
}
