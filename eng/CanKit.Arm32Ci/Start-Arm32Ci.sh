#!/usr/bin/env bash
set -euo pipefail

# Preserve the machine's existing shell-based SDK/proxy configuration.
if [[ -f "$HOME/.config/cankit-arm32.env" ]]; then
  set -a
  # shellcheck source=/dev/null
  source "$HOME/.config/cankit-arm32.env"
  set +a
fi
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH"
exec dotnet "$(dirname -- "${BASH_SOURCE[0]}")/CanKit.Arm32Ci.dll" "$@"
