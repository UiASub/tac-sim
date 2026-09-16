#!/usr/bin/env bash
set -euo pipefail

project_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
"$project_dir/assets.sh" fetch
player="$project_dir/Unity/Builds/Linux/TacSim.x86_64"
build_marker="$project_dir/Unity/Builds/Linux/.source-build-time"
if [[ ! -x "$player" ]]; then
    printf 'Build the Linux player first: TAC → Build Linux player in Unity.\n' >&2
    exit 1
fi

freshness_reference="$player"
if [[ -f "$build_marker" ]]; then
    freshness_reference="$build_marker"
fi
newer_source="$(find "$project_dir/Unity/Assets" "$project_dir/Unity/Packages" \
    "$project_dir/Unity/ProjectSettings" -type f -newer "$freshness_reference" -print -quit)"
if [[ -n "$newer_source" ]]; then
    printf 'The Linux player is older than %s. Rebuild it with TAC → Build Linux player.\n' \
        "${newer_source#"$project_dir/"}" >&2
    exit 1
fi

# Use a native compositor window on Wayland, including niri.
window_args=()
fullscreen_set=false
for argument in "$@"; do
    if [[ "$argument" == -screen-fullscreen ]]; then
        fullscreen_set=true
        break
    fi
done
if [[ "$fullscreen_set" == false ]]; then
    window_args+=(-screen-fullscreen 1)
fi
if [[ "${XDG_SESSION_TYPE:-}" == wayland || -n "${WAYLAND_DISPLAY:-}" ]]; then
    window_args+=(-force-wayland)
fi
exec "$player" "${window_args[@]}" "$@"
