namespace ChocoboRadio;

// A packet is the unit consumed by a codec. It may occupy part of one page or
// be assembled from pieces stored across multiple pages.
internal sealed record OggPacket(
    uint SerialNumber,
    byte[] Data,
    bool IsBeginningOfStream,
    bool IsEndOfStream);
