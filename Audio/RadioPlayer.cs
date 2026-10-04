using System;

namespace ChocoboRadio;

// The UI/framework thread owns the selected session; each session owns its audio resources.
internal sealed class RadioPlayer : IDisposable
{
    private IRadioSession? session;
    private string status = "Stopped";
    private float gain;
    private long remountDeadline;
    public bool IsSuspended => session is { Suspended: true, Finished: false };
    public string Status => IsSuspended && session!.IsFading ? "Fading out after dismount…" : IsSuspended ? "Silent — keeping the stream ready for a resume (up to 60 seconds)." : session?.Status ?? status;
    public TrackInfo Track => session?.Track ?? TrackInfo.Empty;
    public string PlayingStation => session?.StationName ?? "";
    public RadioStreamStatistics? Statistics => session?.Statistics;
    public bool IsPlaying => session is { Started: true, Finished: false, Suspended: false };
    // Hold the mute briefly after dismount so the game's outgoing mount theme stays hidden.
    public bool SuppressGameMusic => IsPlaying || (IsSuspended && session!.HoldGameMusic);
    public bool IsRunning => session is { Finished: false };
    // A failed session still represents an attempt to listen. Station changes
    // should continue playback from the next selection until Stop clears it.
    public bool HasPlaybackIntent => session != null;

    public void SetVolume(float value)
    {
        gain = float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
        session?.SetGain(gain);
    }

    public void Play(Configuration config)
    {
        if (!config.PoweredOn) return;
        Poll();
        if (IsSuspended && config.SelectedStation >= 0 && config.SelectedStation < config.Stations.Count &&
            Uri.TryCreate(config.Stations[config.SelectedStation].Url, UriKind.Absolute, out var requested) &&
            session!.Source == requested)
        {
            session.Resume();
            return;
        }
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
            status = "Enter a direct HTTP or HTTPS audio stream URL.";
            return;
        }
        session = station.StreamType switch
        {
            StationStreamType.OggFlac => new OggFlacRadioSession(uri, station.Name, gain),
            StationStreamType.Mp3 => new Mp3RadioSession(uri, station.Name, gain),
            _ => null,
        };
        if (session == null) status = "Inspect this station or select its stream format first.";
        else if (!string.IsNullOrWhiteSpace(station.DirectoryId)) _ = RadioBrowserClient.RecordClickAsync(station.DirectoryId);
    }

    public void SuspendForRemount()
    {
        if (session is not { Finished: false } || IsSuspended) return;
        session.Suspend(fade: true);
        remountDeadline = Environment.TickCount64 + 60_000;
    }

    public void Pause()
    {
        if (session is not { Finished: false }) return;
        // Do not extend the expiry on repeated power-off updates.
        if (!IsSuspended) remountDeadline = Environment.TickCount64 + 60_000;
        session.Suspend(fade: false);
    }

    public void Poll()
    {
        if (session is { Suspended: true } && (session.Finished || Environment.TickCount64 >= remountDeadline)) Stop();
    }

    public void Stop()
    {
        session?.Dispose();
        session = null;
        status = "Stopped";
    }

    public void Dispose() => Stop();
}
