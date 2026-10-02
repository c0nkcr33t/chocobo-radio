using System;
using System.IO;

namespace ChocoboRadio;

// Input is already cancellable. Strip ICY blocks before the MP3 parser sees them.
// Position counts audio bytes only; the parser requires it even on non-seekable input.
internal sealed class IcyAudioStream : Stream
{
    private readonly Stream source;
    private readonly int interval;
    private readonly Action<byte[]> metadata;
    private int remaining;
    private long position;

    public IcyAudioStream(Stream source, int interval, Action<byte[]> metadata)
    {
        if (interval < 0) throw new ArgumentOutOfRangeException(nameof(interval));
        this.source = source;
        this.interval = interval;
        this.metadata = metadata;
        remaining = interval;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (offset < 0 || count < 0 || offset > buffer.Length - count) throw new ArgumentOutOfRangeException();
        var total = 0;
        while (total < count)
        {
            if (interval > 0 && remaining == 0)
            {
                var size = source.ReadByte();
                if (size < 0) break;
                if (size > 0)
                {
                    var block = new byte[size * 16]; // protocol maximum: 4080 bytes
                    source.ReadExactly(block);
                    metadata(block);
                }
                remaining = interval;
            }
            var wanted = interval == 0 ? count - total : Math.Min(count - total, remaining);
            var read = source.Read(buffer, offset + total, wanted);
            if (read == 0) break;
            total += read;
            position += read;
            if (interval > 0) remaining -= read;
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
