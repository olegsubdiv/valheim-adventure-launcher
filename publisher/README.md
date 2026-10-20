# Pack publisher

This folder builds releases from the existing pinned pack. It does not discover or upgrade mod versions, run the game, or touch the server.

## Build the payload

From the launcher directory:

```powershell
python publisher/build_manifest.py --project-root "F:\Work Projects\LLM\Valheim" --client-root "F:\Work Projects\LLM\Valheim\server\client-pack\ARPG-20261020-000757" --output artifacts/pack --version 2026.10.20.1 --artifact-base-url https://github.com/olegsubdiv/valheim-adventure-launcher/releases/download/content-2026.10.20.1
```

The project/client paths are publisher inputs only. They are not written into the public manifest.

Outputs:

- `manifest.payload.json`: unsigned payload for the launcher signing command.
- `adventure-configs-<version>.zip`: only the pack's changed text configuration and metadata.
- `provenance.json`: every managed file's upstream archive/member, source totals, and explicit generated-file exclusions.

The builder verifies each pinned archive's SHA256 and every exported canonical file's size/SHA256. Unmodified files are fetched from their original Thunderstore/Hexium archives. Unknown binary files are an error: they are never silently bundled in the custom ZIP. The base manifest keeps manual Nexus imports as the fallback. The private company source list can supply encrypted Google Drive mirrors without changing file ownership, versions or hashes. No mod archives are uploaded to GitHub.

The six exact default HUDCompass resource files recorded in `GENERATED_HUD` are excluded from ownership. The mod recreates them from its embedded resources. This is not a wildcard exclusion and does not delete a friend's existing/custom images.

CFG files preserve explicit keyboard shortcuts and selected display preferences. These defaults apply to a fresh installation; the engine merges configured personal keys when updating an existing profile. The publisher never edits the canonical source files.

After building, sign the payload using the launcher's signing CLI. Upload its referenced new artifacts first, verify them, then publish the signed channel manifest last. A failed builder exit must stop publication. Do not publish a manifest pointing at unuploaded or unverified files.

## Google Drive publisher

`Install-Rclone.ps1` installs official portable rclone 1.75.2 into `.tools/rclone`, checks the pinned ZIP SHA256, and changes neither PATH nor system installation.

Run `Configure-Drive.cmd` once. Google authorization is separate from the chat connector. The private configuration lives at:

```text
%LOCALAPPDATA%\ValheimAdventurePublisher\rclone.conf
```

Do not copy that directory into the application, repository, client pack, or diagnostics. The wrapper captures and deletes rclone's credential-bearing configure output instead of displaying it.

The remote uses the narrow `drive.file` scope: it can work with files/folders it creates. rclone's shared Google OAuth client still authorized successfully in the 2026-10-20 test, but rclone warns that it is being retired during 2026. A publisher-owned Desktop OAuth client is the durable replacement; follow [rclone's official instructions](https://rclone.org/drive/#making-your-own-client-id). The wrapper can ask for its client ID/secret without embedding either in source or releases.

Status:

```powershell
./publisher/Drive.ps1 -Action Status
```

Upload a file you are allowed to distribute:

```powershell
./publisher/Drive.ps1 -Action UploadAndVerify -SourceFile "D:\Releases\your-owned-archive.zip"
```

Files use a SHA256-prefixed remote name and `--immutable`, so a later upload cannot overwrite different bytes at that name. Upload verification checks the remote size and Drive MD5, then streams the file back to calculate an independent SHA256. `-SkipDownloadVerify` skips that last network pass only; do not use it for the initial transport check.

Uploading does not enable public sharing. Any public-download permission must be granted deliberately to a specific distribution file; credentials never belong in the client launcher. The initial multi-gigabyte test uses synthetic data only.

To create a bounded-memory synthetic ZIP64:

```powershell
python publisher/test_drive_transfer.py --output artifacts/drive-test/fresh-test.zip --bytes 6900000000
```

No user files are included. The generator refuses existing destinations or insufficient free space.

## Focused checks

```powershell
python -m unittest discover -s tests/publisher -v
```

These cover source pinning, unknown binaries, bad archive hashes, Windows traversal/ADS/reserved names, case collisions, leaking credentials/host paths, exact generated-output handling, and HTTPS-only production sources.

## Fresh configuration releases without rebuilding the offline pack

The legacy `server/Build-ClientPack.py` always copies the original 5.8GB HD archive and creates a full ZIP. It has no staging-only mode. It is unchanged.

For later configuration releases, keep `--client-root` as the trusted baseline allowlist and pass **both**:

```text
--live-profile <current-pinned-client-profile>
--server-config-root <explicit-active-server-BepInEx-config-directory>
```

The builder then reads existing known files from the current pinned profile in place. It overlays only text config files already known to the client baseline from the explicitly selected active server directory. It does not guess between the original server and a Baka copy. New per-player/runtime server files are not included. Finish editing configs before making a release; a file that changes while being read aborts the build.

This view does not copy the installed 28GB HD data or clone the profile. Its output remains the small custom configuration ZIP, manifest, and provenance. The original compressed HD archive is only streamed for its pinned checksum; it is neither copied nor recompressed. HD file hashes remain pinned to the existing HD receipt.

This option is deliberately a limited snapshot of the known baseline. Adding/removing mods or changing their installed layout requires refreshing the reviewed baseline and lock together; it must not silently pick up arbitrary new plugin files.

The five explicit personal config names are `BadgersShadingOverhaul.cfg`, `BepInEx.cfg`, `com.maxsch.valheim.vnei.cfg`, `ru.ivest.valheimvisuals.cfg`, and `shudnal.MyLittleUI.cfg`. All their existing keys are preserved when merging new defaults. Gameplay CFGs do not receive that blanket policy.

The bounded anonymous download check is "python publisher/probe_drive_download.py --file-id <ID> --expected-size <bytes>". It reads only the first 1KiB after the standard large-file confirmation form, checks HTTPS Google download hosts and the requested file ID, and requires a matching Content-Range. It uses no OAuth credentials. The upload result also exposes the stable download endpoint, which still needs deliberately granted read access.

## Encrypted company downloads

The owner explicitly chose private company distribution on2026-10-20. The two exact source archives are uploaded to Drive separately and checked against the pinned SHA256, remote size and MD5 before enabling link access. Links are not committed in plaintext.

Use `Publish-CompanySources.ps1 -SourcesFile <private verified receipt JSON>` to prepare locally, then `-Publish` to publish only `channel/company-sources.enc.json`. Input rows contain `id`, `size`, `sha256`, `downloadUrl`, `driveMd5Verified`. The publisher checks the two explicit IDs and their pins against the signed public collection.

A random256-bit code is generated once at `.private/company-code.txt` and reused. The plaintext source payload, signed source list, upload receipts and code all remain in `.private`/ignored artifacts. AES-256-GCM uses a random96-bit nonce and128-bit tag; the decrypted source list additionally requires the pinned ECDSA release signature. A code does not authorize changed mod files or settings.

Share the code privately with friends. The application stores it in the current Windows user settings; it is excluded from diagnostic ZIPs and redacted from logs. This is not per-person authentication or DRM: anyone with a code/decrypted Drive link can share it. Revoking an already leaked Drive link requires changing its Drive permission/file, not merely encrypting the same link again.
