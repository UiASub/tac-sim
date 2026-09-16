#!/usr/bin/env bash
set -euo pipefail
project_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
if ! command -v python3 >/dev/null; then
    printf 'Python 3 is required. Run ./install.sh first.\n' >&2
    exit 1
fi
exec python3 "$project_dir/scripts/assets.py" "$@"
