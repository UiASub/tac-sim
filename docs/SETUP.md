# Setup and shared assets

## Install on Arch/CachyOS

From a clone of this repository, run as your normal user:

```bash
./install.sh --check  # read-only inventory; nonzero if something is missing
./install.sh          # guided dependency and Editor installation
./install.sh --build  # also offer to build the Linux player
./run.sh
```

The installer checks Git, Python, rclone, Blender, uv, xdg-utils, Unity Hub, and
the Editor version/revision pinned in `Unity/ProjectSettings/ProjectVersion.txt`.
Missing official packages use `sudo pacman -Syu --needed` after confirmation
(this performs a full system upgrade). Missing Hub uses the `unityhub` AUR recipe:
you must review its packaging and confirm before `makepkg -si`. No unattended
sudo, license acceptance, root-run installer, or pip installation into system Python.
Unity packages are restored by Unity; `uv sync --locked` prepares the separate
OpenCV demo. A functional desktop/GPU driver is required; drivers are not replaced.

Hub downloads the pinned Editor when missing. Linux Mono build support is bundled
with the Linux Editor; this project does not require IL2CPP or Android modules.
Sign in and activate an appropriate Unity license in Hub yourself. Close the
Editor before a batch build. Override custom locations with `UNITY_EDITOR` and
`UNITY_HUB`. The Hub CLI is deprecated but works with the installed Hub; if removed
in a future version, use the GUI to install the pinned Editor and rerun.

This bootstrap supports **Arch/CachyOS x86_64**, not arbitrary operating systems.
On other systems install the equivalent tools and pinned Editor manually;
the current launcher/build target is Linux. CachyOS is not Unity's official
Ubuntu support target.

## Storage policy

| Content | Location / ownership |
| --- | --- |
| Scripts, shaders, scenes, settings, packages, small optimized art | Git, as today |
| Large runtime meshes/textures/audio/video | Drive objects → `Unity/Assets/External/` |
| Runtime `.meta` files, including folder metadata | Git; never regenerate identities per machine |
| Large Blender/CAD sources, reference photos, footage | Drive objects → `external-assets/source/` |
| Unity Library, temporary files, builds | Local, regenerable; not synced by this workflow |
| Manifest with exact filenames, sizes, SHA-256 | Git: `assets/manifest.json` |
| OAuth credentials | User config: `$XDG_CONFIG_HOME/tac-sim/rclone.conf` (default `~/.config/...`) |

All existing art stays in Git: it is small. The manifest initially has **no external
payloads**, so the current project runs without Drive access. `PLAN.md` stays untracked.
Do not blanket-ignore `.blend`/FBX files throughout the repository; put large source
work under the designated source folder. Do not mount Drive as the live Unity project.

## Authorize and download

The team's [shared Drive folder](https://drive.google.com/drive/folders/1rMqLbI4LG6reuWRAIsvz66fNWmLyx6F9)
is configured in the manifest. Your Google account needs access to it; a link alone
does not grant access. A Workspace administrator may need to allow rclone OAuth.

```bash
./assets.sh auth                       # browser sign-in, read-only access
./assets.sh fetch                      # required runtime assets only
./assets.sh fetch --group source       # optional modeling/reference files
./assets.sh fetch --group all
./assets.sh check                      # local verification only; no network
```

Authorization opens your browser; rclone prints a local URL if automatic opening
fails. On a remote/headless computer run this from a desktop terminal instead.
Google's `drive.readonly` OAuth scope is account-wide read access, **not restricted
by the folder ID**; use an appropriately scoped team account if needed. The config
is separate from your existing rclone remotes, and tokens never belong in Git.
Rerun `auth` if authorization expires. `auth --write` is only for publishers.

**OAuth longevity:** rclone 1.75 warns that its shared Google OAuth client is being
retired during 2026. Our browser authorization and folder listing worked, but for
long-term team use create your own Desktop OAuth client using
[rclone's instructions](https://rclone.org/drive/#making-your-own-client-id).
Then run `rclone --config "${XDG_CONFIG_HOME:-$HOME/.config}/tac-sim/rclone.conf" config`
and edit `tac-assets` to enter your client ID/secret and reauthorize. Retain the
folder ID and intended read-only scope. Do not paste tokens/secrets into chat or Git.
These Google Cloud/Workspace consent settings require a team owner/admin and are
not provisioned by the installer.

Every `run.sh` verifies SHA-256 against the checked-out Git manifest. If files are
missing/outdated it fetches the pinned objects, verifies them, then performs the
existing player-freshness check. Fresh downloads require rebuilding the player.
A verified checkout works offline: “synced” means matching this Git revision, not
polling whatever is newest in Drive. There is no background process or two-way sync.
Noninteractive runs fail with instructions when browser authorization is needed.

Downloads are staged and checksum-checked before replacement. Local edits are
never knowingly overwritten: move modified files aside before fetching. Unlisted
runtime payloads (including assets removed from a newer manifest) block launch;
move those aside too. Nothing is automatically deleted. Local transfer state in
`.asset-state.json` distinguishes an older downloaded version from local edits.

## Publish an asset

Publishing is explicit, never part of launch. Obtain editor permission on the Drive
folder, then authorize uploads:

```bash
./assets.sh auth --write
# Put an authored file at external-assets/source/rov/hull.blend, then:
./assets.sh publish source rov/hull.blend
# Put an export at Unity/Assets/External/rov/hull.fbx and import it in Unity, then:
./assets.sh publish runtime rov/hull.fbx
```

The command snapshots one file, uploads it to `objects/<sha256>` in Drive, then
updates the Git manifest. Hash-named objects are immutable: keep old objects so old
Git checkouts remain reproducible. Do not edit/rename them through the Drive UI.
Existing loose files in the Drive folder are not automatically imported or deleted.
This folder may also hold human-readable reference folders, but only manifest
objects are managed/downloaded by these scripts.

Review and commit the manifest, scene changes, and **all new runtime/folder `.meta`
files** together. Verify `git status --ignored` if unsure. Large runtime code,
plugins, `.unity`, `.prefab`, `.asset` and `.meta` payloads are deliberately rejected:
keep executable logic and Unity reference-bearing documents in Git. Runtime Blender
sources are also excluded: export FBX and keep `.blend` in the source group.

Close Unity before fetching or switching asset versions to avoid imports during
replacement. Coordinate editing ownership of binary source files: Drive storage
does not merge Blender changes or provide checkout locks. Builds may be uploaded
separately for distribution, but never uploaded automatically by `run.sh`.

## Verification

```bash
python3 -m unittest discover -s scripts/tests -v
bash -n install.sh assets.sh run.sh
shellcheck install.sh assets.sh run.sh
```

References: [rclone Drive](https://rclone.org/drive/),
[rclone config create](https://rclone.org/commands/rclone_config_create/),
[Unity Hub CLI](https://docs.unity.com/en-us/hub/hub-cli-reference).
