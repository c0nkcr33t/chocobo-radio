using System;

namespace ChocoboRadio;

internal sealed record RadioStreamStatistics(
    string Codec,
    int BitRate,
    int SampleRate,
    int Channels,
    double BufferedSeconds,
    long CapturedAt)
{
    public static RadioStreamStatistics Connecting(string codec)
        => new(codec, 0, 0, 0, 0, Environment.TickCount64);

    // Audio continues to leave the buffer while a network read is blocked, so
    // account for the time since the audio thread last published a snapshot.
    public double EstimatedBufferedSeconds(long now)
        => Math.Max(0, BufferedSeconds - Math.Max(0, now - CapturedAt) / 1000.0);
}

internal enum StreamHealth
{
    Inactive,
    Healthy,
    Low,
    Starving,
}

internal sealed class StreamHealthMeter
{
    public StreamHealth Current { get; private set; } = StreamHealth.Inactive;

    public StreamHealth Update(double bufferedSeconds, bool active)
    {
        if (!active) return Current = StreamHealth.Inactive;

        Current = Current switch
        {
            StreamHealth.Healthy when bufferedSeconds < 0.5 => StreamHealth.Low,
            StreamHealth.Healthy => StreamHealth.Healthy,
            StreamHealth.Low when bufferedSeconds < 0.15 => StreamHealth.Starving,
            StreamHealth.Low when bufferedSeconds >= 1.0 => StreamHealth.Healthy,
            StreamHealth.Low => StreamHealth.Low,
            StreamHealth.Starving when bufferedSeconds >= 0.4 => StreamHealth.Low,
            StreamHealth.Starving => StreamHealth.Starving,
            _ when bufferedSeconds < 0.25 => StreamHealth.Starving,
            _ when bufferedSeconds < 0.75 => StreamHealth.Low,
            _ => StreamHealth.Healthy,
        };
        return Current;
    }
}
