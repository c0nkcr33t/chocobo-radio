using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ChocoboRadio;

internal sealed class FlacVorbisComments
{
    private readonly Dictionary<string, List<string>> values = new(StringComparer.OrdinalIgnoreCase);

    public string Vendor { get; private init; } = "";
    public string? First(string key) => values.TryGetValue(key, out var found) && found.Count > 0 ? found[0] : null;

    public TrackInfo? Track
    {
        get
        {
            var title = First("TITLE")?.Trim();
            if (string.IsNullOrEmpty(title)) return null;
            var artist = First("ARTIST")?.Trim();
            if (!string.IsNullOrEmpty(artist)) return new TrackInfo(artist, title);
            var split = title.IndexOf(" - ", StringComparison.Ordinal);
            return split > 0
                ? new TrackInfo(title[..split].Trim(), title[(split + 3)..].Trim())
                : new TrackInfo("", title);
        }
    }

    public string? StationName => First("ORGANIZATION") ?? First("STATION") ?? First("RADIOSTATION");

    public static FlacVorbisComments Parse(byte[] metadataPacket)
    {
        var data = metadataPacket.AsSpan();
        if (data.Length < 4 || (data[0] & 0x7f) != 4)
            throw new InvalidDataException("FLAC header packet is not a Vorbis-comment block.");

        var declaredLength = (data[1] << 16) | (data[2] << 8) | data[3];
        if (declaredLength != data.Length - 4)
            throw new InvalidDataException("Invalid FLAC Vorbis-comment block length.");

        var payload = data[4..];
        var offset = 0;
        var result = new FlacVorbisComments
        {
            Vendor = ReadString(payload, ref offset),
        };
        var count = ReadUInt32(payload, ref offset);
        if (count > 100_000) throw new InvalidDataException("FLAC Vorbis-comment count is unreasonable.");

        for (var i = 0U; i < count; i++)
        {
            var comment = ReadString(payload, ref offset);
            var separator = comment.IndexOf('=');
            if (separator <= 0) continue;
            var key = comment[..separator];
            var value = comment[(separator + 1)..];
            if (!result.values.TryGetValue(key, out var list)) result.values[key] = list = new List<string>();
            list.Add(value);
        }

        if (offset != payload.Length)
            throw new InvalidDataException("FLAC Vorbis-comment block has trailing data.");
        return result;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, ref int offset)
    {
        if (data.Length - offset < 4) throw new EndOfStreamException("Truncated FLAC Vorbis-comment block.");
        var value = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
        offset += 4;
        return value;
    }

    private static string ReadString(ReadOnlySpan<byte> data, ref int offset)
    {
        var length = ReadUInt32(data, ref offset);
        if (length > int.MaxValue || data.Length - offset < (int)length)
            throw new EndOfStreamException("Truncated FLAC Vorbis-comment string.");
        var value = new UTF8Encoding(false, true).GetString(data.Slice(offset, (int)length));
        offset += (int)length;
        return value;
    }
}
