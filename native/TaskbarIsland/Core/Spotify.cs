using System.IO;
using System.Security.Cryptography;
using Windows.Media.Control;

namespace TaskbarIsland.Core;

public sealed record MediaInfo(string Title, string Artist, string Album, bool Playing, double Position, double Duration, byte[]? Thumbnail, string ThumbId);

/// <summary>Spotify (desktop app) via the Windows media controls. Reports null when Spotify isn't open.</summary>
public sealed class SpotifySource(Action<MediaInfo?> update)
{
    GlobalSystemMediaTransportControlsSessionManager? manager;
    GlobalSystemMediaTransportControlsSession? session;
    (string, string)? thumbKey;
    byte[]? thumb;
    string thumbId = "";

    public void Start(CancellationToken ct) => Task.Run(() => Run(ct));

    public async void Command(string name)
    {
        var s = session;
        if (s == null) return;
        try
        {
            switch (name)
            {
                case "toggle": await s.TryTogglePlayPauseAsync(); break;
                case "next": await s.TrySkipNextAsync(); break;
                case "prev": await s.TrySkipPreviousAsync(); break;
            }
            await Task.Delay(250);
            await Poll();
        }
        catch { }
    }

    async Task Run(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                await Poll();
            }
            catch { manager = null; update(null); }
            try { await Task.Delay(1000, ct); } catch { return; }
        }
    }

    async Task Poll()
    {
        if (manager == null) return;
        var s = manager.GetSessions().FirstOrDefault(x =>
            (x.SourceAppUserModelId ?? "").Contains("spotify", StringComparison.OrdinalIgnoreCase));
        session = s;
        if (s == null) { update(null); return; }
        var props = await s.TryGetMediaPropertiesAsync();
        var info = s.GetPlaybackInfo();
        bool playing = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        string title = props.Title ?? "";
        if (title.Length == 0) { update(null); return; }
        var tl = s.GetTimelineProperties();
        double duration = (tl.EndTime - tl.StartTime).TotalSeconds, position = tl.Position.TotalSeconds;
        if (playing)
        {
            double elapsed = (DateTimeOffset.Now - tl.LastUpdatedTime).TotalSeconds;
            if (elapsed is > 0 and < 3600) position += elapsed;
        }
        if (duration > 0) position = Math.Clamp(position, 0, duration);
        var key = (title, props.Artist ?? "");
        if (key != thumbKey)
        {
            thumbKey = key;
            thumb = await ReadThumb(props.Thumbnail);
            thumbId = thumb == null ? "" : Convert.ToHexString(MD5.HashData(thumb))[..12];
        }
        update(new MediaInfo(title, props.Artist ?? props.AlbumArtist ?? "", props.AlbumTitle ?? "", playing, position, Math.Max(duration, 0), thumb, thumbId));
    }

    static async Task<byte[]?> ReadThumb(Windows.Storage.Streams.IRandomAccessStreamReference? r)
    {
        if (r == null) return null;
        try
        {
            using var ras = await r.OpenReadAsync();
            if (ras.Size == 0 || ras.Size > 8_000_000) return null;
            using var stream = ras.AsStreamForRead();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            return ms.ToArray();
        }
        catch { return null; }
    }
}
