using System;

namespace ChocoboRadio;

// Called only on the game thread. Owns only mute changes it actually made.
internal sealed class GameMusicController(Func<bool?> readMute, Action<bool> writeMute, Action<Exception> reportError)
{
    private bool requested;
    private bool attempted;
    public bool OwnsMute { get; private set; }

    public void Update(bool suppress)
    {
        try
        {
            var muted = readMute();
            if (muted == null) return; // Keep ownership so restoration can retry later.
            if (OwnsMute && !muted.Value)
            {
                // The user (or another plugin) unmuted music. Respect that for this session.
                OwnsMute = false;
            }
            if (!suppress)
            {
                if (OwnsMute)
                {
                    writeMute(false);
                    OwnsMute = false;
                }
                requested = false;
                attempted = false;
                return;
            }
            if (!requested) { requested = true; attempted = false; }
            if (attempted) return;
            attempted = true;
            if (!muted.Value)
            {
                writeMute(true);
                OwnsMute = true;
            }
        }
        catch (Exception ex) { reportError(ex); }
    }
}
