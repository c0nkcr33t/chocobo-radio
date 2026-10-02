# Chocobo Radio

## Overview

An internet radio player for FFXIV with a compact, retro stereo interface that
appears when you mount. Listen to saved stations, see artist and track metadata,
and control playback without an external player.

Supports direct **MP3 streams** and follows FFXIV’s master and music volume
settings. Audio is local to your PC; other players do not hear it.

## Installation

1. Open `/xlsettings` → **Experimental → Custom Plugin Repositories**.
2. Add this URL and save:

   ```text
   https://raw.githubusercontent.com/c0nkcr33t/dalamud-plugins/main/pluginmaster.json
   ```

3. Open `/xlplugins` and install **Chocobo Radio**.

If switching from a development build, disable it and remove its Dev Plugin
Location first.

## Usage

- Open the player with `/chocoboradio`, or mount up. Drag the title to move it;
  **X** hides the player without stopping playback.
- Click the **antenna icon** for Stations. Choose **Add station**, enter a name
  and direct HTTP(S) MP3 stream URL, then **Save new station** and **Tune in**.
  Website links, playlist files, AAC, and HLS are not supported.
- Use **Play/Stop**, previous/next station, and the vertical volume slider.
  Track information appears when provided by the station.
- Click the **gear icon** for autoplay, **Keep playing off mount**, game-music
  muting, accent colors, and scrolling. Click the active panel icon or its up
  arrow to collapse the panel.

In mount-only mode, dismounting fades the radio out and keeps the stream ready
for 15 seconds for a quick remount. With game-music muting enabled, music is
restored after a brief delay to cover the mount-theme transition. Manual Stop
and logout close the stream immediately.
