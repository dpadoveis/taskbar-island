using System.Text.Json;

namespace TaskbarIsland.Core;

/// <param name="Level">0..100, null = unknown (charging over cable without a gauge)</param>
public sealed record BatteryReading(int? Level, bool Charging);

/// <summary>Pure parsers for the battery protocols (no I/O, fully testable).</summary>
public static class Protocols
{
    // ---------- AULA F75 (2.4G receiver 3554:FA09, collection FF02/0002) ----------
    public const int AulaVid = 0x3554, AulaPid = 0xFA09;
    public const int AulaUsagePage = 0xFF02, AulaUsage = 0x0002;
    const byte AulaReportId = 0x13, AulaCmdBattery = 0x4A;
    const int AulaLen = 20;

    static byte AulaChecksum(ReadOnlySpan<byte> f)
    {
        int s = 0;
        for (int i = 0; i < 19; i++) s += f[i];
        return (byte)(s & 0xFF);
    }

    public static byte[] AulaRequest()
    {
        var f = new byte[AulaLen];
        f[0] = AulaReportId; f[1] = AulaCmdBattery;
        f[19] = AulaChecksum(f);
        return f;
    }

    public static BatteryReading? ParseAula(ReadOnlySpan<byte> f)
    {
        if (f.Length < AulaLen || f[0] != AulaReportId || (f[1] & 0x7F) != AulaCmdBattery) return null;
        if (f[19] != AulaChecksum(f)) return null;
        byte level = f[5], state = f[6];
        if (state == 0x10) return new BatteryReading(null, true); // cable in: level pinned at 100
        if (level < 1 || level > 100) return null;
        return new BatteryReading(level, false);
    }

    // ---------- Attack Shark X11 (2.4G receiver 1D57:FA60, collection 000A) ----------
    public const int X11Vid = 0x1D57, X11Pid = 0xFA60;
    public const int X11UsagePage = 0x000A;

    /// <summary>Passive input report: 03 55 40 &lt;state&gt; &lt;percent&gt;. state 1=discharging 2=full 3=charging.</summary>
    public static BatteryReading? ParseX11(ReadOnlySpan<byte> f)
    {
        if (f.Length < 5 || f[0] != 0x03 || f[1] != 0x55 || (f[2] != 0x40 && f[2] != 0x41)) return null;
        byte state = f[3], pct = f[4];
        if (state is < 1 or > 3 || pct > 100) return null;
        if (state == 2) return new BatteryReading(100, true);
        return new BatteryReading(pct, state == 3);
    }

    // ---------- MCHOSE V9 PRO headset (2.4G dongle 291D:385D, collection FF90/0001) ----------
    public const int MchoseVid = 0x291D, MchosePid = 0x385D;
    public const int MchoseUsagePage = 0xFF90, MchoseUsage = 0x0001;

    /// <summary>The only command ever sent: battery request, output report 0x55 (64 bytes).</summary>
    public static byte[] MchoseRequest(int length = 64)
    {
        var f = new byte[Math.Max(length, 3)];
        f[0] = 0x55; f[1] = 0x65; f[2] = 0x01;
        return f;
    }

    /// <summary>Reply / push: 55 65 &lt;pct&gt; &lt;state&gt;. state 2 discharging, 3 charging, 4 full, 0x1A asleep;
    /// 0 = cable plugged (level frozen, so no percentage is shown). Returns null for "asleep / no reading".</summary>
    public static BatteryReading? ParseMchose(ReadOnlySpan<byte> f, out bool isBatteryFrame)
    {
        isBatteryFrame = f.Length >= 4 && f[0] == 0x55 && f[1] == 0x65;
        if (!isBatteryFrame) return null;
        byte pct = f[2], state = f[3];
        if (pct > 100) { isBatteryFrame = false; return null; }
        return state switch
        {
            0x04 => new BatteryReading(100, true),
            0x03 or 0x00 => new BatteryReading(null, true),
            0x02 when pct > 0 => new BatteryReading(pct, false),
            _ => null, // 0x1A asleep / off, or unknown
        };
    }

    // ---------- Delux M800 Pro mouse (2.4G receiver 248A:5B2F, wired 5B2E; collection FF00/0001) ----------
    public const int DeluxVid = 0x248A;
    public const int DeluxUsagePage = 0xFF00, DeluxUsage = 0x0001;

    /// <summary>Read-only status query: feature report 0x0C, opcode 0x20, incrementing sequence at byte 4.</summary>
    public static byte[] DeluxRequest(byte seq, int length = 33)
    {
        var f = new byte[Math.Max(length, 33)];
        f[0] = 0x0C; f[1] = 0x01; f[2] = 0x20; f[4] = seq;
        return f;
    }

    /// <summary>Feature reply: [0]=0x0C [2]=0x20, [6] presence, [18] percent, [19] state (0 discharging, 1 charging, 2 full).
    /// Returns null when the mouse isn't connected / no reading.</summary>
    public static BatteryReading? ParseDelux(ReadOnlySpan<byte> r)
    {
        if (r.Length < 20 || r[0] != 0x0C || r[2] != 0x20) return null;
        byte pct = r[18], state = r[19];
        if (pct > 100) return null;
        bool connected = r[6] != 0 || pct > 0;
        if (!connected) return null;
        if (state == 2) return new BatteryReading(100, true);
        if (pct == 0) return null;
        return new BatteryReading(pct, state == 1);
    }

    // ---------- Logitech G HUB websocket ----------
    public static BatteryReading? ParseGHubBattery(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object) return null;
        if (!payload.TryGetProperty("percentage", out var p) || p.ValueKind != JsonValueKind.Number
            || !p.TryGetInt32(out int pct) || pct < 0 || pct > 100) return null;
        bool charging = payload.TryGetProperty("charging", out var c) && c.ValueKind == JsonValueKind.True;
        bool full = payload.TryGetProperty("fullyCharged", out var fc) && fc.ValueKind == JsonValueKind.True;
        return new BatteryReading(pct, charging || (full && pct >= 99));
    }
}
