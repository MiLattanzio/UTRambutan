using System.Net;
using System.Net.Http.Headers;

namespace Rambutan.Streams;

/// <summary>
/// Downloads a googlevideo URL in ranged chunks. Googlevideo throttles unranged
/// GETs to ~2× the media bitrate; ranged chunks download at full speed (measured
/// 36 KB/s vs 13 MB/s). Shared by the cache fill and downloads.
/// </summary>
internal sealed class RangedFetcher(HttpClient http)
{
    private const int ChunkSize = 1 << 20;
    private const int ParallelChunks = 4;
    private const long MaxFileBytes = 256 << 20; // sanity guard, ~50× a typical track

    // HttpClient.Timeout covers headers only for streamed bodies; without a chunk
    // deadline a stalled read wedges the fetch forever.
    private static readonly TimeSpan ChunkTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Ranged-chunk download into <paramref name="target"/>; returns the
    /// total length to commit, or null to discard the partial file.</summary>
    /// <param name="progress">Called after each chunk with (bytes written, total).</param>
    public async Task<long?> FetchAsync(
        string url, Stream target, Action<long, long>? progress, CancellationToken ct)
    {
        // Learn the length from the first response's headers, then overlap body
        // transfers. Buffer only one window (4 MiB), and write in order so even
        // a non-seekable destination works. A slow chunk no longer delays the
        // requests for every subsequent chunk of a cold Media Hub track.
        using var transfer = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var firstTimeout = Deadline(transfer.Token);
        using var firstRequest = Request(url, 0);
        using var first = await http.SendAsync(firstRequest,
            HttpCompletionOption.ResponseHeadersRead, firstTimeout.Token);
        if (first.Content.Headers.ContentRange?.Length is not { } total || total is <= 0 or > MaxFileBytes)
            return null;

        var pending = new Queue<Task<byte[]?>>();
        pending.Enqueue(ReadChunkAsync(first, 0, total, firstTimeout.Token));
        long next = ChunkSize;
        long written = 0;
        try
        {
            while (written < total)
            {
                while (pending.Count < ParallelChunks && next < total)
                {
                    pending.Enqueue(FetchChunkAsync(url, next, total, transfer.Token));
                    next += ChunkSize;
                }
                if (await pending.Dequeue() is not { } bytes)
                    return null;
                await target.WriteAsync(bytes, ct);
                written += bytes.Length;
                progress?.Invoke(written, total);
            }
            return total;
        }
        finally
        {
            await transfer.CancelAsync();
            // Do not leave readers running against a disposed client on failure
            // or cancellation, or leave their exceptions unobserved.
            try { await Task.WhenAll(pending); } catch { }
        }
    }

    private static CancellationTokenSource Deadline(CancellationToken ct)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ChunkTimeout);
        return timeout;
    }

    private static HttpRequestMessage Request(string url, long start)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(start, start + ChunkSize - 1);
        return request;
    }

    private async Task<byte[]?> FetchChunkAsync(string url, long start, long total, CancellationToken ct)
    {
        using var timeout = Deadline(ct);
        using var request = Request(url, start);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        return await ReadChunkAsync(response, start, total, timeout.Token);
    }

    private static async Task<byte[]?> ReadChunkAsync(HttpResponseMessage response, long start, long total, CancellationToken ct)
    {
        var end = Math.Min(start + ChunkSize, total) - 1;
        var range = response.Content.Headers.ContentRange;
        if (response.StatusCode != HttpStatusCode.PartialContent ||
            range is not { Unit: "bytes" } || range.From != start || range.To != end || range.Length != total)
            return null;
        var bytes = new byte[(int)(end - start + 1)];
        await using var content = await response.Content.ReadAsStreamAsync(ct);
        var read = await content.ReadAtLeastAsync(bytes, bytes.Length, throwOnEndOfStream: false, ct);
        // Headers alone cannot prove completeness. Reject short/oversized bodies
        // and changed ranges instead of committing corrupted cache entries.
        if (read != bytes.Length || await content.ReadAsync(new byte[1], ct) != 0)
            return null;
        return bytes;
    }
}
