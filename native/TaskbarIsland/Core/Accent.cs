using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TaskbarIsland.Core;

public static class Accent
{
    public static readonly Color NeutralAccent = Color.FromRgb(0xE8, 0xE8, 0xE8);
    public static readonly Color NeutralTint = Color.FromRgb(0x0E, 0x0E, 0x0E);

    /// <summary>Accent: vivid cover colour that reads on a dark island. Tint: a deep shade of it, for backgrounds.</summary>
    public static (Color Accent, Color Tint) FromCover(BitmapSource cover)
    {
        var small = new FormatConvertedBitmap(
            new TransformedBitmap(cover, new ScaleTransform(12.0 / cover.PixelWidth, 12.0 / cover.PixelHeight)),
            PixelFormats.Bgra32, null, 0);
        int w = small.PixelWidth, h = small.PixelHeight;
        var px = new byte[w * h * 4];
        small.CopyPixels(px, w * 4, 0);

        double bestScore = -1, br = 0, bg = 0, bb = 0, bs = 0;
        for (int i = 0; i < px.Length; i += 4)
        {
            double b = px[i] / 255.0, g = px[i + 1] / 255.0, r = px[i + 2] / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), l = (max + min) / 2;
            double s = max == min ? 0 : (max - min) / (1 - Math.Abs(2 * l - 1));
            double score = s * 1.6 + l * 0.6 - Math.Abs(l - 0.55);
            if (score > bestScore) { bestScore = score; br = r; bg = g; bb = b; bs = s; }
        }
        if (bs < 0.18) return (NeutralAccent, NeutralTint); // greyscale cover -> neutral

        double mx = Math.Max(br, Math.Max(bg, bb)), mn = Math.Min(br, Math.Min(bg, bb)), d = mx - mn, hue = 0;
        if (d > 0)
            hue = mx == br ? ((bg - bb) / d) % 6 : mx == bg ? (bb - br) / d + 2 : (br - bg) / d + 4;
        hue = (hue * 60 + 360) % 360;
        return (Hsl(hue, 0.85, 0.70), Hsl(hue, 0.45, 0.13));
    }

    /// <summary>Accent + deep background tint for a fixed (preset) colour.</summary>
    public static (Color Accent, Color Tint) FromPreset(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b)), d = mx - mn, hue = 0;
        if (d < 0.08) return (c, NeutralTint); // white/grey
        hue = mx == r ? ((g - b) / d) % 6 : mx == g ? (b - r) / d + 2 : (r - g) / d + 4;
        hue = (hue * 60 + 360) % 360;
        return (c, Hsl(hue, 0.45, 0.13));
    }

    static Color Hsl(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = l - c / 2;
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0d), < 120 => (x, c, 0d), < 180 => (0d, c, x),
            < 240 => (0d, x, c), < 300 => (x, 0d, c), _ => (c, 0d, x),
        };
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
