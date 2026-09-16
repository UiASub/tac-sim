#!/usr/bin/env bash
# Arch/CachyOS bootstrap. Run as yourself, never with sudo.
set -euo pipefail
project_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
check_only=false
build=false
for arg in "$@"; do
    case "$arg" in
        --check) check_only=true ;;
        --build) build=true ;;
        -h|--help)
            printf 'Usage: ./install.sh [--check] [--build]\nArch/CachyOS x86_64 setup; prompts before installs. --check is read-only.\n'
            exit 0 ;;
        *) printf 'Unknown option: %s\n' "$arg" >&2; exit 2 ;;
    esac
done
if [[ "$check_only" == true && "$build" == true ]]; then
    printf '%s\n' '--check and --build cannot be combined.' >&2
    exit 2
fi
if [[ "$EUID" == 0 ]]; then
    printf 'Run this script as your normal user, not root.\n' >&2
    exit 1
fi
if [[ "$(uname -s)" != Linux || "$(uname -m)" != x86_64 ]] || ! command -v pacman >/dev/null; then
    printf 'Automatic installation currently supports Arch/CachyOS x86_64 only. See docs/SETUP.md.\n' >&2
    exit 1
fi

confirm() {
    local reply
    if [[ ! -t 0 ]]; then
        printf 'Interactive confirmation needed: %s. Re-run in a terminal.\n' "$1" >&2
        return 1
    fi
    read -r -p "$1 [y/N] " reply
    [[ "$reply" == y || "$reply" == Y ]]
}

missing=()
# Hub's package supplies its desktop dependencies. Linux Mono support ships with the Linux Editor.
for package in git python rclone blender uv xdg-utils; do
    if ! pacman -Q "$package" >/dev/null 2>&1; then missing+=("$package"); fi
done
version="$(sed -n 's/^m_EditorVersion: //p' "$project_dir/Unity/ProjectSettings/ProjectVersion.txt")"
revision="$(sed -n 's/^m_EditorVersionWithRevision: .* (\([^)]*\)).*/\1/p' "$project_dir/Unity/ProjectSettings/ProjectVersion.txt")"
if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+[abfp][0-9]+$ || ! "$revision" =~ ^[0-9a-f]+$ ]]; then
    printf 'Cannot read pinned Unity version/revision.\n' >&2
    exit 1
fi
editor="${UNITY_EDITOR:-$HOME/Unity/Hub/Editor/$version/Editor/Unity}"
hub="${UNITY_HUB:-unityhub}"
if [[ "$check_only" == true ]]; then
    failed=0
    if ((${#missing[@]})); then
        printf 'Missing packages: %s\n' "${missing[*]}"
        failed=1
    fi
    if ! command -v "$hub" >/dev/null; then printf 'Missing Unity Hub\n'; failed=1; fi
    if [[ ! -x "$editor" ]]; then
        printf 'Missing Unity %s at %s (override with UNITY_EDITOR)\n' "$version" "$editor"
        failed=1
    else
        printf 'Found Unity %s: %s\n' "$version" "$editor"
    fi
    if command -v python3 >/dev/null; then
        "$project_dir/assets.sh" check || failed=1
    else
        failed=1
    fi
    printf 'Check does not validate Unity licensing, GPU drivers, or Drive authorization.\n'
    exit "$failed"
fi
if ((${#missing[@]})); then
    printf 'Will request a full system upgrade and install: %s\n' "${missing[*]}"
    confirm 'Continue with sudo pacman -Syu --needed?' || exit 1
    sudo pacman -Syu --needed "${missing[@]}"
fi
if ! command -v "$hub" >/dev/null; then
    if ! pacman -Q base-devel >/dev/null 2>&1; then
        confirm 'Install base-devel for building the Unity Hub AUR package (full system upgrade)?' || exit 1
        sudo pacman -Syu --needed base-devel
    fi
    confirm 'Download the unityhub AUR packaging for review?' || exit 1
    aur_dir="$(mktemp -d -t tac-unityhub-XXXXXX)"
    git clone https://aur.archlinux.org/unityhub.git "$aur_dir/unityhub"
    printf 'Review PKGBUILD and other packaging files at %s in another terminal.\n' "$aur_dir/unityhub"
    printf 'AUR recipes are community-maintained executable code, not official Arch packages.\n'
    confirm 'Have you reviewed the packaging and want to build/install it with makepkg -si?' || exit 1
    (cd "$aur_dir/unityhub" && makepkg -si)
    printf 'AUR build files retained at %s\n' "$aur_dir/unityhub"
fi
if [[ ! -x "$editor" ]]; then
    printf 'Unity %s is required. This is a multi-GB download.\n' "$version"
    confirm 'Download the pinned Editor through Unity Hub?' || exit 1
    # Supported by the installed Hub; if a future Hub removes this deprecated CLI, use its GUI.
    if ! "$hub" --headless install --version "$version" --changeset "$revision"; then
        printf 'Hub install failed. Open Unity Hub, sign in, and install %s manually. Then rerun.\n' "$version" >&2
        exit 1
    fi
    if [[ ! -x "$editor" ]]; then
        printf 'Hub may use a custom install location. Rerun with UNITY_EDITOR=/absolute/path/to/Editor/Unity.\n' >&2
        exit 1
    fi
fi
printf 'Preparing the locked Python/OpenCV demo environment...\n'
uv sync --project "$project_dir/automation-demo" --locked
"$project_dir/assets.sh" sync
printf '\nUnity sign-in and license activation must be completed in Unity Hub by you.\n'
printf 'Open Unity Hub with: %s\nAdd this project: %s/Unity\n' "$hub" "$project_dir"
if [[ "$build" == true ]]; then
    confirm 'Is your Unity license activated and the project closed in the Editor? Build the Linux player now?' || exit 1
    mkdir -p "$project_dir/Unity/Logs"
    "$editor" -batchmode -nographics -quit -projectPath "$project_dir/Unity" \
        -executeMethod TacSim.Editor.TrainingProject.BuildLinux \
        -logFile "$project_dir/Unity/Logs/install-build.log"
    printf 'Linux build complete. Run ./run.sh\n'
else
    printf 'Next: open the project and use TAC → Build Linux player, or rerun ./install.sh --build.\n'
fi
