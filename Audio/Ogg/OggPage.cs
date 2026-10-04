namespace ChocoboRadio;

internal sealed record OggPage(
    byte HeaderType,
    long GranulePosition,
    uint SerialNumber,
    uint SequenceNumber,
    uint Checksum,
    byte[] LacingValues,
    byte[] Body)
{
    public bool IsContinuation => (HeaderType & 0x01) != 0;
    public bool IsBeginningOfStream => (HeaderType & 0x02) != 0;
    public bool IsEndOfStream => (HeaderType & 0x04) != 0;
}