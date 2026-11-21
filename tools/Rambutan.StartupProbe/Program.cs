using System.Diagnostics;
using System.Text.Json;
using Rambutan.Core.Models;
using Rambutan.Core.Playback;
using Rambutan.Streams;

// Device-only diagnostic. Uses a new directory for each run; never clears the
// application's cache or prints signed media URLs. Run with the bundled tools
// on PATH and a JSON queue copied from Rambutan (only public track metadata).
if (args.Length < 4)
    throw new ArgumentException("Usage: StartupProbe TOOLS QUEUE_JSON OUTPUT_ROOT TRACK_INDEX [REPEATS]");
var tools = args[0];
using var queue = JsonDocument.Parse(await File.ReadAllTextAsync(args[1]));
var row = queue.RootElement.GetProperty("tracks")[int.Parse(args[3])];
var track = JsonSerializer.Deserialize<TrackInfo>(row.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
var repeats = args.Length > 4 ? int.Parse(args[4]) : 1;
using var http = new HttpClient();
for (var run = 0; run < repeats; ++run)
{
    var directory = Path.Combine(args[2], Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var watch = Stopwatch.StartNew();
    var resolver = new StreamResolver(new YtDlpManager(tools)) { PreferLowLatencyClient = true };
    var resolved = await resolver.ResolveAsync(track.VideoId);
    var resolve = watch.Elapsed.TotalSeconds;
    var cache = new AudioDiskCache(Path.Combine(directory, "audio")) { MaxSizeBytes = 256L << 20 };
    using var source = new CachingStreamSource(resolver, cache) { PreferLocalPlayback = true };
    var media = await source.ResolveAsync(track.VideoId);
    var download = watch.Elapsed.TotalSeconds - resolve;
    using var preparer = new MediaHubTrackPreparer(new FfmpegManager(tools).EnsureExecutableAsync,
        async (item, ct) =>
        {
            if (item.ThumbnailUrl is null) return null;
            var path = Path.Combine(directory, "cover.img");
            await File.WriteAllBytesAsync(path, await http.GetByteArrayAsync(item.ThumbnailUrl, ct), ct);
            return path;
        });
    var output = await preparer.PrepareAsync(track, media, Path.Combine(directory, "tagged"), default);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        track = track.Title, run, resolveSeconds = resolve, downloadSeconds = download,
        metadataSeconds = watch.Elapsed.TotalSeconds - resolve - download,
        totalSeconds = watch.Elapsed.TotalSeconds,
        bytes = new FileInfo(new Uri(output).LocalPath).Length,
        codec = resolved.AudioCodec
    }));
    Directory.Delete(directory, recursive: true);
}
