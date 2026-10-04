using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ChocoboRadio;

internal sealed class OggPageReader(Stream source)
{
    public async ValueTask<OggPage?> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        // A read returning zero before a new page begins is normal end-of-stream.
        // Once any page byte has arrived, however, missing bytes mean truncation.
        var capturePattern = new byte[4];
        var firstByteCount = await source.ReadAsync(
            capturePattern.AsMemory(0, 1),
            cancellationToken).ConfigureAwait(false);

        if (firstByteCount == 0) return null;

        await source.ReadExactlyAsync(
            capturePattern.AsMemory(1),
            cancellationToken).ConfigureAwait(false);

        if (!capturePattern.AsSpan().SequenceEqual("OggS"u8))
            throw new InvalidDataException("Invalid Ogg capture pattern.");

        var header = new byte[23];
        await source.ReadExactlyAsync(header, cancellationToken)
            .ConfigureAwait(false);

        var version = header[0];
        if (version != 0)
            throw new InvalidDataException($"Unsupported Ogg version: {version}.");

        var headerType = header[1];
        var granulePosition = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(2, 8));
        var serialNumber = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(10, 4));
        var sequenceNumber = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(14, 4));
        var checksum = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(18, 4));
        var segmentCount = header[22];

        var lacingValues = new byte[segmentCount];
        await source.ReadExactlyAsync(lacingValues, cancellationToken)
            .ConfigureAwait(false);

        var bodyLength = 0;
        foreach (var value in lacingValues) bodyLength += value;

        var body = new byte[bodyLength];
        await source.ReadExactlyAsync(body, cancellationToken)
            .ConfigureAwait(false);

        return new OggPage(
            headerType,
            granulePosition,
            serialNumber,
            sequenceNumber,
            checksum,
            lacingValues,
            body);
    }
}
