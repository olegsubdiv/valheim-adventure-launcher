# Windows build

Use the pinned .NET 10 SDK from `global.json`. The build uses Velopack **1.2.161** for both its application SDK and local packaging CLI.

```powershell
.\build\Build.ps1 -Version 0.1.0
```

The script runs core and client regressions, publishes a self-contained Windows x64 application, runs its invisible `--self-test`, and creates the installer, portable archive and update packages in `artifacts/releases`. Windows users do not need to install .NET separately. Build outputs and the local SDK/tools stay outside source control.

The application reads the pack feed and signing public key from its bundled `release-settings.json`. Never add a private signing key, mod archive or OAuth credential to this repository.

Launcher releases and modpack releases are separate:

- Publish Velopack's generated installer/update assets as a **stable application release** on GitHub.
- Publish modpack content as a **prerelease** or another content location, then update the signed `channel/stable.json` pointer only after the referenced content is available.
- The launcher update source ignores prereleases. A content-only release must not replace the stable application release.

The CI workflow builds and uploads artifacts only. It does not publish a GitHub release or handle signing secrets.

`--self-test <report-path>` constructs and lays out the WPF window without showing it, changing a profile, checking the network or launching Valheim. It renders previews at 980x800 and 820x700 using synthetic paths. This is a structural smoke check, not an interactive UI test or a game compatibility test.
