# Valheim Adventure RPG Launcher

A small Windows launcher for our pinned cooperative Valheim collection.

## Players

Download **AdventureLauncher-Setup.exe** from the [latest launcher release](https://github.com/olegsubdiv/valheim-adventure-launcher/releases/latest). Install it, select your Steam Valheim folder and the modpack storage folder, then choose **Update and play**.

Existing r2modman profiles can be selected directly, including a profile moved to another drive through a junction. Matching installed files are reused. New installations can use an empty folder on any drive. Keep Steam signed in.

- **Install / update** applies the published collection without starting the game.
- **Verify / repair** checks hashes and repairs missing or damaged managed files.
- **Restore previous version** restores the last completed update.
- **Collect diagnostics** creates a small ZIP to send to the maintainer.
- **Play** starts the current local collection without a network update.

Choose **Texture quality: Original (no HD), Medium or Higher** before installing or updating. **Original (no HD)** uses the game's original textures and skips the Willybach HD download. Switching from Medium/Higher removes the managed HD plugin and data with a rollback backup; the other mods and gameplay settings remain the same. This is the lighter option for a PC with 16 GB RAM, but actual memory use must be checked on that PC and a Windows-managed page file should remain enabled. Medium remains the default for new installations. Existing installations keep their selected quality until you choose a different variant. Apply a changed choice with **Install / update** or **Update and play** while Valheim is closed; **Play** keeps the current files. **Restore previous version** can undo the last applied switch. Cached HD downloads remain available for later reuse.

Choose **Game language: English or Русский** for Valheim and mods that provide translations. English remains the default. The choice applies when you press **Play** or **Update and play**, without changing shared modpack settings. Some mod text may remain English. This uses Valheim's normal per-user language preference, so it also affects normal Valheim starts on the same Windows account. The original preference is saved once before the first change. The launcher interface stays English.

Enter the private **Company access code** supplied by the maintainer to enable automatic Willybach HD and Badger downloads from Google Drive. The app remembers it on this PC. Without a code, the exact official Nexus archives can still be imported manually. Matching installed files are reused.

The public repository contains only an AES-256-GCM encrypted source list. The random 256-bit code stays outside Git. Decrypted sources also carry the maintainer signature and must match the public collection's exact archive hashes. This is shared-code access, not a Google account login: anyone who receives the code or decrypted links can download the archives.

## Updates

The main **Play** button starts the installed modpack through Steam. **Update launcher** is always visible in the top right. The app checks for a newer launcher when opened and shows its status there; click the button to download and restart when an update is available, or to check again. An offline check does not prevent playing. Portable builds link to the latest installer.

The application and collection have separate versions. [Velopack](https://velopack.io) handles application updates from stable GitHub Releases. The signed channel/stable.json describes the collection. Content releases are prereleases so they do not replace the latest application installer.

Versions and SHA256 hashes are pinned. Catalog packages come from Thunderstore/Hexium. The repository publishes our configuration snapshot and source metadata. Permitted raw files may also be hosted on Google Drive.

Downloads are staged and verified before commit. Only changed files are backed up. A transaction journal recovers interrupted changes. Removed managed files are deleted only if they still match their recorded installed contents. Personal keys in the configured settings policy survive updates.

Logs rotate at approximately 256 KiB, keeping three files; diagnostics retain three bounded ZIPs. Downloaded archives are cached separately. The launcher keeps game saves and server worlds outside its update targets.

## Maintainers

Build on Windows with Python 3.12+ and .NET SDK 10.0.402. A private local SDK can be placed in .tools/dotnet. Dependencies are pinned.

Run **build/Build.ps1 -Version 0.1.0**. It runs regressions, publishes a self-contained application, performs an invisible WPF smoke/render check, and creates the installer, portable archive and update assets under artifacts/releases.

Prepare a collection with **publisher/Release-Pack.ps1 -Version 2026.10.20.2 -ClientRoot D:\\PreparedClient**. Use the explicit live-profile/server-config options documented in [publisher/README.md](publisher/README.md) for changed configurations. Review local output, then repeat with **-Publish**. Assets are uploaded and downloaded for checksum verification before the stable channel is pushed. Published content versions are immutable.

The signing key is local at .private/manifest-signing.pem, excluded from Git. The public key is bundled with the application. Preserve a secure backup of the private key; replacing it requires a deliberate trust migration. Publisher credentials and server passwords must never be included in a release.

Google Drive publishing uses the separate publisher tools. OAuth credentials remain in local application data; players never receive them.

## License

Our code is MIT. Bundled runtime and dependencies retain their own licenses. Mods and texture packs retain their authors' terms.
