using System.Text;
using Rambutan.Core.Diagnostics;
using Rambutan.Core.Models;
using Rambutan.Core.Playback;

namespace Rambutan.Streams;

/// <summary>
/// Media Hub's metadata is read-only and comes from the media container. Give
/// the system a complete, tagged audio file so its MPRIS metadata and artwork
/// keep changing with the native queue while the application is suspended.
/// Uses containers understood by Ubuntu Touch's thumbnailer: Ogg, M4A, MP3,
/// FLAC. Known codecs are copied; other local formats use lossless FLAC.
/// Each output URI is unique to avoid stale thumbnails.
/// </summary>
public sealed class MediaHubTrackPreparer(
    Func<CancellationToken, Task<string>> ffmpegPath,
    Func<TrackInfo, CancellationToken, Task<string?>> artworkPath) : IDisposable
{
    private readonly HttpClient _http = new();

    public async Task<string> PrepareAsync(TrackInfo track, ResolvedMedia media, string outputBase, CancellationToken ct)
    {
        var source = media.Url;
        var format = FormatFor(media.AudioCodec);
        var output = outputBase + format.Extension;
        var part = output + ".part";
        var download = outputBase + ".download";
        var picture = outputBase + ".jpg";
        var metadata = outputBase + ".txt";
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        try
        {
            var ffmpeg = await ffmpegPath(ct);
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
            {
                if (uri.IsFile) source = uri.LocalPath;
                else
                {
                    if (uri.Scheme is not ("http" or "https"))
                        throw new StreamOpenException("Unsupported audio URI for Media Hub.");
                    await using (var file = File.Create(download))
                    {
                        var expected = await new RangedFetcher(_http).FetchAsync(source, file, null, ct);
                        if (expected is null || expected != file.Length)
                            throw new StreamOpenException("The audio download for Media Hub was incomplete.");
                    }
                    source = download;
                }
            }

            var hasCover = false;
            try
            {
                using var coverTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                coverTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                if (await artworkPath(track, coverTimeout.Token) is { } cover)
                {
                    var image = await ProcessRunner.RunAsync(ffmpeg,
                        ["-hide_banner", "-nostdin", "-loglevel", "error", "-y", "-i", cover,
                         "-frames:v", "1", "-vf", "scale=512:-2", picture], coverTimeout.Token,
                        environment: FfmpegManager.ProcessEnvironment(ffmpeg));
                    hasCover = image.ExitCode == 0 && File.Exists(picture);
                    if (!hasCover) AppLog.Write("media-hub", "Cover conversion failed; keeping track text metadata.");
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Missing/unreachable art must never make otherwise valid audio fail.
                AppLog.Write("media-hub", $"Cover unavailable ({ex.GetType().Name}); keeping track text metadata.");
            }

            await File.WriteAllTextAsync(metadata, FfmpegExportWriter.Metadata(track,
                hasCover && format.CommentCover ? await File.ReadAllBytesAsync(picture, ct) : null), new UTF8Encoding(false), ct);
            var args = new List<string>
            {
                "-hide_banner", "-nostdin", "-loglevel", "error", "-y", "-i", source,
                "-f", "ffmetadata", "-i", metadata,
            };
            if (hasCover && !format.CommentCover) args.AddRange(["-i", picture]);
            args.AddRange(["-map", "0:a:0", "-map_metadata", "1", "-map_metadata:s:a", "-1", "-c:a", format.Encoder]);
            if (hasCover && !format.CommentCover)
                args.AddRange(["-map", "2:v:0", "-c:v", "copy", "-disposition:v:0", "attached_pic",
                    "-metadata:s:v:0", "comment=Cover (front)"]);
            args.AddRange(["-f", format.Muxer, part]);
            var result = await ProcessRunner.RunAsync(ffmpeg, args, ct,
                environment: FfmpegManager.ProcessEnvironment(ffmpeg));
            if (result.ExitCode != 0)
                // Avoid logging signed source URLs from ffmpeg diagnostics.
                throw new StreamOpenException($"Media Hub audio preparation failed (ffmpeg {result.ExitCode}).");
            ct.ThrowIfCancellationRequested();
            File.Move(part, output);
            return new Uri(Path.GetFullPath(output)).AbsoluteUri;
        }
        finally
        {
            foreach (var path in new[] { part, download, picture, metadata })
                try { File.Delete(path); } catch (IOException) { }
        }
    }

    internal static (string Extension, string Muxer, string Encoder, bool CommentCover) FormatFor(string codec)
    {
        codec = codec.ToLowerInvariant();
        if (codec.StartsWith("opus") || codec == "vorbis") return (".ogg", "ogg", "copy", true);
        if (codec.StartsWith("mp4a") || codec is "aac" or "alac") return (".m4a", "ipod", "copy", false);
        if (codec is "mp3") return (".mp3", "mp3", "copy", false);
        return (".flac", "flac", codec == "flac" ? "copy" : "flac", false);
    }

    public void Dispose() => _http.Dispose();
}
