using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ChocoboRadio;

// Presents packets from an Ogg-FLAC logical stream as the native FLAC byte
// stream expected by a FLAC decoder. The Ogg mapping's nine-byte prefix is the
// only data removed; metadata blocks and audio frames are already native FLAC.
internal sealed class OggFlacStream : Stream
{
    private readonly OggPacketReader packets;
    private readonly CancellationToken cancellationToken;
    private readonly uint serialNumber;
    private readonly Action<FlacVorbisComments>? metadata;
    private int remainingHeaderPackets;
    private byte[] current;
    private int currentOffset;
    private bool currentEndsLogicalStream;
    private bool logicalStreamEnded;
    private bool disposed;

    public OggFlacIdentification Identification { get; }

    private OggFlacStream(
        OggPacketReader packets,
        OggPacket firstPacket,
        OggFlacIdentification identification,
        Action<FlacVorbisComments>? metadata,
        CancellationToken cancellationToken)
    {
        this.packets = packets;
        this.cancellationToken = cancellationToken;
        serialNumber = firstPacket.SerialNumber;
        this.metadata = metadata;
        remainingHeaderPackets = identification.HeaderPacketCount;
        Identification = identification;

        // Bytes 0..8 belong to the Ogg-FLAC mapping. Starting at byte 9 is
        // exactly what a native decoder expects: "fLaC", then STREAMINFO.
        current = firstPacket.Data[9..];
        currentEndsLogicalStream = firstPacket.IsEndOfStream;
    }

    public static async ValueTask<OggFlacStream> CreateAsync(
        Stream source,
        Action<FlacVorbisComments>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var packets = new OggPacketReader(new OggPageReader(source));
        return await CreateNextAsync(packets, metadata, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The Ogg stream contained no packets.");
    }

    public static async ValueTask<OggFlacStream?> CreateNextAsync(
        OggPacketReader packets,
        Action<FlacVorbisComments>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var firstPacket = await packets.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (firstPacket == null) return null;
        var identification = OggFlacIdentification.Parse(firstPacket);

        return new OggFlacStream(
            packets,
            firstPacket,
            identification,
            metadata,
            cancellationToken);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset > buffer.Length - count) throw new ArgumentException("Offset and count exceed the buffer length.");

        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var total = 0;

        while (!destination.IsEmpty)
        {
            if (currentOffset == current.Length &&
                (logicalStreamEnded || currentEndsLogicalStream || !LoadNextPacket())) break;

            var available = current.AsSpan(currentOffset);
            var count = Math.Min(available.Length, destination.Length);
            available[..count].CopyTo(destination);
            currentOffset += count;
            destination = destination[count..];
            total += count;
        }

        return total;
    }

    private bool LoadNextPacket()
    {
        // SimpleFlac has a synchronous Stream API. This adapter runs on the
        // radio's background worker, so synchronously waiting here does not
        // block Dalamud's framework or UI thread.
        var packet = packets.ReadAsync(cancellationToken)
            .AsTask()
            .GetAwaiter()
            .GetResult();

        if (packet == null) return false;
        if (packet.SerialNumber != serialNumber)
        {
            // Icecast commonly starts a fresh logical stream to publish new
            // Vorbis comments at a track boundary. Leave its identification
            // packet for the next decoder instance.
            packets.PushBack(packet);
            logicalStreamEnded = true;
            return false;
        }

        if (remainingHeaderPackets > 0)
        {
            // The mapping requires the first header after identification to be
            // a Vorbis-comment metadata block.
            if (remainingHeaderPackets == Identification.HeaderPacketCount)
                metadata?.Invoke(FlacVorbisComments.Parse(packet.Data));
            remainingHeaderPackets--;
        }

        current = packet.Data;
        currentOffset = 0;
        currentEndsLogicalStream = packet.IsEndOfStream;
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        disposed = true;
        base.Dispose(disposing);
    }

    public override bool CanRead => !disposed;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
