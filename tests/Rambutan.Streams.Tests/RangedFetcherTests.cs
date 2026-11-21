using System.Net;
using System.Net.Http.Headers;

namespace Rambutan.Streams.Tests;

public sealed class RangedFetcherTests
{
    private const int Chunk = 1 << 20;

    [Fact]
    public async Task DownloadsBoundedConcurrentChunksAndWritesThemInOrder()
    {
        var bytes = new byte[8 * Chunk + 17];
        Random.Shared.NextBytes(bytes);
        var active = 0;
        var peak = 0;
        var fourStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler(async (start, ct) =>
        {
            if (start > 0)
            {
                var count = Interlocked.Increment(ref active);
                peak = Math.Max(peak, count);
                if (count == 4) fourStarted.TrySetResult();
                await fourStarted.Task.WaitAsync(ct);
                // Complete later chunks first: the output must still be intact.
                await Task.Delay((9 - (int)(start / Chunk)) * 5, ct);
                Interlocked.Decrement(ref active);
            }
            return Response(bytes, start);
        }));
        using var output = new MemoryStream();
        var progress = new List<long>();
        var length = await new RangedFetcher(http).FetchAsync("https://audio.test/track", output,
            (written, _) => progress.Add(written), TestContext.Current.CancellationToken);
        Assert.Equal(bytes.Length, length);
        Assert.Equal(bytes, output.ToArray());
        Assert.Equal(4, peak);
        Assert.Equal(0, active);
        Assert.Equal(Enumerable.Range(1, 8).Select(i => (long)i * Chunk).Append(bytes.Length), progress);
    }

    [Theory]
    [InlineData("status")]
    [InlineData("start")]
    [InlineData("end")]
    [InlineData("total")]
    [InlineData("short")]
    [InlineData("long")]
    public async Task RejectsInconsistentOrTruncatedChunks(string failure)
    {
        var bytes = new byte[2 * Chunk + 17];
        using var http = new HttpClient(new Handler((start, _) =>
        {
            var result = Response(bytes, start);
            if (start == Chunk)
            {
                if (failure == "status") result.StatusCode = HttpStatusCode.OK;
                else if (failure is "short" or "long")
                {
                    result.Content = new ByteArrayContent(new byte[Chunk + (failure == "short" ? -1 : 1)]);
                    result.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, start + Chunk - 1, bytes.Length);
                }
                else result.Content.Headers.ContentRange = new ContentRangeHeaderValue(
                    start + (failure == "start" ? 1 : 0), start + Chunk - 1 - (failure == "end" ? 1 : 0),
                    bytes.Length + (failure == "total" ? 1 : 0));
            }
            return Task.FromResult(result);
        }));
        using var output = new MemoryStream();
        Assert.Null(await new RangedFetcher(http).FetchAsync("https://audio.test/track", output, null,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CancellationWaitsForAllOutstandingRequestsToFinish()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var active = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler(async (start, ct) =>
        {
            if (start == 0) return Response(new byte[8 * Chunk], start);
            if (Interlocked.Increment(ref active) == 4) started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, ct); }
            finally { Interlocked.Decrement(ref active); }
            throw new InvalidOperationException("Unreachable");
        }));
        using var output = new MemoryStream();
        var download = new RangedFetcher(http).FetchAsync("https://audio.test/track", output, null, cancel.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download);
        Assert.Equal(0, active);
    }

    private static HttpResponseMessage Response(byte[] bytes, long start)
    {
        var end = Math.Min(start + Chunk, bytes.Length) - 1;
        var result = new HttpResponseMessage(HttpStatusCode.PartialContent)
        {
            Content = new ByteArrayContent(bytes, (int)start, (int)(end - start + 1))
        };
        result.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, end, bytes.Length);
        return result;
    }

    private sealed class Handler(Func<long, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            send(request.Headers.Range!.Ranges.Single().From!.Value, ct);
    }
}
