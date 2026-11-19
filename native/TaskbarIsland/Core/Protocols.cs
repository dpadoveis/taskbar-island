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
    public const int MchoseUsagePage = 0xFF90; // any usage: the collection's usage differs between dongle firmwares

    /// <summary>The only command ever sent: battery request, output report 0x55 (64 bytes).</summary>
    public static byte[] MchoseRequest(int length = 64)
    {
        var f = new byte[Math.Max(length, 3)];
        f[0] = 0x55; f[1] = 0x65; f[2] = 0x01;
        return f;
    }

    /// <summary>Reply / push: 55 65 &lt;pct&gt; &lt;state&gt;. state 2 discharging, 3 charging, 4 full, 0x1A asleep.
    /// state 0: newer dongle firmware (0013+) with older headset firmware answers 55 65 pct 00 while on battery,
    /// so a real percentage is shown; 0 with pct 0 means nothing to read. Returns null for "asleep / no reading".</summary>
    public static BatteryReading? ParseMchose(ReadOnlySpan<byte> f, out bool isBatteryFrame)
    {
        isBatteryFrame = f.Length >= 4 && f[0] == 0x55 && f[1] == 0x65;
        if (!isBatteryFrame) return null;
        byte pct = f[2], state = f[3];
        if (pct > 100) { isBatteryFrame = false; return null; }
        return state switch
        {
            0x04 => new BatteryReading(100, true),
            0x03 => new BatteryReading(null, true),
            0x02 or 0x00 when pct > 0 => new BatteryReading(pct, false),
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

    // ---------- VXE / ATK / Pulsar mice (Kysona protocol; 2.4G dongles 3554:F5xx and 373B:xxxx) ----------
    // 17-byte input/output report 0x08 on USB interface 1, last byte = 0x55 - sum(bytes 0..15).
    // Layout from the Linux "HID: pulsar" driver (VXE Dragonfly R1 Pro 3554:F58A, R1 SE+ 373B:1085, Pulsar X2 ...).
    public const int VxeVid = 0x3554, AtkVid = 0x373B;
    public const int VxeLen = 17;
    public const byte VxeReportId = 0x08, VxeCmdStatus = 0x03, VxeCmdPower = 0x04;

    static byte VxeChecksum(ReadOnlySpan<byte> f)
    {
        byte sum = 0;
        for (int i = 0; i < VxeLen - 1; i++) sum += f[i];
        return (byte)(0x55 - sum);
    }

    /// <summary>Read-only query: 08 &lt;cmd&gt; 00 … &lt;checksum&gt;. Only STATUS (0x03) and POWER (0x04) are ever sent.</summary>
    public static byte[] VxeRequest(byte cmd)
    {
        var f = new byte[VxeLen];
        f[0] = VxeReportId; f[1] = cmd;
        f[VxeLen - 1] = VxeChecksum(f);
        return f;
    }

    /// <summary>A valid reply to <paramref name="cmd"/>: report 0x08, command echoed, checksum correct.</summary>
    public static bool IsVxeReply(ReadOnlySpan<byte> f, byte cmd) =>
        f.Length >= VxeLen && f[0] == VxeReportId && f[1] == cmd && f[VxeLen - 1] == VxeChecksum(f);

    /// <summary>STATUS reply: [6] = 1 when the mouse is linked to the dongle, 0 when off / asleep.</summary>
    public static bool? ParseVxeStatus(ReadOnlySpan<byte> f) =>
        IsVxeReply(f, VxeCmdStatus) && f[6] <= 1 ? f[6] == 1 : null;

    /// <summary>POWER reply: [6] = percent, [7] = 1 on cable (charging, or full at 100).</summary>
    public static BatteryReading? ParseVxePower(ReadOnlySpan<byte> f)
    {
        if (!IsVxeReply(f, VxeCmdPower) || f[6] > 100 || f[7] > 1) return null;
        bool cable = f[7] == 1;
        if (f[6] == 0 && !cable) return null;
        return new BatteryReading(f[6], cable);
    }

    // ---------- Logitech headsets, "Centurion" (G522 dongle 046D:0B18, collection FFA0, report 0x50, 64 bytes) ----------
    // frame = 50 23 &lt;len = payload+1&gt; &lt;flags 0&gt; &lt;payload&gt;, padded to 64. Direct payload: [featIdx, fn|swid, params].
    // The headset sits behind the dongle's "bridge" feature (0x0003); battery = feature 0x0104 on the headset.
    // Layout from HeadsetControl (logitech_centurion_protocol.hpp) and checked against a real G522.
    public const int LogiVid = 0x046D, CenturionPage = 0xFFA0;
    public const byte CenturionReport = 0x50, CenturionPrefix = 0x23, CenturionSwId = 0x01;
    public const ushort CenturionBridge = 0x0003;
    public const byte CenturionBattery = 0x04; // with its high byte: feature 0x0104

    public static byte[] CenturionFrame(ReadOnlySpan<byte> payload, int length = 64)
    {
        var f = new byte[Math.Max(length, payload.Length + 4)];
        f[0] = CenturionReport; f[1] = CenturionPrefix; f[2] = (byte)(payload.Length + 1); f[3] = 0;
        payload.CopyTo(f.AsSpan(4));
        return f;
    }

    /// <summary>Root getFeature(id) sent to the dongle itself.</summary>
    public static byte[] CenturionGetFeature(ushort feature) =>
        CenturionFrame([0x00, CenturionSwId, (byte)(feature >> 8), (byte)feature]);

    /// <summary>Request forwarded to the headset through the bridge: [bridge, 0x11, size, 00, subIdx, fn|swid, params].</summary>
    public static byte[] CenturionBridged(byte bridge, byte subIndex, ReadOnlySpan<byte> pars)
    {
        int size = 3 + pars.Length;
        var p = new byte[4 + size];
        p[0] = bridge; p[1] = 0x10 | CenturionSwId; p[2] = (byte)((size >> 8) & 0x0F); p[3] = (byte)size;
        p[4] = 0x00; p[5] = subIndex; p[6] = CenturionSwId;
        pars.CopyTo(p.AsSpan(7));
        return CenturionFrame(p);
    }

    /// <summary>Reply to the dongle's Root getFeature: the feature index (0 = not supported), or null if not this reply.</summary>
    public static byte? CenturionFeatureIndex(ReadOnlySpan<byte> f) =>
        f.Length >= 7 && f[0] == CenturionReport && f[1] == CenturionPrefix && f[4] == 0x00 && f[5] == CenturionSwId ? f[6] : null;

    /// <summary>Final bridged reply from the headset's feature <paramref name="subIndex"/>; returns its data bytes.</summary>
    public static byte[]? CenturionBridgedData(ReadOnlySpan<byte> f, byte bridge, byte subIndex)
    {
        if (f.Length < 12 || f[0] != CenturionReport || f[1] != CenturionPrefix || f[4] != bridge || f[5] != 0x10) return null;
        if (f[8] != 0x00 || f[9] != subIndex) return null;
        int end = Math.Min(f.Length, 3 + f[2]); // payload = f[4 .. 4 + len - 1)
        return end > 11 ? f[11..end].ToArray() : null;
    }

    /// <summary>Bridge event "headset disconnected": payload [bridge, 00, 00, 00].</summary>
    public static bool CenturionHeadsetGone(ReadOnlySpan<byte> f, byte bridge) =>
        f.Length >= 8 && f[0] == CenturionReport && f[1] == CenturionPrefix && f[4] == bridge && f[5] == 0x00 && f[6] == 0x00 && f[7] == 0x00;

    /// <summary>Battery data: [0] = state of charge, [1] = percentage G HUB shows, [2] = 1/2 charging.</summary>
    public static BatteryReading? ParseCenturionBattery(ReadOnlySpan<byte> d)
    {
        if (d.Length < 3) return null;
        int pct = d[1] is > 0 and <= 100 ? d[1] : d[0];
        if (pct > 100) return null;
        bool charging = d[2] is 1 or 2;
        if (pct == 0 && !charging) return null;
        return new BatteryReading(pct, charging);
    }

    // ---------- Logitech HID++ 2.0 (Unifying / Lightspeed / Bolt receivers and cabled devices; FF00/0002, report 0x11) ----------
    public const int HidppPage = 0xFF00, HidppLongUsage = 0x0002, HidppLongLen = 20;
    public const byte HidppLong = 0x11, HidppSwId = 0x0A;
    public const ushort HidppUnifiedBattery = 0x1004, HidppBatteryStatus = 0x1000, HidppNameType = 0x0005;

    public static byte[] HidppRequest(byte device, byte featIdx, byte fn, params byte[] pars)
    {
        var f = new byte[HidppLongLen];
        f[0] = HidppLong; f[1] = device; f[2] = featIdx; f[3] = (byte)((fn << 4) | HidppSwId);
        pars.AsSpan(0, Math.Min(pars.Length, 16)).CopyTo(f.AsSpan(4));
        return f;
    }

    /// <summary>The reply to a request: same device / feature / function. Null when not; empty when an HID++ error.</summary>
    public static byte[]? HidppReply(ReadOnlySpan<byte> f, byte device, byte featIdx, byte fn)
    {
        if (f.Length < 7 || f[0] != HidppLong || f[1] != device) return null;
        if (f[2] == 0xFF && f[3] == featIdx && (f[4] & 0x0F) == HidppSwId) return [];
        if (f[2] != featIdx || f[3] != (byte)((fn << 4) | HidppSwId)) return null;
        return f[4..].ToArray();
    }

    /// <summary>0x1004 getStatus: [0] percent, [2] charging (0 discharging, 1/2 charging, 3 full), [3] external power.</summary>
    public static BatteryReading? ParseUnifiedBattery(ReadOnlySpan<byte> d)
    {
        if (d.Length < 3 || d[0] > 100) return null;
        if (d[2] == 3) return new BatteryReading(100, true);
        bool charging = d[2] is 1 or 2;
        if (d[0] == 0) return charging ? new BatteryReading(null, true) : null;
        return new BatteryReading(d[0], charging);
    }

    /// <summary>0x1000 getBatteryLevelStatus: [0] percent, [2] status (0 discharging, 1 charging, 2 almost full, 3 full).</summary>
    public static BatteryReading? ParseBatteryStatus(ReadOnlySpan<byte> d)
    {
        if (d.Length < 3 || d[0] > 100) return null;
        if (d[2] == 3) return new BatteryReading(100, true);
        bool charging = d[2] is 1 or 2;
        if (d[2] > 3) return null; // error states
        if (d[0] == 0) return charging ? new BatteryReading(null, true) : null;
        return new BatteryReading(d[0], charging);
    }

    /// <summary>0x0005 getDeviceType → kind.</summary>
    public static DeviceKind HidppKind(byte type) => type switch
    {
        0 or 2 => DeviceKind.Keyboard,
        3 or 4 or 5 or 6 => DeviceKind.Mouse,
        8 => DeviceKind.Headset,
        _ => DeviceKind.Other,
    };

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
