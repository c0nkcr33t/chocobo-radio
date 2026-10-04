using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace ChocoboRadio;

internal sealed record StationProbeResult(StationStreamType StreamType, string? StationName);

internal static class StationProbe
{
    public static async Task<StationProbeResult> InspectAsync(string url, CancellationToken token)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
            throw new ArgumentException("Enter a valid HTTP(S) stream URL first.");

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ChocoboRadio/0.3");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Icy-MetaData", "1");
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var stationName = response.Headers.TryGetValues("icy-name", out var names)
            ? CleanText(names.FirstOrDefault())
            : null;
        if (stationName?.Length == 0) stationName = null;

        var interval = 0;
        if (response.Headers.TryGetValues("icy-metaint", out var intervals) &&
            (!int.TryParse(intervals.SingleOrDefault(), out interval) || interval <= 0))
            throw new InvalidDataException("Station sent an invalid ICY metadata interval.");

        using var network = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var buffered = new BufferedStream(network, 16384);
        using var timed = new CancellableReadStream(buffered, token);
        using var audio = new IcyAudioStream(timed, interval, _ => { });
        var result = await DetectAsync(audio, stationName, token).ConfigureAwait(false);
        return result;
    }

    internal static async ValueTask<StationProbeResult> DetectAsync(
        Stream audio,
        string? stationName = null,
        CancellationToken token = default)
    {
        var prefix = new byte[4];
        await audio.ReadExactlyAsync(prefix, token).ConfigureAwait(false);
        using var replay = new PrefixStream(prefix, audio);

        if (prefix.AsSpan().SequenceEqual("OggS"u8))
            throw new InvalidDataException("This version supports direct MP3 streams only.");

        var frame = Mp3Frame.LoadFromStream(replay)
            ?? throw new InvalidDataException("Stream is not MP3.");
        return new StationProbeResult(StationStreamType.Mp3, stationName);
    }

    private static string CleanText(string? value)
        => new string((value ?? "").Where(c => !char.IsControl(c)).Take(255).ToArray()).Trim();

    // Detection consumes four bytes. Replay them before continuing with the
    // underlying forward-only HTTP stream so format parsers see byte zero.
    private sealed class PrefixStream(byte[] prefix, Stream source) : Stream
    {
        private int prefixOffset;
        private long position;

        public override int Read(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            if (offset < 0 || count < 0 || offset > buffer.Length - count) throw new ArgumentOutOfRangeException();
            var total = 0;
            if (prefixOffset < prefix.Length)
            {
                var copied = Math.Min(count, prefix.Length - prefixOffset);
                prefix.AsSpan(prefixOffset, copied).CopyTo(buffer.AsSpan(offset, copied));
                prefixOffset += copied;
                total += copied;
            }
            if (total < count) total += source.Read(buffer, offset + total, count - total);
            position += total;
            return total;
        }

        public override int Read(Span<byte> buffer)
        {
            var total = 0;
            if (prefixOffset < prefix.Length)
            {
                var copied = Math.Min(buffer.Length, prefix.Length - prefixOffset);
                prefix.AsSpan(prefixOffset, copied).CopyTo(buffer);
                prefixOffset += copied;
                total += copied;
            }
            if (total < buffer.Length) total += source.Read(buffer[total..]);
            position += total;
            return total;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
