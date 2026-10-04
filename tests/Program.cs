using ChocoboRadio;
using NAudio.Wave;
using SimpleFlac;

// A valid MPEG-1 Layer III frame header, followed by synthetic payload. No decoding
// here: exercise the actual NAudio parser against short, non-seekable HTTP-like reads.
var bytes = new byte[417 * 2];
foreach (var offset in new[] { 0, 417 })
{
    bytes[offset] = 0xff;
    bytes[offset + 1] = 0xfb;
    bytes[offset + 2] = 0x90;
}
using (var source = new FragmentedStream(bytes))
using (var stream = new CancellableReadStream(source, CancellationToken.None))
{
    var first = Mp3Frame.LoadFromStream(stream);
    var second = Mp3Frame.LoadFromStream(stream);
    Check(first is { SampleRate: 44100, FrameLength: 417 }, "first fragmented MP3 frame");
    Check(second.FileOffset == 417, "position tracking for second frame");
    Check(stream.Position == bytes.Length, "total position");
    Check(Mp3Frame.LoadFromStream(stream) == null, "clean EOF");
}
using (var source = new FragmentedStream(bytes[..100]))
using (var stream = new CancellableReadStream(source, CancellationToken.None))
{
    try { Mp3Frame.LoadFromStream(stream); throw new Exception("Expected truncated-frame error"); }
    catch (EndOfStreamException) { }
}
using (var cancellation = new CancellationTokenSource())
using (var source = new StalledStream())
using (var stream = new CancellableReadStream(source, cancellation.Token))
{
    var read = Task.Run(() => stream.Read(new byte[4], 0, 4));
    await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
    cancellation.Cancel();
    try { await read.WaitAsync(TimeSpan.FromSeconds(2)); throw new Exception("Expected cancellation"); }
    catch (OperationCanceledException) { }
}
bool muted = false;
var writes = new List<bool>();
var music = new GameMusicController(() => muted, value => { muted = value; writes.Add(value); }, ex => throw ex);
music.Update(true);
Check(muted && music.OwnsMute, "suppression owns its mute");
music.Update(true);
Check(writes.Count == 1, "no repeated writes while playing");
music.Update(false);
Check(!muted && !music.OwnsMute, "restore on stop/failure/unload");
muted = true;
music.Update(true);
Check(!music.OwnsMute, "pre-existing user mute remains a radio mute");
music.Update(false);
Check(muted, "preserve pre-existing mute");
muted = false;
music.Update(true);
muted = false; // user unmutes during playback
music.Update(true);
Check(!music.OwnsMute && !muted, "respect user override");
music.Update(true);
Check(!muted, "do not re-mute after user override");
music.Update(false);
music.Update(true);
Check(muted && music.OwnsMute, "next session can suppress again");
music.Update(false);
var failRestore = false;
var errors = 0;
var retry = new GameMusicController(() => muted, value =>
{
    if (failRestore) throw new IOException("Settings unavailable");
    muted = value;
}, _ => errors++);
retry.Update(true);
failRestore = true;
retry.Update(false);
Check(retry.OwnsMute && errors == 1, "retain ownership on restore failure");
failRestore = false;
retry.Update(false);
Check(!muted && !retry.OwnsMute, "retry restoration");
// Metadata may split even an MP3 header; the decoder must receive identical audio bytes.
using (var icyBytes = new MemoryStream())
{
    var meta = System.Text.Encoding.UTF8.GetBytes("StreamTitle='Björk - Jóga';");
    var interval = 19;
    for (var offset = 0; offset < bytes.Length; offset += interval)
    {
        var length = Math.Min(interval, bytes.Length - offset);
        icyBytes.Write(bytes, offset, length);
        if (length == interval)
        {
            if (offset == 0)
            {
                var blocks = (meta.Length + 15) / 16;
                icyBytes.WriteByte((byte)blocks);
                icyBytes.Write(meta);
                icyBytes.Write(new byte[blocks * 16 - meta.Length]);
            }
            else icyBytes.WriteByte(0);
        }
    }
    using var source = new FragmentedStream(icyBytes.ToArray());
    using var timed = new CancellableReadStream(source, CancellationToken.None);
    var updates = new List<TrackInfo>();
    using var stream = new IcyAudioStream(timed, interval, block => updates.Add(TrackInfo.FromMetadata(block)!));
    var first = Mp3Frame.LoadFromStream(stream);
    var second = Mp3Frame.LoadFromStream(stream);
    Check(first.RawData.SequenceEqual(bytes[..417]), "ICY stripped from first MP3 frame");
    Check(second.RawData.SequenceEqual(bytes[417..]), "ICY stripped from second MP3 frame");
    Check(second.FileOffset == 417 && stream.Position == bytes.Length, "ICY audio-only position");
    Check(updates.Count == 1 && updates[0] == new TrackInfo("Björk", "Jóga"), "UTF8 metadata and zero-block retention");
    Check(Mp3Frame.LoadFromStream(stream) == null, "ICY clean EOF");
}
using (var source = new MemoryStream(new byte[] { 1, 2, 3, 4, 2, 42 }))
using (var stream = new IcyAudioStream(source, 4, _ => { }))
{
    try { stream.ReadExactly(new byte[8]); throw new Exception("Expected truncated ICY error"); }
    catch (EndOfStreamException) { }
}
using (var source = new MemoryStream(bytes))
using (var stream = new IcyAudioStream(source, 0, _ => throw new Exception("Unexpected metadata")))
{
    Check(Mp3Frame.LoadFromStream(stream).FrameLength == 417, "No-metadata stream passthrough");
}
Check(TrackInfo.FromMetadata(System.Text.Encoding.UTF8.GetBytes("StreamTitle='';")) == TrackInfo.Empty, "Explicit empty title clears metadata");
Check(TrackInfo.FromMetadata(System.Text.Encoding.UTF8.GetBytes("StreamUrl='something';")) == null, "Missing title does not clear metadata");
Check(TrackInfo.FromMetadata(System.Text.Encoding.Latin1.GetBytes("StreamTitle='Beyoncé - Song';"))?.Artist == "Beyoncé", "Legacy metadata encoding");
Check(TrackInfo.FromMetadata(System.Text.Encoding.UTF8.GetBytes("StreamTitle='DJ's evening show';"))?.Title == "DJ's evening show", "Unstructured title with apostrophe");

var health = new StreamHealthMeter();
Check(health.Update(1.2, true) == StreamHealth.Healthy, "healthy stream buffer");
Check(health.Update(0.6, true) == StreamHealth.Healthy, "health hysteresis avoids early warning flicker");
Check(health.Update(0.4, true) == StreamHealth.Low, "low stream buffer");
Check(health.Update(0.1, true) == StreamHealth.Starving, "starving stream buffer");
Check(health.Update(0.5, true) == StreamHealth.Low, "starving stream recovers through low state");
Check(health.Update(1.1, true) == StreamHealth.Healthy, "stream health fully recovers");
Check(health.Update(2, false) == StreamHealth.Inactive, "inactive stream health");
var capturedStatistics = new RadioStreamStatistics("MP3", 192000, 44100, 2, 1.5, 1000);
Check(Math.Abs(capturedStatistics.EstimatedBufferedSeconds(1500) - 1.0) < 0.001, "buffer estimate accounts for playback time");

// Build a complete Ogg page in memory. FragmentedStream limits every async read
// to three bytes, like a network stream that does not fill requested buffers.
var oggBody = new byte[] { 0x7f, (byte)'F', (byte)'L', (byte)'A', (byte)'C' };
var oggBytes = new byte[27 + 1 + oggBody.Length];
"OggS"u8.CopyTo(oggBytes);
oggBytes[4] = 0; // stream structure version
oggBytes[5] = 2; // beginning-of-stream flag
System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(oggBytes.AsSpan(14, 4), 1234);
System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(oggBytes.AsSpan(18, 4), 0);
oggBytes[26] = 1; // one lacing value follows
oggBytes[27] = (byte)oggBody.Length;
oggBody.CopyTo(oggBytes, 28);

using (var source = new FragmentedStream(oggBytes))
{
    var reader = new OggPageReader(source);
    var page = await reader.ReadAsync();
    if (page == null) throw new Exception("Failed: Ogg first page exists");
    Check(page.IsBeginningOfStream, "Ogg beginning-of-stream flag");
    Check(page.SerialNumber == 1234 && page.SequenceNumber == 0, "Ogg page identity");
    Check(page.LacingValues.SequenceEqual(new byte[] { 5 }), "Ogg lacing table");
    Check(page.Body.SequenceEqual(oggBody), "Ogg page body");
    Check(await reader.ReadAsync() == null, "Ogg clean EOF");
}

// A 260-byte packet is split as 255 bytes in one page and five bytes in the
// next. The packet reader must hide that page boundary from its caller.
var longPacket = Enumerable.Range(0, 260).Select(i => (byte)i).ToArray();
var firstPacketPage = CreateOggPage(
    headerType: 0x02,
    serialNumber: 42,
    sequenceNumber: 0,
    lacingValues: new byte[] { 255 },
    body: longPacket[..255]);
var secondPacketPage = CreateOggPage(
    headerType: 0x01 | 0x04,
    serialNumber: 42,
    sequenceNumber: 1,
    lacingValues: new byte[] { 5 },
    body: longPacket[255..]);

using (var source = new FragmentedStream(firstPacketPage.Concat(secondPacketPage).ToArray()))
{
    var reader = new OggPacketReader(new OggPageReader(source));
    var packet = await reader.ReadAsync();
    if (packet == null) throw new Exception("Failed: Ogg packet exists");
    Check(packet.Data.SequenceEqual(longPacket), "Ogg packet assembled across pages");
    Check(packet.SerialNumber == 42, "Ogg packet serial number");
    Check(packet.IsBeginningOfStream && packet.IsEndOfStream, "Ogg packet boundary flags");
    Check(await reader.ReadAsync() == null, "Ogg packet clean EOF");
}

// Reproduce the 51-byte identification packet seen in the station sample.
var identificationBytes = new byte[51];
identificationBytes[0] = 0x7f;
"FLAC"u8.CopyTo(identificationBytes.AsSpan(1));
identificationBytes[5] = 1; // Ogg-FLAC mapping major version
identificationBytes[6] = 0; // mapping minor version
System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(identificationBytes.AsSpan(7, 2), 1);
"fLaC"u8.CopyTo(identificationBytes.AsSpan(9));
identificationBytes[13] = 0; // STREAMINFO metadata type
identificationBytes[16] = 34; // 24-bit metadata length: 00 00 22
System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(identificationBytes.AsSpan(17, 2), 4096);
System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(identificationBytes.AsSpan(19, 2), 4096);
var packedAudioInfo = ((ulong)44100 << 44) | ((ulong)1 << 41) | ((ulong)15 << 36);
System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(identificationBytes.AsSpan(27, 8), packedAudioInfo);

var identification = OggFlacIdentification.Parse(
    new OggPacket(42, identificationBytes, IsBeginningOfStream: true, IsEndOfStream: false));
Check(identification.MajorVersion == 1 && identification.MinorVersion == 0, "Ogg-FLAC mapping version");
Check(identification.HeaderPacketCount == 1, "Ogg-FLAC header packet count");
Check(identification.StreamInfo.SampleRate == 44100, "FLAC sample rate");
Check(identification.StreamInfo.Channels == 2, "FLAC channel count");
Check(identification.StreamInfo.BitsPerSample == 16, "FLAC bits per sample");

var commentText = System.Text.Encoding.UTF8.GetBytes("TITLE=Fragma - You Are Alive");
var vendor = System.Text.Encoding.UTF8.GetBytes("test encoder");
var commentPayloadLength = 4 + vendor.Length + 4 + 4 + commentText.Length;
var commentPacket = new byte[4 + commentPayloadLength];
commentPacket[0] = 0x84; // final metadata block, type 4 (Vorbis comments)
commentPacket[3] = checked((byte)commentPayloadLength);
var commentOffset = 4;
System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(commentPacket.AsSpan(commentOffset, 4), (uint)vendor.Length);
commentOffset += 4;
vendor.CopyTo(commentPacket, commentOffset);
commentOffset += vendor.Length;
System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(commentPacket.AsSpan(commentOffset, 4), 1);
commentOffset += 4;
System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(commentPacket.AsSpan(commentOffset, 4), (uint)commentText.Length);
commentOffset += 4;
commentText.CopyTo(commentPacket, commentOffset);
var comments = FlacVorbisComments.Parse(commentPacket);
Check(comments.Vendor == "test encoder", "FLAC comment vendor");
Check(comments.Track == new TrackInfo("Fragma", "You Are Alive"), "FLAC title comment");

// The bridge removes only the nine-byte Ogg-FLAC mapping prefix, then joins
// the native FLAC metadata and audio packets into one forward-only stream.
var audioPacket = new byte[] { 0xff, 0xf8, 1, 2, 3 };
// This synthetic identification says one header packet follows, so include its
// comment block before audio just like the real stream.
var identificationPage = CreateOggPage(
    headerType: 0x02,
    serialNumber: 77,
    sequenceNumber: 0,
    lacingValues: new byte[] { (byte)identificationBytes.Length },
    body: identificationBytes);
var audioPage = CreateOggPage(
    headerType: 0,
    serialNumber: 77,
    sequenceNumber: 1,
    lacingValues: new byte[] { (byte)commentPacket.Length },
    body: commentPacket);
var finalAudioPage = CreateOggPage(
    headerType: 0x04,
    serialNumber: 77,
    sequenceNumber: 2,
    lacingValues: new byte[] { (byte)audioPacket.Length },
    body: audioPacket);
FlacVorbisComments? observedComments = null;
using (var source = new FragmentedStream(identificationPage.Concat(audioPage).Concat(finalAudioPage).ToArray()))
using (var nativeFlac = await OggFlacStream.CreateAsync(source, value => observedComments = value))
using (var output = new MemoryStream())
{
    nativeFlac.CopyTo(output);
    Check(
        output.ToArray().SequenceEqual(identificationBytes[9..].Concat(commentPacket).Concat(audioPacket)),
        "Ogg-FLAC to native FLAC stream bridge");
    Check(observedComments?.Track == new TrackInfo("Fragma", "You Are Alive"), "Ogg-FLAC metadata callback");
}

// Radio servers can publish new comments by beginning another logical Ogg
// stream. The next identification packet must remain available to a fresh FLAC
// decoder instead of being consumed by the preceding stream.
var unterminatedFirstAudioPage = CreateOggPage(
    headerType: 0,
    serialNumber: 77,
    sequenceNumber: 2,
    lacingValues: new byte[] { (byte)audioPacket.Length },
    body: audioPacket);
var secondIdentificationPage = CreateOggPage(
    headerType: 0x02,
    serialNumber: 88,
    sequenceNumber: 0,
    lacingValues: new byte[] { (byte)identificationBytes.Length },
    body: identificationBytes);
var secondCommentPage = CreateOggPage(
    headerType: 0,
    serialNumber: 88,
    sequenceNumber: 1,
    lacingValues: new byte[] { (byte)commentPacket.Length },
    body: commentPacket);
var secondFinalPage = CreateOggPage(
    headerType: 0x04,
    serialNumber: 88,
    sequenceNumber: 2,
    lacingValues: new byte[] { (byte)audioPacket.Length },
    body: audioPacket);
using (var source = new FragmentedStream(
           identificationPage.Concat(audioPage).Concat(unterminatedFirstAudioPage)
               .Concat(secondIdentificationPage).Concat(secondCommentPage).Concat(secondFinalPage).ToArray()))
{
    var packets = new OggPacketReader(new OggPageReader(source));
    using var firstChain = await OggFlacStream.CreateNextAsync(packets);
    if (firstChain == null) throw new Exception("Failed: first chained Ogg-FLAC stream exists");
    using var firstOutput = new MemoryStream();
    firstChain.CopyTo(firstOutput);
    using var secondChain = await OggFlacStream.CreateNextAsync(packets);
    if (secondChain == null) throw new Exception("Failed: second chained Ogg-FLAC stream exists");
    using var secondOutput = new MemoryStream();
    secondChain.CopyTo(secondOutput);
    Check(firstChain.Identification.StreamInfo.SampleRate == 44100, "first chained FLAC identification");
    Check(secondChain.Identification.StreamInfo.SampleRate == 44100, "second chained FLAC identification");
    Check(await OggFlacStream.CreateNextAsync(packets) == null, "chained Ogg-FLAC clean EOF");
}

using (var source = new FragmentedStream(identificationPage.Concat(audioPage).ToArray()))
{
    var probe = await StationProbe.DetectAsync(source);
    Check(probe.StreamType == StationStreamType.OggFlac, "probe detects Ogg-FLAC");
}
using (var source = new FragmentedStream(bytes))
{
    var probe = await StationProbe.DetectAsync(source, "Test MP3");
    Check(probe.StreamType == StationStreamType.Mp3 && probe.StationName == "Test MP3", "probe detects MP3 and retains station name");
}

// Pass a captured .oga file as the first command-line argument to exercise the
// complete Ogg demuxer -> native FLAC bridge -> managed decoder pipeline.
if (args.Length > 0)
{
    await using var source = File.OpenRead(args[0]);
    FlacVorbisComments? realComments = null;
    using var nativeFlac = await OggFlacStream.CreateAsync(source, value => realComments = value);
    using var decoder = new FlacDecoder(
        nativeFlac,
        new FlacDecoder.Options { ValidateOutputHash = false });
    Check(decoder.SampleRate == 44100, "real FLAC sample rate");
    Check(decoder.ChannelCount == 2, "real FLAC channels");

    var decodedFrames = 0;
    var decodedBytes = 0;
    while (decodedFrames < 10 && decoder.DecodeFrame())
    {
        decodedFrames++;
        decodedBytes += decoder.BufferByteCount;
    }

    Check(decodedFrames == 10 && decodedBytes > 0, "real FLAC frame decoding");
    Check(realComments?.Track is { Title.Length: > 0 }, "real FLAC title metadata");
    Console.WriteLine($"Passed: decoded {decodedFrames} real FLAC frames ({decodedBytes} PCM bytes).");
}

var policy = new PlaybackPolicy();
Check(policy.Update(true, false, false, true, false) == PlaybackAction.None, "Mount-only login is quiet");
Check(policy.Update(true, true, false, true, false) == PlaybackAction.Play, "Mount autoplay");
Check(policy.Update(true, true, false, true, false) == PlaybackAction.None, "Manual stop is respected on mount");
Check(policy.Update(true, false, false, true, true) == PlaybackAction.Stop, "Dismount stops mount mode");
Check(policy.Update(true, false, true, true, false) == PlaybackAction.Play, "Enable full-time autoplay");
Check(policy.Update(true, true, true, true, true) == PlaybackAction.None, "Mount does not restart full-time stream");
Check(policy.Update(true, false, true, true, true) == PlaybackAction.None, "Full-time dismount continues");
Check(policy.Update(true, false, true, true, false) == PlaybackAction.None, "Full-time manual stop/failure stays stopped");
Check(policy.Update(false, false, true, true, true) == PlaybackAction.Stop, "Logout always stops");
Check(policy.Update(true, false, true, true, false) == PlaybackAction.Play, "Full-time login autoplay");
Check(policy.Update(true, false, false, true, true) == PlaybackAction.Stop, "Switch back to mount-only while unmounted");
Check(DismountTransition.Gain(0) == 1, "Dismount fade starts at full gain");
Check(Math.Abs(DismountTransition.Gain(400) - 0.5f) < 0.001f, "Dismount fade midpoint");
Check(DismountTransition.Gain(800) == 0 && DismountTransition.Gain(15000) == 0, "Dismount stays silent after fade");
var previousGain = 1f;
for (var elapsed = 0; elapsed <= 1000; elapsed += 10)
{
    var currentGain = DismountTransition.Gain(elapsed);
    Check(currentGain <= previousGain && currentGain >= 0, "Fade is monotonic and bounded");
    previousGain = currentGain;
}
var transitionMuted = false;
var transitionMusic = new GameMusicController(() => transitionMuted, value => transitionMuted = value, ex => throw ex);
transitionMusic.Update(true);
transitionMusic.Update(DismountTransition.HoldMusic(800));
Check(transitionMuted, "Game music stays muted after radio fade completes");
transitionMusic.Update(DismountTransition.HoldMusic(2999));
Check(transitionMuted, "Mount music transition is held muted");
transitionMusic.Update(DismountTransition.HoldMusic(3000));
Check(!transitionMuted, "Game music restored after transition hold");
transitionMusic.Update(true);
transitionMusic.Update(false);
Check(!transitionMuted, "Manual stop bypasses dismount hold");
Console.WriteLine("Passed: dismount fade envelope and delayed game music restoration.");
Console.WriteLine("Passed: Ogg page parsing and cross-page packet assembly.");
Console.WriteLine("Passed: Ogg-FLAC identification and STREAMINFO parsing.");
Console.WriteLine("Passed: Ogg-FLAC to native FLAC stream bridge.");
Console.WriteLine("Passed: station probe detects MP3 and Ogg-FLAC.");
Console.WriteLine("Passed: ICY framing and metadata, playback policy, fragmented MP3 frames, position, EOF, truncation, and stalled-read cancellation, music ownership/restoration, user overrides, and restoration retry.");

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception($"Failed: {name}");
}

static byte[] CreateOggPage(
    byte headerType,
    uint serialNumber,
    uint sequenceNumber,
    byte[] lacingValues,
    byte[] body)
{
    var bytes = new byte[27 + lacingValues.Length + body.Length];
    "OggS"u8.CopyTo(bytes);
    bytes[4] = 0;
    bytes[5] = headerType;
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(14, 4), serialNumber);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(18, 4), sequenceNumber);
    bytes[26] = checked((byte)lacingValues.Length);
    lacingValues.CopyTo(bytes, 27);
    body.CopyTo(bytes, 27 + lacingValues.Length);
    return bytes;
}

sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
{
    public override bool CanSeek => false;
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => base.ReadAsync(buffer[..Math.Min(3, buffer.Length)], cancellationToken);
}

sealed class StalledStream : MemoryStream
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Entered.TrySetResult();
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return 0;
    }
}
