#!/usr/bin/env python3
"""Build a pinned Adventure RPG manifest without redistributing upstream mod bytes."""
from __future__ import annotations
import argparse
import collections
import datetime as dt
import hashlib
import json
import pathlib
import re
import sys
import urllib.parse
import zipfile
from dataclasses import dataclass

RESERVED = {"CON", "PRN", "AUX", "NUL"} | {f"{p}{i}" for p in ("COM", "LPT") for i in range(1, 10)}
TEXT_EXTENSIONS = {".cfg", ".json", ".yml", ".yaml", ".txt", ".ini", ".xml", ".csv", ".md"}
PERSONAL_CONFIGS = {"badgersshadingoverhaul.cfg", "bepinex.cfg", "com.maxsch.valheim.vnei.cfg", "ru.ivest.valheimvisuals.cfg", "shudnal.mylittleui.cfg"}
PERSONAL_KEY = re.compile(r"(?:hot.?key|shortcut|keybind|keyboard|toggle.?key|font.?size|ui.?scale|hud.?scale|window.?position|window.?size|volume)", re.I)
SECRET_FIELD = re.compile(r'(?i)(?:"?(?:access_token|refresh_token|client_secret|authorization|discordwebhook|webhookurl|password)"?\s*[:=]\s*)(.+)')
HOST_PATH = re.compile(r"(?i)(?:[a-z]:[\\/]+Users[\\/]+|[a-z]:[\\/]+Work Projects[\\/]+|[a-z]:[\\/]+SteamLibrary[\\/]+)")
ALLOWED_ROOTS = ("BepInEx/", "bootstrap/")
GENERATED_HUD = {"BepInEx/config/Neobotics/HUDCompass/" + name for name in ("center.png", "compass.png", "English.loc", "mapicon_anchor.png", "mapicon_cart.png", "mask.png")}

def read_json(path: pathlib.Path):
    return json.loads(path.read_text(encoding="utf-8-sig"))

def digest_file(path: pathlib.Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for block in iter(lambda: f.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()

def digest_stream(f) -> str:
    h = hashlib.sha256()
    for block in iter(lambda: f.read(1024 * 1024), b""):
        h.update(block)
    return h.hexdigest()

def safe_path(raw: str) -> str:
    path = raw.replace("\\", "/")
    if not path or path.startswith("/") or ":" in path or "\x00" in path:
        raise ValueError(f"Unsafe relative path: {raw!r}")
    parts = path.split("/")
    if any(not p or p in (".", "..") or p.endswith((" ", ".")) or
           any(c in p for c in '<>"|?*') or p.split(".")[0].upper() in RESERVED for p in parts):
        raise ValueError(f"Unsafe relative path: {raw!r}")
    return "/".join(parts)

def reject_duplicates(paths, context: str) -> None:
    seen = set()
    for path in paths:
        key = safe_path(path).casefold()
        if key in seen:
            raise ValueError(f"Case-insensitive duplicate in {context}: {path}")
        seen.add(key)

def validate_public_url(url: str, allow_local: bool = False) -> None:
    p = urllib.parse.urlparse(url)
    if p.scheme == "https" and p.hostname and p.username is None and p.password is None:
        return
    if allow_local and (p.scheme == "file" or (p.scheme == "http" and p.hostname in ("localhost", "127.0.0.1", "::1"))):
        return
    raise ValueError(f"Artifact URL must be public HTTPS: {url!r}")

def assert_publishable_text(data: bytes, path: str) -> str:
    text = data.decode("utf-8-sig")
    if HOST_PATH.search(text):
        raise ValueError(f"Local host path found in release content: {path}")
    for line in text.splitlines():
        if line.lstrip().startswith(("#", "//", ";")):
            continue
        match = SECRET_FIELD.search(line)
        if match:
            value = match.group(1).strip().strip(",").strip().strip('"').strip("'").strip()
            if value and value.casefold() not in ("none", "null", "false", "true", "[]", "{}", "changeme", "change_me"):
                raise ValueError(f"Possible credential in release content: {path}")
    return text

def config_policy(path: str, text: str) -> tuple[str, list[str]]:
    if pathlib.PurePosixPath(path).suffix.lower() not in (".cfg", ".ini"):
        return "managed", []
    section, setting_type = "", ""
    preserved = []
    for line in text.splitlines():
        stripped = line.strip()
        if stripped.startswith("[") and stripped.endswith("]"):
            section = stripped[1:-1]
        elif stripped.startswith("# Setting type:"):
            setting_type = stripped.partition(":")[2].strip()
        elif stripped and not stripped.startswith(("#", ";")) and "=" in stripped:
            key = stripped.partition("=")[0].strip()
            if setting_type.casefold() in ("keyboardshortcut", "keycode", "shortcut") or PERSONAL_KEY.search(key) or pathlib.PurePosixPath(path).name.casefold() in PERSONAL_CONFIGS:
                preserved.append(f"{section}/{key}")
            setting_type = ""
    return "config", sorted(set(preserved))

@dataclass(frozen=True)
class Member:
    artifact_id: str
    entry: str
    size: int
    sha256: str

def make_artifact(package: dict, archive: pathlib.Path, allow_local: bool) -> dict:
    aid = package["full_name"]
    digest = digest_file(archive)
    expected = package.get("sha256", "").lower()
    if len(expected) != 64 or digest != expected:
        raise ValueError(f"Pinned archive SHA256 mismatch: {archive.name}")
    urls = [package["download_url"]] if package.get("download_url") else []
    for url in urls:
        validate_public_url(url, allow_local)
    if not urls and package.get("namespace") not in ("Badger", "Willybach"):
        raise ValueError(f"No verified source URL for {aid}")
    return {
        "id": aid, "kind": "7z" if archive.suffix.lower() == ".7z" else "zip",
        "fileName": archive.name, "size": archive.stat().st_size, "sha256": digest,
        "urls": urls,
        "manualUrl": package.get("website_url") if not urls else None,
        "instructions": "Download this exact version from the official mod page, then import its original archive. Existing matching installed files are reused." if not urls else None,
    }

def canonical_inventory(client_root: pathlib.Path) -> dict[str, dict]:
    inventory = {}
    for row in read_json(client_root / "client-files.json"):
        path = safe_path(row["path"])
        if path.startswith(ALLOWED_ROOTS) or path == "release.json":
            key = path.casefold()
            if key in inventory:
                raise ValueError(f"Duplicate canonical path: {path}")
            actual = client_root / pathlib.Path(path)
            if actual.is_symlink() or not actual.is_file():
                raise ValueError(f"Canonical file missing or is a symlink: {path}")
            if actual.stat().st_size != row["bytes"] or digest_file(actual) != row["sha256"].lower():
                raise ValueError(f"Canonical file SHA256 mismatch: {path}")
            inventory[key] = {"path": path, "sha256": row["sha256"].lower(), "size": row["bytes"], "source": actual}
    return inventory

def apply_live_snapshot(inventory: dict[str, dict], live_profile: pathlib.Path, server_config_root: pathlib.Path) -> dict[str, dict]:
    """Read a fresh limited view in place; never clone the profile or the HD archive."""
    if not live_profile.is_dir() or not server_config_root.is_dir():
        raise ValueError("Both live profile and explicit server config directories must exist")
    result = {}
    for key, baseline in inventory.items():
        path = baseline["path"]
        row = dict(baseline)
        source = baseline["source"]
        if path.startswith("BepInEx/"):
            source = live_profile / pathlib.Path(path)
            if not source.is_file():
                raise ValueError(f"Known client payload is missing from the live profile: {path}")
            # The selected root may be the friend's legitimate profile junction. Nested links are not snapshot sources.
            for part in [source, *source.parents]:
                if part == live_profile:
                    break
                if part.is_symlink() or (hasattr(part, "is_junction") and part.is_junction()):
                    raise ValueError(f"Nested link is not a release snapshot source: {path}")
            if path.startswith("BepInEx/config/") and source.suffix.lower() in (".cfg", ".json", ".yml", ".yaml", ".txt"):
                relative = pathlib.Path(path[len("BepInEx/config/"):])
                server_source = server_config_root / relative
                if server_source.is_file():
                    for part in [server_source, *server_source.parents]:
                        if part == server_config_root:
                            break
                        if part.is_symlink() or (hasattr(part, "is_junction") and part.is_junction()):
                            raise ValueError(f"Nested server config link is not a release source: {path}")
                    source = server_source
            before = source.stat()
            row["source"] = source
            if source.suffix.lower() in TEXT_EXTENSIONS:
                row["snapshotBytes"] = source.read_bytes()
                row["sha256"] = hashlib.sha256(row["snapshotBytes"]).hexdigest()
                row["size"] = len(row["snapshotBytes"])
            else:
                row["sha256"] = digest_file(source)
                row["size"] = before.st_size
            after = source.stat()
            if before.st_size != after.st_size or before.st_mtime_ns != after.st_mtime_ns:
                raise ValueError(f"Source changed during snapshot; finish editing and retry: {path}")
        result[key] = row
    return result
def build_manifest(project_root: pathlib.Path, client_root: pathlib.Path, output: pathlib.Path,
                   version: str, artifact_base_url: str, allow_local: bool = False,
                   live_profile: pathlib.Path | None = None, server_config_root: pathlib.Path | None = None) -> dict:
    if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]{0,79}", version):
        raise ValueError("Version must be a portable release identifier")
    validate_public_url(artifact_base_url, allow_local)
    packages = read_json(project_root / "installed.lock.json")
    reject_duplicates([x["full_name"] for x in packages], "package IDs")
    canonical = canonical_inventory(client_root)
    if bool(live_profile) != bool(server_config_root):
        raise ValueError("Live snapshot requires both --live-profile and explicit --server-config-root")
    if live_profile is not None:
        canonical = apply_live_snapshot(canonical, live_profile, server_config_root)
    artifacts, member_index, owned, stats = [], collections.defaultdict(list), {}, collections.Counter()
    hd = None
    for package in packages:
        archive = pathlib.Path(package["archive"])
        if not archive.is_file():
            raise ValueError(f"Pinned archive missing: {archive.name}")
        if package.get("namespace") == "Willybach":
            # 5.8GB archive is already pinned; its 152 installed outputs have independent SHA256 receipts.
            # Still verify the archive SHA in a bounded-memory streaming read before publishing provenance.
            artifact = make_artifact(package, archive, allow_local)
            hd = (package, artifact)
            artifacts.append(artifact)
            continue
        artifact = make_artifact(package, archive, allow_local)
        artifacts.append(artifact)
        for installed in package.get("installed_files", []):
            owned[safe_path(installed).casefold()] = artifact["id"]
        with zipfile.ZipFile(archive) as z:
            entries = [i for i in z.infolist() if not i.is_dir()]
            reject_duplicates([i.filename for i in entries], archive.name)
            for info in entries:
                entry = safe_path(info.filename)
                if (info.external_attr >> 16) & 0o170000 == 0o120000:
                    raise ValueError(f"Archive symlink not allowed: {archive.name}:{entry}")
                with z.open(info) as f:
                    digest = digest_stream(f)
                member_index[(digest, info.file_size)].append(Member(artifact["id"], entry, info.file_size, digest))
    if hd:
        for row in read_json(client_root / "hd-files.json"):
            path = safe_path(row["path"])
            prefix = "BepInEx/plugins/"
            if not path.startswith(prefix):
                raise ValueError(f"Unexpected HD output path: {path}")
            member_index[(row["sha256"].lower(), row["bytes"])].append(Member(hd[1]["id"], path[len(prefix):], row["bytes"], row["sha256"].lower()))
    output.mkdir(parents=True, exist_ok=True)
    files, overlay, provenance, unmapped_binary, generated = [], [], [], [], []
    for key, row in sorted(canonical.items()):
        path, sha, size = row["path"], row["sha256"], row["size"]
        if path in GENERATED_HUD:
            generated.append({"path": path, "sha256": sha, "regenerated_by": "Neobotics-HUDCompass", "reason": "Default embedded resource copied by the plugin; excluded from owned file list"})
            continue
        matches = member_index.get((sha, size), [])
        # Prefer the package recorded as owner; bootstrap core is known to BepInExPack.
        preferred = owned.get(key)
        if path.startswith("bootstrap/"):
            preferred = next((a["id"] for a in artifacts if a["id"].startswith("denikson-BepInExPack_Valheim-")), None)
        match = next((m for m in matches if m.artifact_id == preferred), matches[0] if matches else None)
        policy, preserve = "managed", []
        source_path = row["source"]
        source_bytes = row.get("snapshotBytes")
        if source_path.suffix.lower() in TEXT_EXTENSIONS:
            text = (source_bytes if source_bytes is not None else source_path.read_bytes()).decode("utf-8-sig")
            policy, preserve = config_policy(path, text)
        if match:
            aid, entry = match.artifact_id, match.entry
            stats["upstreamFiles"] += 1
        else:
            if source_path.suffix.lower() not in TEXT_EXTENSIONS:
                unmapped_binary.append(path)
                continue
            aid, entry = "adventure-configs-" + version, path
            data = source_bytes if source_bytes is not None else source_path.read_bytes()
            assert_publishable_text(data, path)
            overlay.append((path, data))
            stats["customFiles"] += 1
        files.append({"path": path, "artifactId": aid, "entry": entry, "size": size,
                      "sha256": sha, "policy": policy, "preserveKeys": preserve})
        provenance.append({"path": path, "artifactId": aid, "entry": entry, "sha256": sha})
    if unmapped_binary:
        raise ValueError("Unmapped binaries/assets cannot be republished in custom overlay: " + "; ".join(unmapped_binary))
    if hd:
        package, artifact = hd
        hd_files = read_json(client_root / "hd-files.json")
        reject_duplicates([x["path"] for x in hd_files], "HD receipt")
        expected_hd = {safe_path(x).casefold() for x in package["installed_files"]}
        for row in hd_files:
            path = safe_path(row["path"])
            if path.casefold() not in expected_hd:
                raise ValueError(f"HD receipt file absent from lock: {path}")
            if path.casefold() in canonical:
                # Loader and MaterialOverrides may also appear in the export. Keep the precise canonical payload mapping.
                existing = next(x for x in files if x["path"].casefold() == path.casefold())
                if existing["sha256"] != row["sha256"].lower():
                    raise ValueError(f"Canonical/HD receipt mismatch: {path}")
                continue
            prefix = "BepInEx/plugins/"
            if not path.startswith(prefix):
                raise ValueError(f"Unexpected HD output path: {path}")
            files.append({"path": path, "artifactId": artifact["id"], "entry": path[len(prefix):],
                          "size": row["bytes"], "sha256": row["sha256"].lower(), "policy": "managed", "preserveKeys": []})
            stats["hdFiles"] += 1
        if expected_hd != {safe_path(x["path"]).casefold() for x in hd_files}:
            raise ValueError("HD receipt does not cover every pinned HD file")
    if overlay:
        name = "adventure-configs-" + version + ".zip"
        target = output / name
        with zipfile.ZipFile(target, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as z:
            for path, data in sorted(overlay):
                info = zipfile.ZipInfo(path, date_time=(2026, 1, 1, 0, 0, 0))
                info.compress_type = zipfile.ZIP_DEFLATED
                info.external_attr = 0o100644 << 16
                z.writestr(info, data)
        url = artifact_base_url.rstrip("/") + "/" + urllib.parse.quote(name)
        artifacts.append({"id": "adventure-configs-" + version, "kind": "zip", "fileName": name,
                          "size": target.stat().st_size, "sha256": digest_file(target), "urls": [url],
                          "manualUrl": None, "instructions": None})
    used = {f["artifactId"] for f in files}
    missing_packages = [a["id"] for a in artifacts if a["id"] not in used]
    if missing_packages:
        raise ValueError("Packages without mapped installed content: " + ", ".join(missing_packages))
    reject_duplicates([x["path"] for x in files], "manifest")
    release = read_json(client_root / "release.json")
    manifest = {"schemaVersion": 1, "packId": "adventure-rpg", "title": "Valheim Adventure RPG",
                "version": version, "gameVersion": release.get("game_version", ""), "notes": "Pinned cooperative pack. English. Willybach Higher.",
                "publishedAt": dt.datetime.now(dt.timezone.utc).isoformat(), "artifacts": artifacts, "files": files}
    text = json.dumps(manifest, ensure_ascii=False, indent=2) + "\n"
    if str(project_root).replace("\\", "/") in text or str(client_root).replace("\\", "/") in text:
        raise ValueError("Local path leaked into manifest")
    (output / "manifest.payload.json").write_text(text, encoding="utf-8")
    report = {"version": version, "packages": len(packages), "artifacts": len(artifacts),
              "files": len(files), "installedBytes": sum(x["size"] for x in files),
              "downloadBytes": sum(x["size"] for x in artifacts), "customArtifactBytes": sum(x["size"] for x in artifacts if x["id"].startswith("adventure-configs-")),
              "manualArtifacts": [a["id"] for a in artifacts if not a["urls"]], "counts": dict(stats),
              "sources": dict(collections.Counter("manual" if not a["urls"] else urllib.parse.urlparse(a["urls"][0]).hostname for a in artifacts)),
              "snapshotMode": "live-profile-and-explicit-server-configs" if live_profile else "canonical-client-export",
              "generatedFiles": generated, "provenance": provenance}
    (output / "provenance.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return report

def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project-root", type=pathlib.Path, required=True)
    parser.add_argument("--client-root", type=pathlib.Path, required=True)
    parser.add_argument("--output", type=pathlib.Path, required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--artifact-base-url", required=True)
    parser.add_argument("--live-profile", type=pathlib.Path, help="Optional fresh profile view without copying HD; requires --server-config-root")
    parser.add_argument("--server-config-root", type=pathlib.Path, help="Explicit active server BepInEx/config directory; overlays only known client text configs")
    parser.add_argument("--allow-local-urls", action="store_true", help="Development fixtures only; never publish local source URLs")
    args = parser.parse_args(argv)
    try:
        report = build_manifest(args.project_root, args.client_root, args.output, args.version, args.artifact_base_url, args.allow_local_urls, args.live_profile, args.server_config_root)
        print(json.dumps({k: v for k, v in report.items() if k != "provenance"}, ensure_ascii=False, indent=2))
        return 0
    except (ValueError, OSError, zipfile.BadZipFile) as ex:
        print(f"Release build failed: {ex}", file=sys.stderr)
        return 1

if __name__ == "__main__":
    raise SystemExit(main())
