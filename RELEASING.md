# Releases

The current version is `0.2.0.0`. Windows in-game playback has been confirmed by
the developer; the remaining verification checklist is in README.md.

1. Update `Version` in `ChocoboRadio.csproj` when preparing a new release.
2. Run the stream checks:
   ```sh
   dotnet run --project tests/StreamChecks.csproj
   ```
3. Build Release against matching Dalamud API 15 development assemblies:
   ```sh
   dotnet build ChocoboRadio.csproj -c Release -p:DalamudLibPath="/path/to/Hooks/dev/"
   ```
4. Test on the Windows game PC, including mounting, game volume/mute, station
   changes, logout/unload, and network failures.
5. Create a draft GitHub release with a matching tag (for example `v0.2.0`).
   Attach `bin/Release/ChocoboRadio/latest.zip`, describe changes and limitations,
   and publish when ready.

The archive includes the plugin, manifest, NAudio dependencies, and their
license. Do not commit build output or the copied Dalamud SDK assemblies.

GitHub releases and a Dalamud custom plugin repository are separate. There is
no custom installer feed (`pluginmaster.json`) yet; users currently extract the
release and load it through Dalamud Dev Plugin Locations.
