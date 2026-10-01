# Chocobo Radio

Experimental Dalamud API 15 plugin based on the adjacent DJ Scraper template.
Shows a radio panel when you mount, including passenger seats and flying.
Open it manually with `/chocoboradio`.

Audio plays **inside the plugin**, using bundled NAudio libraries and Windows'
MP3 decoder/output. No external player, FFmpeg, or executable path is required.
The radio follows FFXIV's master and BGM volume/mute controls. Its own volume
slider is an additional multiplier and changes volume without reconnecting.

## Setup

1. Load the plugin and run `/chocoboradio`.
2. Expand **Stations**, click **Add station**, and enter a name and a direct
   HTTP(S) **MP3 audio stream** URL from a station you can use.
3. Press **Play / Reconnect**.
4. Optionally enable **Play selected station when mounting**.

Dismounting, logging out, and unloading cancel playback. Closing the panel keeps
playback running. Stop stays stopped until manual Play or the next mount.

No station is bundled. The earlier SomaFM preset was removed because its
[published stream terms](https://somafm.com/groovesalad/directstreamlinks.html)
exclude use in video games. Existing saved stations are preserved; the earlier
AAC preset will need removing/replacing. Old ffplay settings are ignored.

## What “in-game” means here

Playback runs in FFXIV's process and mirrors the game's master/BGM sliders and
mute switches through Dalamud's game configuration API. It is not injected into
FFXIV's native sound engine. The Windows default output device is used, which
may differ from the game's selected device. Background/inactive-window audio
settings, positional audio, and native music fades are not mirrored yet.

Normal game music is not automatically suppressed. Muting BGM also mutes the
radio, so it cannot be used to suppress only the original music. Automatically
replacing or ducking mount music is a separate future feature.

**Other players cannot hear this local playback**, including passengers on the
same mount. Playing a local sound effect would not transmit arbitrary audio to
other clients either. Shared listening would require compatible plugins on
participants' clients, a way to share station/session state, and synchronization.
That feature is not implemented; live station buffering can also differ between
listeners.

## Build and install

For a standalone checkout, install .NET 10 and obtain the matching Dalamud
API 15 development assemblies from your XIVLauncher installation. From the
repository folder, run (PowerShell example):

```powershell
dotnet build ChocoboRadio.csproj -c Release "-p:DalamudLibPath=C:\path\to\Hooks\dev\"
dotnet run --project tests/StreamChecks.csproj
```

Keep the trailing separator on `DalamudLibPath`. See [RELEASING.md](RELEASING.md)
for release packaging.

Use .NET and Dalamud development assemblies matching API 15. From the workspace
root, reuse the existing template's local SDK and assemblies:

```bash
scripts/dj-scraper-plugin/.dotnet/dotnet build \
  scripts/chocobo-radio/ChocoboRadio.csproj --configuration Release \
  -p:DalamudLibPath=/home/gmahin/workspace/scripts/dj-scraper-plugin/.dalamud/dev/
```

Copy the complete `bin/Release` output to a dedicated folder on the game PC,
including **NAudio.Core.dll** and **NAudio.WinMM.dll**. In Dalamud Settings >
Experimental > Dev Plugin Locations, add the `ChocoboRadio.dll` path. Keep the
generated JSON manifest and dependencies beside it. The release archive is
`bin/Release/ChocoboRadio/latest.zip`.

This project does not yet have a published custom repository or release URL.
The embedded player targets Windows; Wine/Linux/macOS playback is unverified.
Initial playback on the Windows game PC has been confirmed by the developer.

## Current stream support

- Direct MP3 streams only. AAC, HLS, playlist files, and website players are not
  supported. Use the station's direct MP3 endpoint.
- Requests audio without ICY metadata. Stations that insist on embedded metadata
  are rejected rather than feeding metadata bytes to the MP3 decoder.
- Downloads, frame decoding, and audio cleanup run off the game thread. Network
  reads are cancellable and time out after 15 seconds without completing a read.
- Audio buffers are bounded. Stop silences queued software samples and cancels
  the session; audio already submitted to the device may linger briefly.
- Failed/ended streams require **Play / Reconnect**. No automatic retries, song
  metadata, fades, or shared sessions yet.
- Zone transitions may stop playback if mount state briefly clears.

## Verification

The release build and automated stream checks can run on the development host:

```bash
scripts/dj-scraper-plugin/.dotnet/dotnet run \
  --project scripts/chocobo-radio/tests/StreamChecks.csproj
```

The checks cover actual NAudio MP3 frame parsing across fragmented reads,
position tracking on a non-seekable source, EOF/truncation, and cancellation of
stalled reads. They do not exercise Windows decoding/output or the game client.

Before release, test on the game PC:

- Play a direct MP3 station; verify no external player process is launched.
- Change radio, master, and BGM volume; test both game mute toggles. Confirm
  changes apply without reconnecting or changing the saved game settings.
- Mount, fly, land, dismount, and ride as a passenger. Landing alone should not
  stop playback. Check zoning behavior.
- Stop/switch stations repeatedly while connecting and playing; verify an old
  session never resumes audibly. Close/reopen the panel while playing.
- Log out or unload while connecting/playing; verify audio stops.
- Disconnect the network or output device; verify errors and manual reconnect.
