using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace ChocoboRadio;

// The UI/framework thread owns the selected session; each session owns its audio resources.
internal sealed class RadioPlayer : IDisposable
{
    private Session? session;
    private string status = "Stopped";
    private float gain;
    public string Status => session?.Status ?? status;
    public TrackInfo Track => session?.Track ?? TrackInfo.Empty;
    public string PlayingStation => session?.StationName ?? "";
    public bool IsPlaying => session is { Started: true, Finished: false };
    public bool IsRunning => session is { Finished: false };

    public void SetVolume(float value)
    {
        gain = float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
        if (session != null) session.Gain = gain;
    }

    public void Play(Configuration config)
    {
        Stop();
        if (config.SelectedStation < 0 || config.SelectedStation >= config.Stations.Count)
        {
            status = "Add and select a station first.";
            return;
        }
        var station = config.Stations[config.SelectedStation];
        if (!Uri.TryCreate(station.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && uri.Scheme != "http"))
        {
            status = "Enter a direct HTTP or HTTPS MP3 stream URL.";
            return;
        }
        session = new Session(uri, station.Name, gain);
    }

    public void Stop()
    {
        session?.Stop();
        session = null;
        status = "Stopped";
    }

    public void Dispose() => Stop();

    private sealed class Session
    {
        private readonly object gate = new();
        private readonly CancellationTokenSource cancellation = new();
        private bool disposed;
        private volatile bool stopped;
        public readonly string StationName;
        public volatile TrackInfo Track = TrackInfo.Empty;
        public volatile bool Finished;
        public volatile bool Started;
        public volatile float Gain;
        public volatile string Status = "Connecting…";

        public Session(Uri uri, string name, float gain)
        {
            Gain = gain;
            StationName = name;
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
                    var track = TrackInfo.FromMetadata(block);
                    if (track != null) Track = track;
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
                output.Init(new GainProvider(samples, this).ToWaveProvider16());
                var pcm = new byte[65536];
                var started = false;
                do
                {
                    token.ThrowIfCancellationRequested();
                    if (frame.SampleRate != format.SampleRate || (frame.ChannelMode == ChannelMode.Mono ? 1 : 2) != channels)
                        throw new InvalidDataException("Station changed audio format. Press Play / Reconnect.");
                    while (buffer.BufferedDuration.TotalSeconds > 2)
                    {
                        token.ThrowIfCancellationRequested();
                        if (started && output.PlaybackState != PlaybackState.Playing)
                            throw new IOException("Audio output stopped. Check your output device and reconnect.");
                        token.WaitHandle.WaitOne(25);
                    }
                    var count = decoder.DecompressFrame(frame, pcm, 0);
                    buffer.AddSamples(pcm, 0, count);
                    if (!started && buffer.BufferedDuration.TotalSeconds >= 0.5)
                    {
                        token.ThrowIfCancellationRequested();
                        output.Play();
                        started = true;
                        Started = true;
                        Status = $"Playing: {name}";
                    }
                    frame = Mp3Frame.LoadFromStream(input);
                } while (frame != null);
                Status = "Stream ended. Press Play / Reconnect.";
            }
            catch (OperationCanceledException)
            {
                Status = stopped ? "Stopped" : "Station timed out. Press Play / Reconnect.";
            }
            catch (Exception ex)
            {
                Status = stopped ? "Stopped" : $"Radio error: {ex.Message}";
            }
            finally
            {
                Finished = true;
                lock (gate)
                {
                    disposed = true;
                    cancellation.Dispose();
                }
            }
        }

        private sealed class GainProvider(ISampleProvider source, Session owner) : ISampleProvider
        {
            public WaveFormat WaveFormat => source.WaveFormat;
            public int Read(float[] buffer, int offset, int count)
            {
                var read = source.Read(buffer, offset, count);
                var gain = owner.stopped ? 0 : owner.Gain;
                for (var i = offset; i < offset + read; i++) buffer[i] *= gain;
                return read;
            }
        }
    }
}
