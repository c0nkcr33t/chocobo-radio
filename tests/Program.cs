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
Console.WriteLine("Passed: fragmented MP3 frames, position, EOF, truncation, and stalled-read cancellation, music ownership/restoration, user overrides, and restoration retry.");

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
