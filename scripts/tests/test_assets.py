import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location("assets", Path(__file__).parents[1] / "assets.py")
assets = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(assets)
REPO = assets.ROOT


class AssetTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        for name, value in {"ROOT": self.root, "MANIFEST": self.root / "assets/manifest.json",
                            "STATE": self.root / ".asset-state.json", "RELEASE": self.root / ".asset-release.json",
                            "CONFIG": self.root / "config/rclone.conf"}.items():
            helper = patch.object(assets, name, value)
            helper.start()
            self.addCleanup(helper.stop)
        self.data = {"version": 1, "drive_folder_id": "test-folder", "files": []}
        assets.write_json(assets.MANIFEST, self.data)

    def entry(self, content=b"payload", group="source", name="rov/model.blend"):
        entry = {"group": group, "path": name, "size": len(content),
                 "sha256": assets.hashlib.sha256(content).hexdigest()}
        self.data["files"].append(entry)
        if group == "runtime":
            meta = Path(str(assets.destination(entry)) + ".meta")
            meta.parent.mkdir(parents=True, exist_ok=True)
            meta.write_text("guid: test\n")
        return entry

    def download(self, content):
        def perform(*args, **kwargs):
            self.assertEqual(args[0], "copyto")
            self.assertEqual(kwargs["folder"], "test-folder")
            Path(args[2]).write_bytes(content)
        return perform

    def test_empty_manifest_does_not_need_credentials_or_network(self):
        with patch.object(assets, "rclone", side_effect=AssertionError("network")):
            assets.fetch(self.data, "runtime")

    def test_fetch_and_offline_verification(self):
        entry = self.entry()
        with patch.object(assets, "ensure_auth"), patch.object(assets, "rclone", side_effect=self.download(b"payload")) as remote:
            assets.fetch(self.data, "source")
            self.assertEqual(remote.call_count, 1)
        with patch.object(assets, "rclone", side_effect=AssertionError("network")):
            assets.fetch(self.data, "source")
        self.assertEqual(assets.destination(entry).read_bytes(), b"payload")

    def test_checksum_mismatch_never_installs_payload(self):
        entry = self.entry()
        with patch.object(assets, "ensure_auth"), patch.object(assets, "rclone", side_effect=self.download(b"invalid")):
            with self.assertRaisesRegex(ValueError, "checksum mismatch"):
                assets.fetch(self.data, "source")
        self.assertFalse(assets.destination(entry).exists())
        self.assertFalse(assets.STATE.exists())

    def test_managed_old_version_replaced_but_local_edit_preserved(self):
        entry = self.entry()
        path = assets.destination(entry)
        path.parent.mkdir(parents=True)
        path.write_bytes(b"old version")
        assets.write_json(assets.STATE, {str(path.relative_to(self.root)): assets.digest(path)})
        with patch.object(assets, "ensure_auth"), patch.object(assets, "rclone", side_effect=self.download(b"payload")):
            assets.fetch(self.data, "source")
        path.write_bytes(b"artist's work")
        with patch.object(assets, "ensure_auth"), patch.object(assets, "rclone") as remote:
            with self.assertRaisesRegex(ValueError, "Local edits"):
                assets.fetch(self.data, "source")
            remote.assert_not_called()
        self.assertEqual(path.read_bytes(), b"artist's work")

    def test_download_failure_preserves_old_file(self):
        entry = self.entry()
        path = assets.destination(entry)
        path.parent.mkdir(parents=True)
        path.write_bytes(b"old")
        assets.write_json(assets.STATE, {str(path.relative_to(self.root)): assets.digest(path)})
        with patch.object(assets, "ensure_auth"), patch.object(assets, "rclone", side_effect=OSError("offline")):
            with self.assertRaises(OSError):
                assets.fetch(self.data, "source")
        self.assertEqual(path.read_bytes(), b"old")

    def test_runtime_meta_required_and_unlisted_payload_blocks(self):
        entry = self.entry(group="runtime", name="model.fbx")
        path = assets.destination(entry)
        Path(str(path) + ".meta").unlink()
        with self.assertRaisesRegex(ValueError, "metadata"):
            assets.verify(self.data, "runtime")
        path.write_bytes(b"payload")
        self.data["files"] = []
        with self.assertRaisesRegex(ValueError, "Unlisted"):
            assets.verify(self.data, "runtime")

    def test_invalid_paths_and_runtime_code_rejected(self):
        for name in ("../escape", "/tmp/escape", "a/../../escape", "a//b", "a\\b", ".secret"):
            with self.subTest(name=name), self.assertRaises(ValueError):
                assets.destination({"group": "source", "path": name})
        for name in ("exploit.cs", "plugin.dll", "model.fbx.meta", "scene.unity", "model.blend"):
            with self.subTest(name=name), self.assertRaises(ValueError):
                assets.destination({"group": "runtime", "path": name})

    def test_symlink_escape_rejected(self):
        (self.root / "external-assets").symlink_to(self.root.parent, target_is_directory=True)
        with self.assertRaisesRegex(ValueError, "Symlink"):
            assets.destination({"group": "source", "path": "model.blend"})

    def test_duplicate_and_invalid_hash_rejected(self):
        entry = self.entry()
        self.data["files"].append(entry.copy())
        assets.write_json(assets.MANIFEST, self.data)
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            assets.load_manifest()
        self.data["files"] = [dict(entry, sha256="bad")]
        assets.write_json(assets.MANIFEST, self.data)
        with self.assertRaisesRegex(ValueError, "checksum"):
            assets.load_manifest()

    def test_source_is_optional_for_launch(self):
        self.entry()
        self.assertEqual(assets.verify(self.data, "runtime"), [])
        self.assertEqual(len(assets.verify(self.data, "all")), 1)

    def test_publish_uploads_then_updates_manifest(self):
        path = assets.destination({"group": "source", "path": "hull.blend"})
        path.parent.mkdir(parents=True)
        path.write_bytes(b"artist source")
        with patch.object(assets, "ensure_auth"), patch.object(assets, "rclone") as remote:
            assets.publish(self.data, "source", "hull.blend")
            self.assertIn("--immutable", remote.call_args.args)
        manifest = assets.load_manifest()
        self.assertEqual(manifest["files"][0]["sha256"], assets.digest(path))

    def test_publish_failure_leaves_manifest_unchanged(self):
        path = assets.destination({"group": "source", "path": "hull.blend"})
        path.parent.mkdir(parents=True)
        path.write_bytes(b"artist source")
        before = assets.MANIFEST.read_bytes()
        with patch.object(assets, "ensure_auth"), patch.object(assets, "rclone", side_effect=OSError("no access")):
            with self.assertRaises(OSError):
                assets.publish(self.data, "source", "hull.blend")
        self.assertEqual(assets.MANIFEST.read_bytes(), before)

    def test_authorization_uses_separate_config_and_readonly_scope(self):
        def fake_rclone(*args, **kwargs):
            assets.CONFIG.write_text("fake credentials")
        with patch.object(assets, "rclone", side_effect=fake_rclone) as remote:
            assets.authorize(self.data)
        self.assertIn("drive.readonly", remote.call_args_list[0].args)
        self.assertIn("--no-output", remote.call_args_list[0].args)
        self.assertEqual(assets.CONFIG.stat().st_mode & 0o777, 0o600)

    def test_run_checks_assets_and_preserves_display_args(self):
        for name in ("run.sh", "assets.sh", "scripts/assets.py"):
            target = self.root / name
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(REPO / name, target)
        for name in ("Unity/Assets", "Unity/Packages", "Unity/ProjectSettings", "Unity/Builds/Linux"):
            (self.root / name).mkdir(parents=True, exist_ok=True)
        player = self.root / "Unity/Builds/Linux/TacSim.x86_64"
        player.write_text('#!/usr/bin/env bash\nprintf "ARG:%s\\n" "$@"\n')
        player.chmod(0o755)
        assets.write_json(assets.RELEASE, {"version": 1, "sha256": assets.release_hash([]), "files": []})
        offline_env = {**os.environ, "TAC_ASSETS_OFFLINE": "1", "XDG_SESSION_TYPE": "wayland"}
        result = subprocess.run([str(self.root / "run.sh"), "-screen-fullscreen", "0", "-automationPort", "8765"],
                                capture_output=True, text=True, env=offline_env)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("Assets verified", result.stdout)
        self.assertIn("ARG:-force-wayland", result.stdout)
        self.assertIn("ARG:8765", result.stdout)
        self.assertEqual(result.stdout.count("ARG:-screen-fullscreen"), 1)
        newer = self.root / "Unity/Assets/new-source.cs"
        newer.write_text("// changed source\n")
        os.utime(player, (1, 1))
        result = subprocess.run([str(self.root / "run.sh")], capture_output=True, text=True, env=offline_env)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("older than", result.stderr)
        self.assertNotIn("ARG:", result.stdout)
        self.entry(group="runtime", name="missing.fbx")
        assets.write_json(assets.MANIFEST, self.data)
        result = subprocess.run([str(self.root / "run.sh")], capture_output=True, text=True,
                                input="", env={**os.environ, "XDG_CONFIG_HOME": str(self.root / "no-auth")})
        self.assertNotEqual(result.returncode, 0)
        self.assertNotIn("ARG:", result.stdout)
        self.assertIn("./assets.sh auth", result.stderr)

    def test_latest_release_is_checked_on_every_online_sync(self):
        release = {"version": 1, "sha256": assets.release_hash([]), "files": []}
        with patch.object(assets, "ensure_auth"), patch.object(assets, "rclone", return_value=json.dumps(release)) as remote:
            assets.sync_latest(self.data)
            assets.sync_latest(self.data)
            self.assertEqual(remote.call_count, 2)
        with patch.object(assets, "rclone", side_effect=AssertionError("network")):
            assets.sync_latest(self.data, offline=True)

    def test_release_pointer_is_not_published_if_payload_upload_fails(self):
        entry = self.entry(group="runtime", name="model.fbx")
        assets.destination(entry).write_bytes(b"payload")
        with patch.object(assets, "ensure_auth"), patch.object(assets, "rclone", side_effect=OSError("upload failed")) as remote:
            with self.assertRaises(OSError):
                assets.publish_release(self.data)
        self.assertEqual(remote.call_count, 1)
        self.assertTrue(remote.call_args.args[2].startswith("tac-assets:objects/"))

    def test_release_hash_is_order_independent(self):
        one = self.entry(name="one.blend")
        two = self.entry(name="two.blend")
        self.assertEqual(assets.release_hash([one, two]), assets.release_hash([two, one]))

    def test_release_rejects_bad_hash_and_new_runtime_paths(self):
        with self.assertRaisesRegex(ValueError, "hash"):
            assets.release_manifest(self.data, {"version": 1, "sha256": "bad", "files": []})
        entry = {"group": "runtime", "path": "new.fbx", "sha256": "a" * 64, "size": 1}
        with self.assertRaisesRegex(ValueError, "Update Git"):
            assets.release_manifest(self.data, {"version": 1, "sha256": assets.release_hash([entry]), "files": [entry]})

    def test_latest_changed_content_downloads_and_local_edits_survive(self):
        entry = self.entry(group="runtime", name="model.fbx")
        path = assets.destination(entry)
        path.write_bytes(b"old version")
        assets.write_json(assets.STATE, {str(path.relative_to(self.root)): assets.digest(path)})
        release = {"version": 1, "sha256": assets.release_hash([entry]), "files": [entry]}
        def remote(*args, **kwargs):
            if args[0] == "cat": return json.dumps(release)
            return self.download(b"payload")(*args, **kwargs)
        with patch.object(assets, "ensure_auth"), patch.object(assets, "rclone", side_effect=remote):
            assets.sync_latest(self.data)
            self.assertEqual(path.read_bytes(), b"payload")
            path.write_bytes(b"local edit")
            with self.assertRaisesRegex(ValueError, "Local edits"):
                assets.sync_latest(self.data)
            self.assertEqual(path.read_bytes(), b"local edit")

    def test_git_ignores_payload_but_tracks_unity_metadata(self):
        subprocess.run(["git", "init", "-q", str(self.root)], check=True)
        shutil.copy2(REPO / ".gitignore", self.root / ".gitignore")
        for name, ignored in (("Unity/Assets/External/rov/hull.fbx", True),
                              ("Unity/Assets/External/rov/hull.fbx.meta", False),
                              ("Unity/Assets/External/rov.meta", False),
                              ("Unity/Assets/External.meta", False),
                              ("external-assets/source/model.blend", True),
                              (".asset-state.json", True)):
            with self.subTest(name=name):
                path = self.root / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.touch()
                result = subprocess.run(["git", "check-ignore", "-q", name], cwd=self.root)
                self.assertEqual(result.returncode, 0 if ignored else 1)

    def test_installer_check_is_readonly_and_reports_missing_packages(self):
        shutil.copy2(REPO / "install.sh", self.root / "install.sh")
        shutil.copy2(REPO / "assets.sh", self.root / "assets.sh")
        (self.root / "scripts").mkdir()
        shutil.copy2(REPO / "scripts/assets.py", self.root / "scripts/assets.py")
        settings = self.root / "Unity/ProjectSettings"
        settings.mkdir(parents=True)
        shutil.copy2(REPO / "Unity/ProjectSettings/ProjectVersion.txt", settings)
        binaries = self.root / "bin"
        binaries.mkdir()
        # No sudo or package installation can run in this read-only test.
        commands = {"pacman": '#!/bin/sh\n[ "$2" != blender ]\n',
                    "unityhub": '#!/bin/sh\nexit 0\n',
                    "Unity": '#!/bin/sh\nexit 0\n',
                    "sudo": '#!/bin/sh\nexit 99\n'}
        for name, body in commands.items():
            path = binaries / name
            path.write_text(body)
            path.chmod(0o755)
        result = subprocess.run([str(self.root / "install.sh"), "--check"], capture_output=True, text=True,
                                env={**os.environ, "PATH": f"{binaries}:{os.environ['PATH']}",
                                     "UNITY_EDITOR": str(binaries / "Unity")})
        self.assertEqual(result.returncode, 1)
        self.assertIn("Missing packages: blender", result.stdout)
        self.assertFalse(assets.STATE.exists())
        self.assertFalse(assets.CONFIG.exists())


if __name__ == "__main__":
    unittest.main()
