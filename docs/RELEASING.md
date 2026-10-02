# Releases

The current version is `0.3.0.0`. Windows in-game playback has been confirmed by
the developer; the remaining verification checklist is in ../README.md.

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
5. Create a draft GitHub release with a matching tag (for example `v0.3.0`).
   Attach `bin/Release/ChocoboRadio/latest.zip`, describe changes and limitations,
   and publish when ready.

The archive includes the plugin, manifest, NAudio and NLayer dependencies, and their
license. Do not commit build output or the copied Dalamud SDK assemblies.

The custom repository URL is:

```text
https://raw.githubusercontent.com/c0nkcr33t/chocobo-radio/main/pluginmaster.json
```

For the initial release, publish tag `v0.3.0` with the built `latest.zip` attached.
The feed points at that exact tag. For later releases, update `AssemblyVersion`,
`LastUpdate` (Unix seconds), and both download URLs in `pluginmaster.json`.
The feed version must exactly match the manifest inside the ZIP. Publish the
release asset before pushing the updated feed so clients can download it.
