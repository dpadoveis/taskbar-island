using System.Windows;
using System.Windows.Media;

namespace TaskbarIsland;

/// <summary>
/// How the Spotify text (Figtree) is rasterised. Compared side by side (Display / Ideal / no hinting / 2x-3x
/// bitmap cache): Ideal formatting with hinting turned off gave the smoothest, most even strokes; bitmap
/// caching blurred and dimmed the grey text.
/// </summary>
static class TextLook
{
    public static void Apply(FrameworkElement e)
    {
        TextOptions.SetTextFormattingMode(e, TextFormattingMode.Ideal);
        TextOptions.SetTextHintingMode(e, TextHintingMode.Animated); // no hinting: glyphs keep their true shapes
    }
}
