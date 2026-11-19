using System.Text.Json;
using TaskbarIsland.Core;
using Xunit;

namespace TaskbarIsland.Tests;

public class ProtocolsTests
{
    static byte[] H(string hex) => Convert.FromHexString(hex);
    static JsonElement J(string json) => JsonDocument.Parse(json).RootElement;

    [Fact] public void AulaRequestBytes() =>
        Assert.Equal(H("134A" + string.Concat(Enumerable.Repeat("00", 17)) + "5D"), Protocols.AulaRequest());

    [Fact] public void AulaOnBattery97() =>
        Assert.Equal(new BatteryReading(97, false), Protocols.ParseAula(H("134A0100026101000000000000000000000000C2")));

    [Fact] public void AulaRealCapture100() =>
        Assert.Equal(new BatteryReading(100, false), Protocols.ParseAula(H("134a0100026401000000000000000000000000c5")));

    [Fact] public void AulaCableInIsChargingWithoutLevel() =>
        Assert.Equal(new BatteryReading(null, true), Protocols.ParseAula(H("134A0100026410000000000000000000000000D4")));

    [Fact] public void AulaBadChecksumIgnored() =>
        Assert.Null(Protocols.ParseAula(H("134A0100026101000000000000000000000000C3")));

    [Fact] public void AulaOtherCommandIgnored()
    {
        var f = new byte[20]; f[0] = 0x13; f[1] = 0x0A; f[19] = (byte)(f.Take(19).Sum(b => b) & 0xFF);
        Assert.Null(Protocols.ParseAula(f));
    }

    [Fact] public void X11Discharging() =>
        Assert.Equal(new BatteryReading(64, false), Protocols.ParseX11(H("0355400140")));

    [Fact] public void X11ChargingAndFull()
    {
        Assert.Equal(new BatteryReading(50, true), Protocols.ParseX11(H("0355400332")));
        Assert.Equal(new BatteryReading(100, true), Protocols.ParseX11(H("0355400264")));
    }

    [Fact] public void X11OtherSubtypesIgnored()
    {
        Assert.Null(Protocols.ParseX11(H("0355100200"))); // DPI stage
        Assert.Null(Protocols.ParseX11(H("0355500103"))); // ack
    }

    // ---- MCHOSE V9 PRO (frames from the PyFlat/Device-Battery-Info#24 captures) ----
    [Fact] public void MchoseRequestBytes()
    {
        var f = Protocols.MchoseRequest();
        Assert.Equal(64, f.Length);
        Assert.Equal(H("556501"), f[..3]);
        Assert.All(f[3..], b => Assert.Equal(0, b));
    }

    [Fact] public void MchoseDischarging()
    {
        Assert.Equal(new BatteryReading(90, false), Protocols.ParseMchose(H("55655A02000000"), out var isBatt));
        Assert.True(isBatt);
    }

    [Fact] public void MchoseChargingAndFull()
    {
        Assert.Equal(new BatteryReading(null, true), Protocols.ParseMchose(H("55653203"), out _)); // charging push
        Assert.Equal(new BatteryReading(100, true), Protocols.ParseMchose(H("55656404"), out _));
    }

    [Fact] public void MchoseAsleepAndForeignFrames()
    {
        Assert.Null(Protocols.ParseMchose(H("5565001A"), out var asleep)); Assert.True(asleep);   // off: a battery frame without reading
        Assert.Null(Protocols.ParseMchose(H("55110200"), out var other)); Assert.False(other);   // EQ-mode frame: ignored
        Assert.Null(Protocols.ParseMchose(H("0601"), out var media)); Assert.False(media);       // media keys: ignored
    }

    [Fact] public void MchoseNewDongleFirmwareStateZero()
    {
        // dongle 0013 + headset 0002 answers 55 65 pct 00 while on battery (HeadsetControl PR #624 report)
        Assert.Equal(new BatteryReading(72, false), Protocols.ParseMchose(H("55654800"), out var b)); Assert.True(b);
        Assert.Null(Protocols.ParseMchose(H("55650000"), out _));
    }

    // ---- VXE / ATK / Pulsar (layout from the Linux "HID: pulsar" driver) ----
    static byte[] Vxe(string hex16)
    {
        var f = H(hex16); var r = new byte[17]; f.CopyTo(r, 0);
        byte sum = 0; for (int i = 0; i < 16; i++) sum += r[i];
        r[16] = (byte)(0x55 - sum); return r;
    }

    [Fact] public void VxeRequestBytes()
    {
        Assert.Equal(H("080300000000000000000000000000004A"), Protocols.VxeRequest(0x03)); // 0x55-0x0B
        Assert.Equal(H("0804000000000000000000000000000049"), Protocols.VxeRequest(0x04));
    }

    [Fact] public void VxeStatusAndPower()
    {
        Assert.True(Protocols.ParseVxeStatus(Vxe("080300000000010000")));
        Assert.False(Protocols.ParseVxeStatus(Vxe("080300000000000000")));
        Assert.Equal(new BatteryReading(64, false), Protocols.ParseVxePower(Vxe("08040000000040000F a0".Replace(" ", ""))));
        Assert.Equal(new BatteryReading(87, true), Protocols.ParseVxePower(Vxe("080400000000570110")));
    }

    [Fact] public void VxeRejectsBadFrames()
    {
        var bad = Vxe("080400000000400000"); bad[16] ^= 1;
        Assert.Null(Protocols.ParseVxePower(bad));                               // checksum
        Assert.Null(Protocols.ParseVxePower(Vxe("080300000000400000")));          // wrong echo
        Assert.Null(Protocols.ParseVxePower(Vxe("080400000000C80000")));          // > 100 %
        Assert.Null(Protocols.ParseVxeStatus(Vxe("080300000000050000")));         // bogus availability
    }

    // ---- Logitech Centurion (frames captured from a real G522, dongle 046D:0B18) ----
    static byte[] Pad(string hex) { var b = new byte[64]; H(hex).CopyTo(b, 0); return b; }

    [Fact] public void CenturionRequests()
    {
        Assert.Equal(Pad("5023050000010003"), Protocols.CenturionGetFeature(0x0003));
        Assert.Equal(Pad("50230a0003110005000001 0104".Replace(" ", "")), Protocols.CenturionBridged(3, 0, [0x01, 0x04]));
        Assert.Equal(Pad("5023080003110003000501"), Protocols.CenturionBridged(3, 5, []));
    }

    [Fact] public void CenturionReplies()
    {
        Assert.Equal((byte)3, Protocols.CenturionFeatureIndex(H("50230600000103000100")));
        Assert.Null(Protocols.CenturionBridgedData(H("50230300031100000000000000"), 3, 0)); // ack only
        Assert.Equal(H("050003"), Protocols.CenturionBridgedData(H("50230b0003100006000001050003000000"), 3, 0));
        var batt = Protocols.CenturionBridgedData(H("50230b0003100006000501373200000000"), 3, 5);
        Assert.Equal(new BatteryReading(50, false), Protocols.ParseCenturionBattery(batt)); // G HUB showed 50 %
        Assert.Equal(new BatteryReading(80, true), Protocols.ParseCenturionBattery(H("555001")));
        Assert.True(Protocols.CenturionHeadsetGone(H("5023050003000000"), 3));
    }

    // ---- Logitech HID++ 2.0 ----
    [Fact] public void HidppRequestAndReply()
    {
        Assert.Equal(H("110100" + "0A" + "1004" + "000000000000000000000000000000"[..28]), Protocols.HidppRequest(1, 0, 0, 0x10, 0x04));
        Assert.Equal(H("06"), Protocols.HidppReply(H("1101000A06000000000000000000000000000000"), 1, 0, 0)![..1]);
        Assert.Empty(Protocols.HidppReply(H("1101FF060A050000000000000000000000000000"), 1, 6, 1)!);  // HID++ error
        Assert.Null(Protocols.HidppReply(H("1102000A06000000000000000000000000000000"), 1, 0, 0));    // other device
        Assert.Equal(new BatteryReading(64, false), Protocols.ParseUnifiedBattery(H("40080000")));
        Assert.Equal(new BatteryReading(64, true), Protocols.ParseUnifiedBattery(H("40080101")));
        Assert.Equal(new BatteryReading(100, true), Protocols.ParseUnifiedBattery(H("64080301")));
        Assert.Equal(new BatteryReading(30, false), Protocols.ParseBatteryStatus(H("1E0000")));
    }

    // ---- Delux M800 Pro (layout from krisgfx9/Wireless-Device-Battery-Monitor#156 and xb-bx/m800-pro-driver) ----
    static byte[] DeluxReply(byte presence, byte pct, byte state)
    {
        var r = new byte[33]; r[0] = 0x0C; r[1] = 0x01; r[2] = 0x20; r[6] = presence; r[18] = pct; r[19] = state;
        return r;
    }

    [Fact] public void DeluxRequestBytes()
    {
        var f = Protocols.DeluxRequest(7);
        Assert.Equal(33, f.Length);
        Assert.Equal(H("0C0120000700"), f[..6]);
    }

    [Fact] public void DeluxReadings()
    {
        Assert.Equal(new BatteryReading(75, false), Protocols.ParseDelux(DeluxReply(1, 75, 0)));
        Assert.Equal(new BatteryReading(40, true), Protocols.ParseDelux(DeluxReply(1, 40, 1)));
        Assert.Equal(new BatteryReading(100, true), Protocols.ParseDelux(DeluxReply(1, 100, 2)));
    }

    [Fact] public void DeluxNotConnectedOrInvalid()
    {
        Assert.Null(Protocols.ParseDelux(DeluxReply(0, 0, 0)));   // mouse off / not linked
        Assert.Null(Protocols.ParseDelux(DeluxReply(1, 150, 0))); // impossible percentage
        var wrong = DeluxReply(1, 50, 0); wrong[2] = 0x05;        // not a status reply
        Assert.Null(Protocols.ParseDelux(wrong));
    }

    [Fact] public void GHub()
    {
        Assert.Equal(new BatteryReading(60, false), Protocols.ParseGHubBattery(J("{\"percentage\":60,\"charging\":false}")));
        Assert.Equal(new BatteryReading(80, true), Protocols.ParseGHubBattery(J("{\"percentage\":80,\"charging\":true}")));
        Assert.Null(Protocols.ParseGHubBattery(J("{}")));
    }
}
