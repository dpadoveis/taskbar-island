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
        Assert.Equal(new BatteryReading(null, true), Protocols.ParseMchose(H("55655A00"), out _)); // cable in, level frozen
        Assert.Equal(new BatteryReading(100, true), Protocols.ParseMchose(H("55656404"), out _));
    }

    [Fact] public void MchoseAsleepAndForeignFrames()
    {
        Assert.Null(Protocols.ParseMchose(H("5565001A"), out var asleep)); Assert.True(asleep);   // off: a battery frame without reading
        Assert.Null(Protocols.ParseMchose(H("55110200"), out var other)); Assert.False(other);   // EQ-mode frame: ignored
        Assert.Null(Protocols.ParseMchose(H("0601"), out var media)); Assert.False(media);       // media keys: ignored
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
