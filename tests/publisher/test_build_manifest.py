import hashlib
import json
import pathlib
import sys
import tempfile
import unittest
import zipfile

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[2] / "publisher"))
import build_manifest as builder

def sha(data):
    return hashlib.sha256(data).hexdigest()

class ReleaseBuilderTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = pathlib.Path(self.tmp.name)
        self.project = self.root / "project"
        self.client = self.root / "client"
        self.output = self.root / "output"
        self.project.mkdir()
        self.client.mkdir()
        archive = self.project / "Example-Mod-1.0.0.zip"
        with zipfile.ZipFile(archive, "w") as z:
            z.writestr("Example.dll", b"pinned DLL fixture")
            z.writestr("README.md", b"upstream readme")
        self.package = {
            "full_name": "Example-Mod-1.0.0", "namespace": "Example",
            "archive": str(archive), "sha256": builder.digest_file(archive),
            "download_url": "https://example.org/mod/1.0.0.zip",
            "installed_files": ["BepInEx/plugins/Example/Example.dll"],
        }
        self.files = {
            "BepInEx/plugins/Example/Example.dll": b"pinned DLL fixture",
            "BepInEx/config/example.cfg": b"[General]\n# Setting type: KeyboardShortcut\nToggle = F8\nHealth = 120\n",
            "release.json": b'{"game_version":"1.0.20"}',
        }
        self.write_fixture()

    def tearDown(self):
        self.tmp.cleanup()

    def write_fixture(self):
        inventory = []
        for path, data in self.files.items():
            target = self.client / path
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
            inventory.append({"path": path, "bytes": len(data), "sha256": sha(data)})
        (self.client / "client-files.json").write_text(json.dumps(inventory), encoding="utf-8")
        (self.project / "installed.lock.json").write_text(json.dumps([self.package]), encoding="utf-8")

    def build(self):
        return builder.build_manifest(self.project, self.client, self.output, "1.0.0", "https://example.org/releases/1.0.0")

    def test_pinned_file_maps_to_original_archive_and_only_configs_are_bundled(self):
        report = self.build()
        manifest = json.loads((self.output / "manifest.payload.json").read_text())
        dll = next(f for f in manifest["files"] if f["path"].endswith(".dll"))
        self.assertEqual("Example-Mod-1.0.0", dll["artifactId"])
        self.assertEqual("Example.dll", dll["entry"])
        cfg = next(f for f in manifest["files"] if f["path"].endswith(".cfg"))
        self.assertEqual("config", cfg["policy"])
        self.assertEqual(["General/Toggle"], cfg["preserveKeys"])
        with zipfile.ZipFile(self.output / "adventure-configs-1.0.0.zip") as z:
            self.assertNotIn("BepInEx/plugins/Example/Example.dll", z.namelist())
            self.assertIn("BepInEx/config/example.cfg", z.namelist())
        self.assertEqual(2, report["artifacts"])
        self.assertNotIn(str(self.project), (self.output / "manifest.payload.json").read_text())

    def test_unknown_binary_is_not_silently_republished(self):
        self.files["BepInEx/plugins/unknown.dll"] = b"not in a pinned archive"
        self.write_fixture()
        with self.assertRaisesRegex(ValueError, "Unmapped binaries"):
            self.build()

    def test_corrupt_archive_is_rejected(self):
        pathlib.Path(self.package["archive"]).write_bytes(b"corrupted archive")
        with self.assertRaisesRegex(ValueError, "SHA256 mismatch"):
            self.build()

    def test_traversal_windows_ads_reserved_and_case_collision_are_rejected(self):
        for value in ("../x", "/x", "C:/x", "a/../x", "a/x:stream", "a/CON.txt", "a./x", "a//x"):
            with self.subTest(value=value), self.assertRaises(ValueError):
                builder.safe_path(value)
        with self.assertRaisesRegex(ValueError, "duplicate"):
            builder.reject_duplicates(["BepInEx/a.dll", "bepinex/A.dll"], "fixture")

    def test_overlay_must_not_leak_host_paths_or_credentials(self):
        for value in (b"Path = C:\\Users\\example\\Downloads\n", b'{"refresh_token":"secret"}', b"Password = hidden"):
            with self.subTest(value=value), self.assertRaises(ValueError):
                builder.assert_publishable_text(value, "settings.cfg")
        self.assertIn("Password", builder.assert_publishable_text(b"Password = \n", "settings.cfg"))

    def test_only_exact_generated_hud_files_are_excluded(self):
        self.files["BepInEx/config/Neobotics/HUDCompass/center.png"] = b"generated default fixture"
        self.write_fixture()
        report = self.build()
        self.assertEqual(1, len(report["generatedFiles"]))
        self.assertEqual("Neobotics-HUDCompass", report["generatedFiles"][0]["regenerated_by"])
        self.files["BepInEx/config/Neobotics/HUDCompass/custom.png"] = b"user custom image"
        self.write_fixture()
        with self.assertRaisesRegex(ValueError, "custom.png"):
            self.build()

    def test_insecure_url_requires_explicit_development_flag(self):
        for value in ("http://example.org/mod.zip", "file:///C:/test.zip", "https://user:password@example.org/x"):
            with self.subTest(value=value), self.assertRaises(ValueError):
                builder.validate_public_url(value)
        builder.validate_public_url("http://127.0.0.1:8800/test.zip", allow_local=True)

    def test_live_snapshot_reads_fresh_config_and_explicit_server_overlay(self):
        import shutil
        live = self.root / "live"
        server = self.root / "active-server-config"
        shutil.copytree(self.client / "BepInEx", live / "BepInEx")
        server.mkdir()
        (live / "BepInEx/config/example.cfg").write_bytes(b"[General]\nHealth = 130\n")
        (server / "example.cfg").write_bytes(b"[General]\nHealth = 140\n")
        (server / "new-player-private.json").write_text('{"password":"never publish"}')
        report = builder.build_manifest(self.project, self.client, self.output, "1.0.0", "https://example.org/releases/1.0.0", live_profile=live, server_config_root=server)
        with zipfile.ZipFile(self.output / "adventure-configs-1.0.0.zip") as z:
            self.assertEqual(b"[General]\nHealth = 140\n", z.read("BepInEx/config/example.cfg"))
            self.assertNotIn("BepInEx/config/new-player-private.json", z.namelist())
        self.assertEqual("live-profile-and-explicit-server-configs", report["snapshotMode"])

    def test_live_snapshot_requires_both_explicit_inputs(self):
        with self.assertRaisesRegex(ValueError, "both --live-profile"):
            builder.build_manifest(self.project, self.client, self.output, "1.0.0", "https://example.org/x", live_profile=self.client)

    def test_only_five_explicit_client_config_types_preserve_all_existing_keys(self):
        for name in builder.PERSONAL_CONFIGS:
            policy, keys = builder.config_policy("BepInEx/config/" + name, "[UI]\nCustom = 2\n")
            self.assertEqual("config", policy)
            self.assertEqual(["UI/Custom"], keys)
        _, gameplay_keys = builder.config_policy("BepInEx/config/creaturelevelcontrol.cfg", "[Balance]\nHealth = 120\n")
        self.assertEqual([], gameplay_keys)

if __name__ == "__main__":
    unittest.main()
