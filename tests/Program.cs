using ChocoboRadio;
using NAudio.Wave;

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

Console.WriteLine("Passed: ICY framing and metadata, fragmented MP3 frames, position, EOF, truncation, and stalled-read cancellation, music ownership/restoration, user overrides, and restoration retry.");

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception($"Failed: {name}");
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
