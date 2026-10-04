namespace ChocoboRadio;

// Values carried by FLAC's mandatory STREAMINFO metadata block.
internal sealed record FlacStreamInfo(
    ushort MinimumBlockSize,
    ushort MaximumBlockSize,
    int MinimumFrameSize,
    int MaximumFrameSize,
    int SampleRate,
    int Channels,
    int BitsPerSample,
    long TotalSamples,
    byte[] Md5Signature);
