namespace ChocoboRadio;

internal enum PlaybackAction { None, Play, Stop }

// Autoplay is edge-triggered: pressing Stop or a stream failure never starts a retry loop.
internal sealed class PlaybackPolicy
{
    private bool wasLoggedIn;
    private bool wasEligible;
    private bool wasAutoPlay;

    public PlaybackAction Update(bool loggedIn, bool mounted, bool fullTime, bool autoPlay, bool running)
    {
        var eligible = loggedIn && (fullTime || mounted);
        var action = PlaybackAction.None;
        if ((wasLoggedIn && !loggedIn) || (wasEligible && !eligible)) action = PlaybackAction.Stop;
        else if (eligible && autoPlay && (!wasEligible || !wasAutoPlay) && !running) action = PlaybackAction.Play;
        wasLoggedIn = loggedIn;
        wasEligible = eligible;
        wasAutoPlay = autoPlay;
        return action;
    }
}
