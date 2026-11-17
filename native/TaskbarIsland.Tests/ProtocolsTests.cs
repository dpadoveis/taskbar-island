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

    [Fact] public void GHub()
    {
        Assert.Equal(new BatteryReading(60, false), Protocols.ParseGHubBattery(J("{\"percentage\":60,\"charging\":false}")));
        Assert.Equal(new BatteryReading(80, true), Protocols.ParseGHubBattery(J("{\"percentage\":80,\"charging\":true}")));
        Assert.Null(Protocols.ParseGHubBattery(J("{}")));
    }
}
