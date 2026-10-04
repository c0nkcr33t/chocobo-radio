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
  and direct HTTP(S) MP3 stream URL, then save. Chocobo Radio briefly inspects
  new URLs to verify MP3 audio and retrieve the station name when available.
  Website links, playlist files, AAC, and HLS are not supported.
- The **power button** toggles standby, stopping audio and blocking autoplay while off.
- Use **Play/Pause**, previous/next station, and the vertical volume slider.
  Track information appears when provided by the station.
- Click the **gear icon** for autoplay, **Keep playing off mount**, game-music
  muting, cutscene volume reduction, accent colors, and scrolling. Cutscene reduction
  defaults to 20% of normal volume and can be disabled. Click the active panel icon or its up
  arrow to collapse the panel.
