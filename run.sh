#!/usr/bin/env bash
set -euo pipefail

project_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
player="$project_dir/Unity/Builds/Linux/TacSim.x86_64"
if [[ ! -x "$player" ]]; then
    printf 'Build the Linux player first: TAC → Build Linux player in Unity.\n' >&2
    exit 1
fi

# Use a native compositor window on Wayland, including niri.
window_args=(-screen-fullscreen 1)
if [[ "${XDG_SESSION_TYPE:-}" == wayland || -n "${WAYLAND_DISPLAY:-}" ]]; then
    window_args+=(-force-wayland)
fi
exec "$player" "${window_args[@]}" "$@"
