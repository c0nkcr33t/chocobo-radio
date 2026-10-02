using System;

namespace ChocoboRadio;

internal static class DismountTransition
{
    // Fixed, conservative timing: the game does not expose a mount-music completion event here.
    internal const double FadeMilliseconds = 800;
    internal const double MusicHoldMilliseconds = 3000;

    internal static float Gain(double elapsedMilliseconds)
    {
        var progress = Math.Clamp(elapsedMilliseconds / FadeMilliseconds, 0, 1);
        return (float)(1 - progress * progress * (3 - 2 * progress));
    }

    internal static bool HoldMusic(double elapsedMilliseconds) => elapsedMilliseconds < MusicHoldMilliseconds;
}
