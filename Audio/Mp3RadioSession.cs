
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace ChocoboRadio;


internal sealed class Mp3RadioSession : IRadioSession
{
    // Private fields
    private readonly object gate = new();
    private readonly CancellationTokenSource cancellation = new();
    private bool disposed;
    private volatile TrackInfo track = TrackInfo.Empty;
    private volatile string status = "Connecting...";
    private volatile bool started;
    private volatile bool finished;
    private volatile bool stopped;
    private volatile bool suspended;
    private volatile float gain;
    private volatile bool fadeOnSuspend;
    private volatile RadioStreamStatistics statistics = RadioStreamStatistics.Connecting("MP3");
    private long suspensionStarted;

    // Read-only properties;
    public Uri Source { get; }
    public string StationName { get; }
    public TrackInfo Track => track;
    public string Status => status;
    public RadioStreamStatistics Statistics => statistics;
    public bool Started => started;
    public bool Finished => finished;
    public bool Suspended => suspended;
    public bool IsFading => fadeOnSuspend && Environment.TickCount64 - Interlocked.Read(ref suspensionStarted) < DismountTransition.FadeMilliseconds;
    public bool HoldGameMusic => fadeOnSuspend && DismountTransition.HoldMusic(Environment.TickCount64 - Interlocked.Read(ref suspensionStarted));

    public Mp3RadioSession(Uri uri, string name, float initialGain)
    {
        gain = initialGain;
        StationName = name;
        Source = uri;

        _ = Task.Run(() => Run(uri, name));
    }

    public void Stop()
    {
        // Silence already-buffered samples immediately, before cancelling network reads.
        stopped = true;
        lock (gate)
        {
            if (!disposed) cancellation.Cancel();
        }
    }

    public void Suspend(bool fade)
    {
        fadeOnSuspend = fade;

        if (fade)
        {
            Interlocked.Exchange(ref suspensionStarted, Environment.TickCount64);
        }

        suspended = true;
    }

    public void Resume()
    {
        fadeOnSuspend = false;
        suspended = false;
    }

    public void SetGain(float value)
    {
        gain = float.IsFinite(value)
            ? Math.Clamp(value, 0, 1)
            : 0;
    }

    private async Task Run(Uri uri, string name)
    {
        var token = cancellation.Token;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ChocoboRadio/0.3");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Icy-MetaData", "1");
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var type = response.Content.Headers.ContentType?.MediaType;
            if (type != null && type != "audio/mpeg" && type != "audio/mp3" && type != "application/octet-stream")
                throw new InvalidDataException("This version needs a direct MP3 stream (not AAC, HLS, or a web player).");
            var interval = 0;
            if (response.Headers.TryGetValues("icy-metaint", out var intervals) &&
                (!int.TryParse(intervals.SingleOrDefault(), out interval) || interval <= 0))
                throw new InvalidDataException("Station sent an invalid ICY metadata interval.");
            using var network = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var buffered = new BufferedStream(network, 16384);
            using var timed = new CancellableReadStream(buffered, token);
            using var input = new IcyAudioStream(timed, interval, block =>
            {
                var trackMetadata = TrackInfo.FromMetadata(block);
                if (trackMetadata != null) track = trackMetadata;
            });
            var frame = Mp3Frame.LoadFromStream(input) ?? throw new InvalidDataException("The stream contained no MP3 audio.");
            var channels = frame.ChannelMode == ChannelMode.Mono ? 1 : 2;
            var format = new Mp3WaveFormat(frame.SampleRate, channels, frame.FrameLength, frame.BitRate);
            using var decoder = new NLayer.NAudioSupport.Mp3FrameDecompressor(format);
            var buffer = new BufferedWaveProvider(decoder.OutputFormat)
            {
                BufferDuration = TimeSpan.FromSeconds(5),
                ReadFully = true,
            };
            using var output = new WaveOutEvent { DesiredLatency = 150 };
            var samples = buffer.ToSampleProvider();
            output.Init(new Mp3RadioGainProvider(samples, this).ToWaveProvider16());
            var pcm = new byte[65536];
            var outputStarted = false;
            do
            {
                token.ThrowIfCancellationRequested();
                if (frame.SampleRate != format.SampleRate || (frame.ChannelMode == ChannelMode.Mono ? 1 : 2) != channels)
                    throw new InvalidDataException("Station changed audio format. Press Play / Reconnect.");
                while (buffer.BufferedDuration.TotalSeconds > 2)
                {
                    PublishStatistics(frame, channels, buffer);
                    token.ThrowIfCancellationRequested();
                    if (outputStarted && output.PlaybackState != PlaybackState.Playing)
                        throw new IOException("Audio output stopped. Check your output device and reconnect.");
                    token.WaitHandle.WaitOne(25);
                }
                var count = decoder.DecompressFrame(frame, pcm, 0);
                buffer.AddSamples(pcm, 0, count);
                PublishStatistics(frame, channels, buffer);
                if (!outputStarted && buffer.BufferedDuration.TotalSeconds >= 0.5)
                {
                    token.ThrowIfCancellationRequested();
                    output.Play();
                    outputStarted = true;
                    started = true;
                    status = $"Playing: {name}";
                }
                frame = Mp3Frame.LoadFromStream(input);
            } while (frame != null);
            status = "Stream ended. Press Play / Reconnect.";
        }
        catch (OperationCanceledException)
        {
            status = stopped ? "Stopped" : "Station timed out. Press Play / Reconnect.";
        }
        catch (Exception ex)
        {
            status = stopped ? "Stopped" : $"Radio error: {ex.Message}";
        }
        finally
        {
            finished = true;
            lock (gate)
            {
                disposed = true;
                cancellation.Dispose();
            }
        }
    }

    private void PublishStatistics(Mp3Frame frame, int channels, BufferedWaveProvider buffer)
    {
        statistics = new RadioStreamStatistics(
            "MP3",
            frame.BitRate,
            frame.SampleRate,
            channels,
            buffer.BufferedDuration.TotalSeconds,
            Environment.TickCount64);
    }

    public void Dispose()
    {
        Stop();
    }

    private sealed class Mp3RadioGainProvider(ISampleProvider source, Mp3RadioSession owner) : ISampleProvider
    {
        public WaveFormat WaveFormat => source.WaveFormat;
        public int Read(float[] buffer, int offset, int count)
        {
            var read = source.Read(buffer, offset, count);
            var gain = owner.stopped ? 0: owner.gain;
            var suspended = owner.Suspended;
            var elapsed = Environment.TickCount64 - Interlocked.Read(ref owner.suspensionStarted);
            for (var i = 0; i < read; i++)
            {
                // Apply the envelope per audio frame, independent of game frame rate.
                var fade = suspended
                    ? (owner.fadeOnSuspend ? DismountTransition.Gain(elapsed + (i / WaveFormat.Channels) * 1000.0 / WaveFormat.SampleRate) : 0)
                    : 1;
                buffer[offset + i] *= gain * fade;
            }
            return read;
        }
    }
}
