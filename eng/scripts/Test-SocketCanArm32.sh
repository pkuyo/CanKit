#!/usr/bin/env bash
# Runs on the ARM32 machine; GitHub Actions and Node.js run on the controller.
set -euo pipefail

# The supervisor owns the lock and stops the entire test process group before
# releasing it. Keep this entry point usable for direct tests on the Pi too.
if [[ "${1:-}" != --worker ]]; then
  script_directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
  exec python3 "$script_directory/Run-SocketCanArm32.py" "$@"
fi
shift
source_directory="$(realpath -e -- "${1:?Source directory is required.}")"
results_directory="$(realpath -e -- "${2:?Results directory is required.}")"
test_runner="${3:?Test runner is required.}"

# SSH sessions do not inherit the old runner service's proxy/SDK environment.
if [[ -f "$HOME/.config/cankit-arm32.env" ]]; then
  set -a
  # shellcheck source=/dev/null
  source "$HOME/.config/cankit-arm32.env"
  set +a
fi
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_NOLOGO=true
export DOTNET_CLI_TELEMETRY_OPTOUT=true
export DOTNET_CLI_UI_LANGUAGE=en
export CANKIT_TEST_ADAPTERS=CanKit.Adapter.SocketCAN
# These runs use separate source trees. Reusable build servers have no useful
# lifetime beyond this run, and must not outlive its cleanup.
export MSBUILDDISABLENODEREUSE=1
export DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1
export UseSharedCompilation=false

if [[ "$(uname -m)" != armv7l || "$(getconf LONG_BIT)" != 32 ]]; then
  echo 'These tests require the native ARM32 kernel and userspace.' >&2
  exit 1
fi
cd -- "$source_directory"
dotnet --info | tee "$results_directory/dotnet-info.txt"
if ! grep -Eq '^[[:space:]]*Architecture:[[:space:]]+arm[[:space:]]*$' "$results_directory/dotnet-info.txt"; then
  echo 'The .NET host must use ARM32.' >&2
  exit 1
fi
for iface in vcan0 vcan1 vcan2; do
  ip -details link show "$iface" | grep -qw vcan
done

dotnet restore CanKitAdapters.slnf
if [[ "$test_runner" == Microsoft.Testing.Platform ]]; then
  dotnet test --solution CanKitAdapters.slnf -c Release -f net8.0 --no-restore \
    --results-directory "$results_directory" --report-trx --report-trx-filename Arm32TestResults.trx
else
  # Reviewed commits from before the MTP migration still use VSTest.
  dotnet test CanKitAdapters.slnf -c Release -f net8.0 --no-restore \
    --results-directory "$results_directory" --logger 'trx;LogFileName=Arm32TestResults.trx'
fi
