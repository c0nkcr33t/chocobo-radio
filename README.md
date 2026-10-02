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
2. Expand **Stations**, click **Add station**, and enter a name and a direct
   HTTP(S) **MP3 audio stream** URL from a station you can use.
3. Press **Play / Reconnect**.
4. Optionally enable **Play selected station when mounting**.

Dismounting, logging out, and unloading cancel playback. Closing the panel keeps
playback running. Stop stays stopped until manual Play or the next mount.

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
- Requests audio without ICY metadata. Stations that insist on embedded metadata
  are rejected rather than feeding metadata bytes to the MP3 decoder.
- Downloads, frame decoding, and audio cleanup run off the game thread. Network
  reads are cancellable and time out after 15 seconds without completing a read.
- Audio buffers are bounded. Stop silences queued software samples and cancels
  the session; audio already submitted to the device may linger briefly.
- Failed/ended streams require **Play / Reconnect**. No automatic retries, song
  metadata, fades, or shared sessions yet.
- Zone transitions may stop playback if mount state briefly clears.

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
retry checks. These tests do not exercise Windows audio output or the client.

On the game PC, verify music restoration after Stop, dismount, stream failure,
and unload. Also test with BGM already muted, suppression disabled, and manual
unmute during playback. Verify radio/master/BGM volume changes apply live.
Copy the entire new release archive, including the added NLayer libraries.

## One-command releases

With changes committed on `main`, run `./scripts/release.sh MAJOR.MINOR.PATCH`.
GitHub Actions builds, tests, and publishes the ZIP; the shared catalog picks it
up within about 30 minutes (GitHub schedules may be delayed). See [docs/RELEASING.md](docs/RELEASING.md)
for details.
