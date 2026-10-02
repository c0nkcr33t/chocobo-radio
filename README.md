# Chocobo Radio

Shows a radio panel when you mount, including passenger seats and flying.
Open it manually with `/chocoboradio`.

Audio plays **inside the plugin**, using NLayer for MP3 decoding and NAudio for
Windows audio output. No external player, FFmpeg, or executable path is required.
The radio follows FFXIV's master and BGM volume/mute controls. Its own volume
slider is an additional multiplier and changes volume without reconnecting.

## Install through Dalamud

Add this URL under `/xlsettings` → **Experimental → Custom Plugin Repositories**:

```text
https://raw.githubusercontent.com/c0nkcr33t/dalamud-plugins/main/pluginmaster.json
```

Save, open `/xlplugins`, and install **Chocobo Radio**. The matching GitHub
release must be published with `latest.zip` attached before installation works.
If you previously loaded the dev build, disable it and remove its Dev Plugin
Location first to avoid loading two copies. Restart the game if needed.

## Setup

1. Load the plugin and run `/chocoboradio`.
2. Click **Stations** at the top of the mini player, click **Add station**, and enter a name and a direct
   HTTP(S) **MP3 audio stream** URL. Click **Save new station**, then **Tune in**.
3. Use the mini player for Play/Stop, volume, and previous/next station controls.
4. Set autoplay and **Keep playing off mount** in the **Settings** popup.

By default, dismounting stops playback. **Keep playing off mount** lets it
continue throughout your session. Logout and unload still stop it. Autoplay
starts on mounting in mount mode, or login/enabling full-time mode in full-time
mode. Pressing Stop or a stream failure does not cause an automatic retry.
Closing the panel keeps playback running.

## Build and install

For a standalone checkout, install .NET 10 and obtain the matching Dalamud
API 15 development assemblies from your XIVLauncher installation. From the
repository folder, run (PowerShell example):

```powershell
dotnet build ChocoboRadio.csproj -c Release "-p:DalamudLibPath=C:\path\to\Hooks\dev\"
dotnet run --project tests/StreamChecks.csproj
```

Keep the trailing separator on `DalamudLibPath`. See [release instructions](docs/RELEASING.md)
for release packaging.

Use .NET and Dalamud development assemblies matching API 15. From the workspace
root, reuse the existing template's local SDK and assemblies:

```bash
scripts/dj-scraper-plugin/.dotnet/dotnet build \
  scripts/chocobo-radio/ChocoboRadio.csproj --configuration Release \
  -p:DalamudLibPath=/home/gmahin/workspace/scripts/dj-scraper-plugin/.dalamud/dev/
```

Copy the complete `bin/Release` output to a dedicated folder on the game PC,
including all **NAudio** and **NLayer** DLLs and license files. In Dalamud Settings >
Experimental > Dev Plugin Locations, add the `ChocoboRadio.dll` path. Keep the
generated JSON manifest and dependencies beside it. The release archive is
`bin/Release/ChocoboRadio/latest.zip`.

The custom repository feed is `pluginmaster.json`; see the installation section above.
The embedded player targets Windows; Wine/Linux/macOS playback is unverified.
Initial playback on the Windows game PC has been confirmed by the developer.

## Current stream support

- Direct MP3 streams only. AAC, HLS, playlist files, and website players are not
  supported. Use the station's direct MP3 endpoint.
- Requests ICY track metadata and removes its blocks before MP3 decoding.
  Artist/title display uses the common `Artist - Title` format, otherwise the
  full station-provided text is displayed. UTF-8 and Latin-1 text are supported.
  Stations without metadata still play normally. Titles may lead audible audio
  slightly because of buffering. Album art and external lookups are deferred.
- Downloads, frame decoding, and audio cleanup run off the game thread. Network
  reads are cancellable and time out after 15 seconds without completing a read.
- Audio buffers are bounded. Stop silences queued software samples and cancels
  the session; audio already submitted to the device may linger briefly.
- Failed/ended streams require **Play**. Use **Reconnect** to reconnect a running
  stream. No automatic retries, fades, or shared sessions yet.
- Zone transitions may stop mount-only playback if mount state briefly clears.

## Player display

The mini stereo is the only player view. **Stations** and **Settings** buttons
at the top open separate popups; use Close or click outside to dismiss them.
The player defaults to a narrower 400-unit width, with a height calculated from
the actual font and controls. Resize grips are hidden; the player fits its controls automatically. The station stays on the first display line; artist and title share one
scrolling line below it. A vertical volume slider sits to the right of the
display and transport buttons, with its current value shown inside the slider.
Turn scrolling off in Settings. Hover the display for the full track and status.

Save station edits explicitly; deletion requires confirmation. Previous/next
wraps around the saved list and changes audio immediately while playing.

## Game music

**Mute game music while radio plays** defaults to enabled. Music is muted once
radio playback starts, then restored when playback stops, fails, or the plugin
unloads. Master and BGM sliders still control radio volume. Only a mute applied
by Chocobo Radio is ignored for radio volume; a pre-existing user mute still
silences it.

If you unmute music during playback, that override is respected for the rest of
the session. Use radio Stop/volume controls to silence radio while suppression
is active, since the game's BGM checkbox is already muted. Another plugin
writing the same mute value cannot be distinguished from our own change.
A game crash cannot run restoration; check the BGM mute manually afterward.

This still uses plugin audio output, not FFXIV's native audio engine. Other
players do not hear the stream automatically. NLayer removes the Windows ACM
decoder dependency; Wine/Proton playback still needs verification.

## Repository layout

- `Plugin.cs`: Dalamud services and mount lifecycle.
- `Audio/`: streaming, decoder/output, and music mute ownership.
- `Settings/`: persisted settings and station definitions.
- `Windows/`: in-game interface.
- `tests/`: stream and music-state checks.
- `docs/`: release instructions.
- `licenses/`: dependency licenses included in the release.

Namespaces remain unchanged to preserve saved configuration compatibility.

## Testing this update

Run `dotnet run --project tests/StreamChecks.csproj` for fragmented MP3 reads,
EOF/truncation, cancellation, music restoration, user overrides, and restoration
retry checks, ICY framing/metadata, and playback mode transitions. These tests do not exercise Windows audio output or the client.

On the game PC, verify music restoration after Stop, dismount, stream failure,
and unload. Also test with BGM already muted, suppression disabled, and manual
unmute during playback. Verify radio/master/BGM volume changes apply live.
Also test metadata on a station that provides it, mini-player sizing and popup scrolling at
your UI scale, station add/rename/delete, and full-time dismount/logout behavior.
Copy the entire new release archive.

## One-command releases

With changes committed on `main`, run `./scripts/release.sh MAJOR.MINOR.PATCH`.
GitHub Actions builds, tests, and publishes the ZIP; the shared catalog picks it
up within about 30 minutes (GitHub schedules may be delayed). See [docs/RELEASING.md](docs/RELEASING.md)
for details.

### Stereo faceplate controls

Drag the **Chocobo Radio** badge to move the stereo, click **X** to hide it, and use
`/chocoboradio` to reopen. There is no full-size view or expand/collapse toggle.
**Stations** opens the library/editor; **Settings** opens playback/display
preferences, current player status, and Reconnect. Existing saved station and
playback settings are preserved; the obsolete compact-view setting is ignored.

### Station name lookup

After entering a new stream URL, leave its field to look up the station's
`icy-name` header if the name is still empty or “New station.” You can also
click **Find station name**. The lookup requests headers without starting audio,
times out after eight seconds, and never replaces a name you edit while waiting.
Click Save to keep the result. Not all stations provide this header; manual
names remain supported.

Stations and Settings use small, non-modal popups with explicitly sized,
scrollable content to avoid collapsed sizing. They do not dim the game. Transport buttons divide the width below the display equally.
The middle button remains Play/Stop: stopping disconnects the live stream.
