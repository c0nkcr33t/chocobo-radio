using System;
using System.Buffers.Binary;
using System.IO;

namespace ChocoboRadio;

internal sealed record OggFlacIdentification(
    byte MajorVersion,
    byte MinorVersion,
    ushort HeaderPacketCount,
    FlacStreamInfo StreamInfo)
{
    private const int MappingHeaderLength = 9;
    private const int NativeMarkerLength = 4;
    private const int MetadataHeaderLength = 4;
    private const int StreamInfoLength = 34;

    public static OggFlacIdentification Parse(OggPacket packet)
    {
        var data = packet.Data.AsSpan();
        var requiredLength = MappingHeaderLength + NativeMarkerLength +
                             MetadataHeaderLength + StreamInfoLength;

        if (!packet.IsBeginningOfStream)
            throw new InvalidDataException("Ogg-FLAC identification must be the beginning-of-stream packet.");
        if (data.Length < requiredLength)
            throw new InvalidDataException("Truncated Ogg-FLAC identification packet.");
        if (data[0] != 0x7f || !data.Slice(1, 4).SequenceEqual("FLAC"u8))
            throw new InvalidDataException("Packet is not an Ogg-FLAC identification packet.");

        var majorVersion = data[5];
        var minorVersion = data[6];
        if (majorVersion != 1)
            throw new InvalidDataException($"Unsupported Ogg-FLAC mapping version {majorVersion}.{minorVersion}.");

        var headerPacketCount = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(7, 2));
        if (!data.Slice(9, 4).SequenceEqual("fLaC"u8))
            throw new InvalidDataException("Ogg-FLAC packet is missing the native FLAC marker.");

        // A FLAC metadata header uses the high bit for "last block", the low
        // seven bits for its type, and the following three bytes for length.
        var metadataType = data[13] & 0x7f;
        var metadataLength = ReadUInt24BigEndian(data.Slice(14, 3));
        if (metadataType != 0 || metadataLength != StreamInfoLength)
            throw new InvalidDataException("Ogg-FLAC identification does not contain a valid STREAMINFO block.");

        var streamInfo = data.Slice(17, StreamInfoLength);
        var minimumBlockSize = BinaryPrimitives.ReadUInt16BigEndian(streamInfo.Slice(0, 2));
        var maximumBlockSize = BinaryPrimitives.ReadUInt16BigEndian(streamInfo.Slice(2, 2));
        var minimumFrameSize = ReadUInt24BigEndian(streamInfo.Slice(4, 3));
        var maximumFrameSize = ReadUInt24BigEndian(streamInfo.Slice(7, 3));

        // FLAC packs four values into the next 64 bits:
        // sample rate (20), channels minus one (3), bits per sample minus one
        // (5), and total samples (36).
        var packedAudioInfo = BinaryPrimitives.ReadUInt64BigEndian(streamInfo.Slice(10, 8));
        var sampleRate = (int)(packedAudioInfo >> 44);
        var channels = (int)((packedAudioInfo >> 41) & 0x07) + 1;
        var bitsPerSample = (int)((packedAudioInfo >> 36) & 0x1f) + 1;
        var totalSamples = (long)(packedAudioInfo & 0x0f_ffff_ffffUL);
        var md5 = streamInfo.Slice(18, 16).ToArray();

        return new OggFlacIdentification(
            majorVersion,
            minorVersion,
            headerPacketCount,
            new FlacStreamInfo(
                minimumBlockSize,
                maximumBlockSize,
                minimumFrameSize,
                maximumFrameSize,
                sampleRate,
                channels,
                bitsPerSample,
                totalSamples,
                md5));
    }

    private static int ReadUInt24BigEndian(ReadOnlySpan<byte> bytes)
        => (bytes[0] << 16) | (bytes[1] << 8) | bytes[2];
}
