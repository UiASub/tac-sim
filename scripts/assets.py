#!/usr/bin/env python3
"""Pinned, one-way Drive downloads. Never mirror/delete or overwrite local edits."""
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT / "assets/manifest.json"
ROOTS = {"runtime": "Unity/Assets/External", "source": "external-assets/source"}
CONFIG = Path(os.environ.get("XDG_CONFIG_HOME", str(Path.home() / ".config"))) / "tac-sim/rclone.conf"
STATE = ROOT / ".asset-state.json"
RELEASE = ROOT / ".asset-release.json"


def digest(path):
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(mode="w", dir=path.parent, delete=False) as stream:
        temporary = Path(stream.name)
        json.dump(value, stream, indent=2)
        stream.write("\n")
    os.replace(temporary, path)


def destination(entry):
    group, name = entry["group"], entry["path"]
    if group not in ROOTS or not isinstance(name, str):
        raise ValueError("Invalid asset group or path")
    relative = PurePosixPath(name)
    if (relative.is_absolute() or not relative.parts or str(relative) != name
            or any(part in (".", "..") or part.startswith(".") for part in relative.parts)
            or "\\" in name or any(ord(c) < 32 for c in name)):
        raise ValueError(f"Unsafe asset path: {name!r}")
    # Metadata and executable project logic must remain reviewed in Git.
    allowed = {".fbx", ".obj", ".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff",
               ".exr", ".hdr", ".wav", ".ogg", ".mp4"}
    if group == "runtime" and relative.suffix.lower() not in allowed:
        raise ValueError(f"Unsupported runtime payload: {name}; keep code and .meta files in Git")
    path = ROOT / ROOTS[group] / name
    # Reject symlinks anywhere along the destination, including managed roots.
    current = ROOT
    for part in path.relative_to(ROOT).parts:
        current /= part
        if current.is_symlink():
            raise ValueError(f"Symlink not allowed in asset destination: {current}")
    return path


def load_manifest():
    data = json.loads(MANIFEST.read_text())
    return validate_manifest(data)


def validate_manifest(data):
    if data.get("version") != 1 or not isinstance(data.get("files"), list):
        raise ValueError("Unsupported asset manifest")
    folder = data.get("drive_folder_id", "")
    if not isinstance(folder, str) or (folder and not re.fullmatch(r"[\w-]+", folder)):
        raise ValueError("drive_folder_id must be a Google Drive folder ID, not a URL")
    seen = set()
    for entry in data["files"]:
        path = destination(entry)
        if path in seen:
            raise ValueError(f"Duplicate manifest destination: {path}")
        seen.add(path)
        if not re.fullmatch(r"[0-9a-f]{64}", entry["sha256"]):
            raise ValueError(f"Invalid checksum: {path}")
        if type(entry["size"]) is not int or entry["size"] < 0:
            raise ValueError(f"Invalid size: {path}")
    return data


def release_hash(files):
    canonical = json.dumps(sorted(files, key=lambda e: (e["group"], e["path"])),
                           sort_keys=True, separators=(",", ":")).encode()
    return hashlib.sha256(canonical).hexdigest()


def release_manifest(data, release):
    if release.get("version") != 1 or release.get("sha256") != release_hash(release["files"]):
        raise ValueError("Runtime release hash is invalid")
    result = validate_manifest({**data, "files": release["files"]})
    # New paths / identities require a Git update; Drive can only replace approved payloads.
    approved = {e["path"] for e in entries_for(data, "runtime")}
    if any(e["group"] != "runtime" for e in result["files"]) or {e["path"] for e in result["files"]} != approved:
        raise ValueError("Drive release requires different runtime assets. Update Git before launching.")
    return result


def sync_latest(data, offline=False):
    if offline:
        if not RELEASE.exists():
            raise ValueError("No verified release cached. Run ./assets.sh sync online first")
        release = json.loads(RELEASE.read_text())
    else:
        ensure_auth(data)
        raw = rclone("cat", "tac-assets:latest.json", "--head", "1048577",
                     "--retries", "1", "--contimeout", "10s", "--timeout", "30s",
                     folder=data["drive_folder_id"], capture=True)
        if len(raw.encode()) > 1048576:
            raise ValueError("Runtime release index is too large")
        release = json.loads(raw)
    current = release_manifest(data, release)
    print(f"Runtime release: {release['sha256'][:16]} ({'offline cache' if offline else 'latest on Drive'})")
    if offline:
        if verify(current, "runtime"):
            raise ValueError("Cached runtime assets are missing/modified; reconnect and sync")
        print("Assets verified (offline runtime).")
    else:
        fetch(current, "runtime")
        write_json(RELEASE, release)


def publish_release(data):
    if verify(data, "runtime"):
        raise ValueError("Runtime files do not match the manifest; publish changed files first")
    ensure_auth(data)
    files = entries_for(data, "runtime")
    # Make all immutable payloads available before replacing the small release pointer.
    for entry in files:
        path = destination(entry)
        with tempfile.TemporaryDirectory(prefix="tac-release-") as directory:
            snapshot = Path(directory) / "payload"
            shutil.copyfile(path, snapshot)
            if digest(snapshot) != entry["sha256"]:
                raise ValueError(f"Asset changed during release: {path}")
            rclone("copyto", str(snapshot), f"tac-assets:objects/{entry['sha256']}",
                   "--immutable", "--checksum", folder=data["drive_folder_id"])
    release = {"version": 1, "sha256": release_hash(files), "files": files}
    with tempfile.TemporaryDirectory(prefix="tac-release-") as directory:
        index = Path(directory) / "latest.json"
        write_json(index, release)
        rclone("copyto", str(index), "tac-assets:latest.json", folder=data["drive_folder_id"])
    print(f"Published runtime release {release['sha256']}")


def rclone(*args, folder=None, capture=False):
    if not shutil.which("rclone"):
        raise ValueError("rclone is missing; run ./install.sh")
    command = ["rclone", "--config", str(CONFIG)]
    if folder:
        command += ["--drive-root-folder-id", folder]
    result = subprocess.run(command + list(args), check=True, text=True,
                            stdout=subprocess.PIPE if capture else None)
    return result.stdout or ""


def authorize(data, write=False):
    if not data["drive_folder_id"]:
        raise ValueError("Set drive_folder_id in assets/manifest.json to the shared folder ID first")
    print("Authorize rclone in your browser using an account with access to the shared folder.")
    print("Google's read-only scope can read all Drive files your account can access; the folder setting is not a security boundary.")
    if write:
        print("Publisher authorization grants Drive write access. Only use this for publishing assets.")
    CONFIG.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
    # Dedicated config; do not touch any existing user rclone remotes or print tokens.
    old_umask = os.umask(0o077)
    try:
        rclone("config", "create", "tac-assets", "drive",
               "scope", "drive" if write else "drive.readonly",
               "root_folder_id", data["drive_folder_id"],
               "config_is_local", "true", "--no-output")
    finally:
        os.umask(old_umask)
        if CONFIG.exists():
            CONFIG.chmod(0o600)
    CONFIG.chmod(0o600)
    rclone("lsf", "tac-assets:", "--max-depth", "1", folder=data["drive_folder_id"], capture=True)
    print("Drive authorization ready. Credentials are stored outside the repository.")


def ensure_auth(data):
    if CONFIG.exists():
        return
    if sys.stdin.isatty() and input("Missing assets need Drive access. Open browser authorization? [y/N] ").lower() == "y":
        authorize(data)
    else:
        raise ValueError("Run ./assets.sh auth in a terminal to authorize downloads, then retry")


def entries_for(data, group):
    return [e for e in data["files"] if group == "all" or e["group"] == group]


def verify(data, group):
    entries = entries_for(data, group)
    missing = []
    expected = {destination(e) for e in data["files"] if e["group"] == "runtime"}
    if group in ("runtime", "all"):
        base = ROOT / ROOTS["runtime"]
        if base.exists():
            for path in base.rglob("*"):
                if path.is_symlink():
                    raise ValueError(f"Symlink in runtime assets: {path}")
                if path.is_file() and path.suffix != ".meta" and path not in expected:
                    raise ValueError(f"Unlisted runtime asset: {path}. Move it outside Assets or publish it; nothing was deleted.")
    for entry in entries:
        path = destination(entry)
        if entry["group"] == "runtime" and not Path(str(path) + ".meta").is_file():
            raise ValueError(f"Missing Git-managed Unity metadata: {path}.meta")
        if not path.is_file() or path.stat().st_size != entry["size"] or digest(path) != entry["sha256"]:
            missing.append(entry)
    return missing


def fetch(data, group):
    missing = verify(data, group)
    if not missing:
        print(f"Assets verified ({group}); no downloads needed.")
        return
    if not data["drive_folder_id"]:
        raise ValueError("Asset manifest has no Drive folder ID")
    ensure_auth(data)
    state = json.loads(STATE.read_text()) if STATE.exists() else {}
    for entry in missing:
        path = destination(entry)
        key = str(path.relative_to(ROOT))
        previous = digest(path) if path.is_file() else None
        if path.exists() and (previous is None or previous != state.get(key)):
            raise ValueError(f"Local edits/unmanaged file at {path}; move it aside before fetching. Nothing was overwritten.")
        print(f"Downloading {key} ({entry['size']} bytes)", flush=True)
        with tempfile.TemporaryDirectory(prefix="tac-assets-") as directory:
            temporary = Path(directory) / "payload"
            rclone("copyto", f"tac-assets:objects/{entry['sha256']}", str(temporary),
                   "--retries", "2", "--contimeout", "15s", "--timeout", "60s", folder=data["drive_folder_id"])
            if temporary.stat().st_size != entry["size"] or digest(temporary) != entry["sha256"]:
                raise ValueError(f"Downloaded checksum mismatch: {key}; original left untouched")
            destination(entry)  # Recheck symlink safety after download.
            if (digest(path) if path.is_file() else None) != previous:
                raise ValueError(f"Asset changed during download: {key}; original left untouched")
            path.parent.mkdir(parents=True, exist_ok=True)
            # Atomic replacement on destination filesystem; fresh mtime invalidates old player builds.
            with tempfile.NamedTemporaryFile(dir=path.parent, prefix=".download-", delete=False) as output:
                staged = Path(output.name)
                with temporary.open("rb") as source:
                    shutil.copyfileobj(source, output)
            try:
                os.replace(staged, path)
            finally:
                staged.unlink(missing_ok=True)
        state[key] = entry["sha256"]
        write_json(STATE, state)
    if verify(data, group):
        raise ValueError("Assets changed during verification; retry")
    print("Assets downloaded and verified.")


def publish(data, group, name):
    entry = {"group": group, "path": name}
    path = destination(entry)
    if not path.is_file():
        raise ValueError(f"Missing file: {path}")
    if group == "runtime" and not Path(str(path) + ".meta").is_file():
        raise ValueError("Import the runtime asset in Unity first, then track its .meta in Git")
    if not data["drive_folder_id"]:
        raise ValueError("Set drive_folder_id in assets/manifest.json first")
    ensure_auth(data)
    # Snapshot avoids uploading content that changes beneath the hash calculation.
    with tempfile.TemporaryDirectory(prefix="tac-publish-") as directory:
        snapshot = Path(directory) / "payload"
        shutil.copyfile(path, snapshot)
        entry.update(sha256=digest(snapshot), size=snapshot.stat().st_size)
        rclone("copyto", str(snapshot), f"tac-assets:objects/{entry['sha256']}",
               "--immutable", "--checksum", folder=data["drive_folder_id"])
    data["files"] = [e for e in data["files"] if (e["group"], e["path"]) != (group, name)] + [entry]
    write_json(MANIFEST, data)
    state = json.loads(STATE.read_text()) if STATE.exists() else {}
    state[str(path.relative_to(ROOT))] = entry["sha256"]
    write_json(STATE, state)
    print("Published. Review and commit assets/manifest.json and any Unity .meta files; payload stays out of Git.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    auth = sub.add_parser("auth", help="Browser authorization (read-only by default)")
    auth.add_argument("--write", action="store_true", help="Grant publisher upload access")
    for command in ("check", "fetch"):
        child = sub.add_parser(command)
        child.add_argument("--group", choices=["runtime", "source", "all"], default="runtime")
    upload = sub.add_parser("publish", help="Explicitly upload one immutable object and update the manifest")
    upload.add_argument("group", choices=list(ROOTS))
    upload.add_argument("path", help="Path relative to the group's local directory")
    sync = sub.add_parser("sync", help="Check latest runtime release on Drive and download changes")
    sync.add_argument("--offline", action="store_true", help="Explicitly use the last verified release without network")
    sub.add_parser("release", help="Publish the current runtime manifest as latest on Drive")
    args = parser.parse_args()
    try:
        data = load_manifest()
        if args.command == "auth":
            authorize(data, args.write)
        elif args.command == "fetch":
            fetch(data, args.group)
        elif args.command == "publish":
            publish(data, args.group, args.path)
        elif args.command == "sync":
            sync_latest(data, args.offline)
        elif args.command == "release":
            publish_release(data)
        else:
            missing = verify(data, args.group)
            if missing:
                raise ValueError("Missing/outdated assets: " + ", ".join(e["path"] for e in missing)
                                 + f". Run ./assets.sh fetch --group {args.group}")
            print(f"Assets verified ({args.group}).")
    except (ValueError, OSError, KeyError, TypeError, subprocess.CalledProcessError) as error:
        print(f"Asset error: {error}", file=sys.stderr)
        print("For expired/denied Drive access, run ./assets.sh auth and check folder permissions.", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
