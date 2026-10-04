using System;
using System.Collections.Generic;

namespace ChocoboRadio;

internal sealed record RadioStreamStatistics(
    string Codec,
    int BitRate,
    int SampleRate,
    int Channels,
    double BufferedSeconds,
    long CapturedAt,
    int BitsPerSample = 0)
{
    public static RadioStreamStatistics Connecting(string codec)
        => new(codec, 0, 0, 0, 0, Environment.TickCount64);

    // Audio continues to leave the buffer while a network read is blocked, so
    // account for the time since the audio thread last published a snapshot.
    public double EstimatedBufferedSeconds(long now)
        => Math.Max(0, BufferedSeconds - Math.Max(0, now - CapturedAt) / 1000.0);
}

// FLAC is variable-rate, and Ogg readers consume whole pages at a time. A
// rolling window turns those bursty byte reads into a useful stream bitrate.
internal sealed class RollingBitRateMeter(
    double windowSeconds = 10,
    double minimumSeconds = 5,
    double refreshSeconds = 5)
{
    private readonly Queue<Measurement> measurements = new();
    private double decodedSeconds;
    private double lastRefreshSeconds;
    private int currentBitRate;

    public int Update(long compressedBytes, int decodedSamples, int sampleRate)
    {
        if (compressedBytes < 0) throw new ArgumentOutOfRangeException(nameof(compressedBytes));
        if (decodedSamples < 0) throw new ArgumentOutOfRangeException(nameof(decodedSamples));
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));

        decodedSeconds += decodedSamples / (double)sampleRate;
        measurements.Enqueue(new Measurement(compressedBytes, decodedSeconds));

        var cutoff = decodedSeconds - windowSeconds;
        while (measurements.Count > 1 && measurements.Peek().DecodedSeconds < cutoff)
            measurements.Dequeue();

        var first = measurements.Peek();
        var elapsed = decodedSeconds - first.DecodedSeconds;
        var bytes = compressedBytes - first.CompressedBytes;
        if (elapsed < minimumSeconds || bytes <= 0) return currentBitRate;
        if (currentBitRate > 0 && decodedSeconds - lastRefreshSeconds < refreshSeconds)
            return currentBitRate;

        currentBitRate = checked((int)Math.Round(bytes * 8 / elapsed));
        lastRefreshSeconds = decodedSeconds;
        return currentBitRate;
    }

    private readonly record struct Measurement(long CompressedBytes, double DecodedSeconds);
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
