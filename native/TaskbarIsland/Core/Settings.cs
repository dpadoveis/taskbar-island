using System.IO;
using System.Text.Json;

namespace TaskbarIsland.Core;

/// <summary>User choices, saved to %APPDATA%\TaskbarIsland\settings.json.</summary>
public sealed class Settings
{
    /// <summary>Hide (and free memory) while a fullscreen / listed game is in front.</summary>
    public bool GameMode { get; set; } = true;

    /// <summary>"auto" = colour taken from the album cover; otherwise one of <see cref="Presets"/> (hex).</summary>
    public string Color { get; set; } = "auto";

    /// <summary>Spotify volume in the card (adds a small bridge script to the desktop Spotify). Off by default.</summary>
    public bool SpotifyVolume { get; set; }

    public static readonly string[] Presets = ["#FF9BD0", "#60CDFF", "#6CCB5F", "#B4A0FF", "#FFB070", "#E8E8E8"];

    static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskbarIsland", "settings.json");

    public static Settings Load()
    {
        try
        {
            var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new();
            if (s.Color != "auto" && !Presets.Contains(s.Color, StringComparer.OrdinalIgnoreCase)) s.Color = "auto";
            return s;
        }
        catch { return new(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
