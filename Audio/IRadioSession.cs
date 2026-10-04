using System;

namespace ChocoboRadio;

internal interface IRadioSession : IDisposable
{
    Uri Source { get; }
    string StationName { get; }
    TrackInfo Track { get; }
    string Status { get; }
    RadioStreamStatistics? Statistics { get; }

    bool Started { get; }
    bool Finished { get; }
    bool Suspended { get; }
    bool IsFading { get; }
    bool HoldGameMusic { get; }

    void SetGain(float gain);
    void Suspend(bool fade);
    void Resume();
    void Stop();
}
