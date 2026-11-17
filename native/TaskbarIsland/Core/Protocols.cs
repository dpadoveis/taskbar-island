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
