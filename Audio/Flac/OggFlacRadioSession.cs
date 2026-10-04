using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using SimpleFlac;

namespace ChocoboRadio;


internal sealed class OggFlacRadioSession : IRadioSession
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
    private volatile string stationName;
    private volatile RadioStreamStatistics statistics = RadioStreamStatistics.Connecting("FLAC");
    private long suspensionStarted;

    // Read-only properties;
    public Uri Source { get; }
    public string StationName => stationName;
    public TrackInfo Track => track;
    public string Status => status;
    public RadioStreamStatistics Statistics => statistics;
    public bool Started => started;
    public bool Finished => finished;
    public bool Suspended => suspended;
    public bool IsFading => fadeOnSuspend && Environment.TickCount64 - Interlocked.Read(ref suspensionStarted) < DismountTransition.FadeMilliseconds;
    public bool HoldGameMusic => fadeOnSuspend && DismountTransition.HoldMusic(Environment.TickCount64 - Interlocked.Read(ref suspensionStarted));

    public OggFlacRadioSession(Uri uri, string name, float initialGain)
    {
        gain = initialGain;
        stationName = name;
        Source = uri;

        _ = Task.Run(() => Run(uri, name));
    }

    public void Stop()
    {
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
            if (type != null && type != "audio/ogg" && type != "application/ogg" &&
                type != "audio/flac" && type != "audio/x-flac" && type != "application/octet-stream")
                throw new InvalidDataException("This station is not sending an Ogg-FLAC stream.");

            if (response.Headers.TryGetValues("icy-name", out var stationNames))
            {
                var found = CleanText(stationNames.FirstOrDefault());
                if (found.Length > 0 && (string.IsNullOrWhiteSpace(stationName) || stationName == "New station"))
                    stationName = found;
            }

            var interval = 0;
            if (response.Headers.TryGetValues("icy-metaint", out var intervals) &&
                (!int.TryParse(intervals.SingleOrDefault(), out interval) || interval <= 0))
                throw new InvalidDataException("Station sent an invalid ICY metadata interval.");

            using var network = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var buffered = new BufferedStream(network, 16384);
            using var timed = new CancellableReadStream(buffered, token);
            using var input = new IcyAudioStream(timed, interval, block =>
            {
                var icyTrack = TrackInfo.FromMetadata(block);
                if (icyTrack != null) track = icyTrack;
            });
            void ApplyComments(FlacVorbisComments comments)
            {
                if (comments.Track != null) track = comments.Track;
                var found = CleanText(comments.StationName);
                if (found.Length > 0 && (string.IsNullOrWhiteSpace(stationName) || stationName == "New station"))
                    stationName = found;
            }

            var packets = new OggPacketReader(new OggPageReader(input));
            var bitRate = new RollingBitRateMeter();
            BufferedWaveProvider? outputBuffer = null;
            WaveOutEvent? output = null;
            WaveFormat? format = null;
            var outputStarted = false;
            try
            {
                while (true)
                {
                    using var nativeFlac = await OggFlacStream.CreateNextAsync(packets, ApplyComments, token)
                        .ConfigureAwait(false);
                    if (nativeFlac == null) break;
                    using var decoder = new FlacDecoder(
                        nativeFlac,
                        new FlacDecoder.Options { ValidateOutputHash = false });

                    if (decoder.BitsPerSample is not (8 or 16 or 24 or 32))
                        throw new InvalidDataException($"Unsupported FLAC bit depth: {decoder.BitsPerSample}.");
                    var chainFormat = new WaveFormat(decoder.SampleRate, decoder.BitsPerSample, decoder.ChannelCount);
                    if (format == null)
                    {
                        format = chainFormat;
                        outputBuffer = new BufferedWaveProvider(format)
                        {
                            BufferDuration = TimeSpan.FromSeconds(5),
                            ReadFully = true,
                        };
                        output = new WaveOutEvent { DesiredLatency = 150 };
                        output.Init(new OggFlacRadioGainProvider(outputBuffer.ToSampleProvider(), this).ToWaveProvider16());
                    }
                    else if (chainFormat.SampleRate != format.SampleRate ||
                             chainFormat.Channels != format.Channels ||
                             chainFormat.BitsPerSample != format.BitsPerSample)
                        throw new InvalidDataException("Station changed FLAC audio format. Press Play / Reconnect.");

                    while (decoder.DecodeFrame())
                    {
                        token.ThrowIfCancellationRequested();
                        while (outputBuffer!.BufferedDuration.TotalSeconds > 2)
                        {
                            token.ThrowIfCancellationRequested();
                            if (outputStarted && output!.PlaybackState != PlaybackState.Playing)
                                throw new IOException("Audio output stopped. Check your output device and reconnect.");
                            token.WaitHandle.WaitOne(25);
                        }

                        outputBuffer.AddSamples(decoder.BufferBytes, 0, decoder.BufferByteCount);
                        if (!outputStarted && outputBuffer.BufferedDuration.TotalSeconds >= 0.5)
                        {
                            output!.Play();
                            outputStarted = true;
                            started = true;
                            status = $"Playing: {stationName}";
                        }
                        PublishStatistics(
                            decoder,
                            outputBuffer,
                            bitRate.Update(input.Position, decoder.BufferSampleCount, decoder.SampleRate));
                    }
                }
            }
            finally { output?.Dispose(); }

            status = "Stream ended. Press Play / Reconnect.";
        }
        catch (OperationCanceledException)
        {
            status = stopped
                ? "Stopped"
                : "Station timed out. Press Play / Reconnect.";
        }
        catch (Exception ex)
        {
            if (!stopped) Plugin.Log.Error(ex, "Chocobo Radio: Ogg-FLAC playback failed for {Uri}", uri);
            status = stopped
                ? "Stopped"
                : $"Radio error: {ex.Message}";
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


    private void PublishStatistics(FlacDecoder decoder, BufferedWaveProvider buffer, int bitRate)
    {
        statistics = new RadioStreamStatistics(
            "FLAC",
            bitRate,
            decoder.SampleRate,
            decoder.ChannelCount,
            buffer.BufferedDuration.TotalSeconds,
            Environment.TickCount64,
            decoder.BitsPerSample
        );
    }

    public void Dispose()
    {
        Stop();
    }

    private static string CleanText(string? value)
        => new string((value ?? "").Where(c => !char.IsControl(c)).Take(255).ToArray()).Trim();

    private sealed class OggFlacRadioGainProvider(ISampleProvider source, OggFlacRadioSession owner) : ISampleProvider
    {
        public WaveFormat WaveFormat => source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            var read = source.Read(buffer, offset, count);
            var gain = owner.stopped ? 0 : owner.gain;
            var suspended = owner.Suspended;
            var elapsed = Environment.TickCount64 - Interlocked.Read(ref owner.suspensionStarted);
            for (var i = 0; i < read; i++)
            {
                var fade = suspended
                    ? (owner.fadeOnSuspend
                        ? DismountTransition.Gain(elapsed + (i / WaveFormat.Channels) * 1000.0 / WaveFormat.SampleRate)
                        : 0)
                    : 1;
                buffer[offset + i] *= gain * fade;
            }
            return read;
        }
    }
}
