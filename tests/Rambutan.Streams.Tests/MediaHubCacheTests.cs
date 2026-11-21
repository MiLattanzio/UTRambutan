using System.Net;
using System.Net.Http.Headers;
using Rambutan.Core.Settings;

namespace Rambutan.Streams.Tests;

public sealed class MediaHubCacheTests
{
    [Fact]
    public async Task NativePlaybackWaitsForOneSharedDownloadAndUsesTheCompleteLocalFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "rambutan-native-cache-" + Guid.NewGuid());
        try
        {
            var cache = new AudioDiskCache(directory) { MaxSizeBytes = 1024 };
            var resolver = new StreamResolver((id, quality, ct) => Task.FromResult(
                new ResolvedStream { VideoId = id, Url = "https://audio.test/stream", Itag = "251",
                    AudioCodec = "opus", AverageBitrateKbps = 128, Quality = quality,
                    ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(2) }));
            var handler = new DelayedAudio();
            using var source = new CachingStreamSource(resolver, cache, handler) { PreferLocalPlayback = true };
            var first = source.ResolveAsync("a");
            var second = source.ResolveAsync("a");
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            handler.Release.SetResult();
            var result = await Task.WhenAll(first, second);
            Assert.Equal(1, handler.Requests);
            Assert.Equal(result[0].Url, result[1].Url);
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(result[0].Url, TestContext.Current.CancellationToken));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private sealed class DelayedAudio : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Requests;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Requests);
            Started.TrySetResult();
            await Release.Task.WaitAsync(ct);
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent([1, 2, 3]) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 2, 3);
            return response;
        }
    }
}
