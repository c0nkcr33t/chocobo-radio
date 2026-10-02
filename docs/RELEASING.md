# Automated releases

From the repository root, commit your changes on `main`, then run:

```bash
./scripts/release.sh 0.4.0
```

Choose the next version for this plugin. Requires Git, Python 3, and push access.
On Windows, use Git Bash or `python scripts/release.py 0.4.0` in PowerShell.
The helper updates the project version, commits it, creates an annotated tag,
and atomically pushes main and the tag. It refuses dirty working trees,
non-main branches, existing tags, and backward versions.

To release the project's current version for the first time, pass that version
without the final `.0` (Chocobo Radio `0.3.0.0` → `0.3.0`). An existing published
tag such as DJ Scraper's `v0.1.0` cannot be reused; pick a newer version.

## What GitHub does

The **Release** Action:

1. Installs .NET 10 and downloads a checksum-pinned official Dalamud API 15 SDK.
2. Restores locked dependencies, builds Release, and runs available test projects.
3. Checks that the tag, project version, and packaged manifest agree.
4. Uploads `latest.zip` to a draft release, then publishes only after upload.

Main-branch pushes and manual workflow runs build/test and save an Actions
artifact; only `v*` tag runs publish a GitHub release. Watch the Actions tab for
results. For transient failures, rerun the failed workflow. Published releases
are never overwritten. Failed pushes leave the local commit/tag for retry;
the helper prints the retry command.

No personal access token is needed. The workflow requests `contents: write`
only for its publishing job. Actions must be enabled in the repository.

## Shared installer feed

Users should add:

```text
https://raw.githubusercontent.com/c0nkcr33t/dalamud-plugins/main/pluginmaster.json
```

The shared repository checks published releases every 30 minutes and updates
entries from their ZIP manifests. To refresh sooner, run its **Refresh plugin
catalog** workflow manually. GitHub scheduling may add delay. The legacy
per-plugin `pluginmaster.json` is not automatically updated; migrate users to
the shared feed for automatic future updates.

## SDK upgrades

`scripts/build_release.py` pins the Dalamud distribution revision and SHA-256.
When upgrading Dalamud API, update those constants, the expected API check,
and the project's SDK version together, then validate locally. Do not replace
the pinned URL with a moving latest build.

Optional local CI check (requires dotnet on PATH and network access):

```bash
python3 scripts/build_release.py
```

Test in-game before tagging. CI cannot verify actual game behavior.
