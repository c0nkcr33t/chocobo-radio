using System;
using System.IO;
using System.Threading;

namespace ChocoboRadio;

// NAudio's frame parser reads synchronously. Bridge those reads to cancellable HTTP
// reads on the worker, so Stop/unload never waits for a network read on the game thread.
internal sealed class CancellableReadStream(Stream source, CancellationToken token) : Stream
{
    private long position;
    public override int Read(byte[] buffer, int offset, int count)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var total = 0;
        // Mp3Frame expects full reads and queries Position even for non-seekable streams.
        while (total < count)
        {
            timeout.Token.ThrowIfCancellationRequested();
            var read = source.ReadAsync(buffer.AsMemory(offset + total, count - total), timeout.Token)
                .AsTask().GetAwaiter().GetResult();
            if (read == 0) break;
            total += read;
            position += read;
        }
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
