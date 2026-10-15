#!/usr/bin/env bash
# Runs on the hosted Linux runner. Only source files are sent to the test machine.
set -euo pipefail

: "${ARM32_HOST:?Set ARM32_HOST to a Tailscale IP or MagicDNS name.}"
arm32_user="${ARM32_USER:-actions-runner}"
if [[ ! "$ARM32_HOST" =~ ^[a-zA-Z0-9][a-zA-Z0-9.-]*$ || ! "$arm32_user" =~ ^[a-z_][a-zA-Z0-9_-]*$ ]]; then
  echo 'Invalid ARM32_HOST or ARM32_USER.' >&2
  exit 1
fi

script_directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
source_directory="${ARM32_SOURCE_DIRECTORY:-.}"
run_key="run-${GITHUB_RUN_ID:-local}-${GITHUB_RUN_ATTEMPT:-1}-${GITHUB_JOB:-manual}-$$"
if [[ ! "$run_key" =~ ^[a-zA-Z0-9_-]+$ ]]; then
  echo 'Invalid run identifier.' >&2
  exit 1
fi
results_directory="${GITHUB_WORKSPACE:-$PWD}/TestResults/arm32"
mkdir -p -- "$results_directory"
scratch_directory="$(mktemp -d)"
remote_directory=".cache/cankit-arm32/$run_key"
target="$arm32_user@$ARM32_HOST"
remote_prepared=false
tests_started=false
ssh_pid=''
log_pid=''
sleep_pid=''
heartbeat_open=false

# Invoked indirectly by the EXIT trap.
# shellcheck disable=SC2329
finish() {
  local exit_code=$?
  local cleanup_timeout=60s
  trap - EXIT
  trap '' INT TERM PIPE
  set +e

  if [[ -n "$sleep_pid" ]]; then
    kill "$sleep_pid" 2>/dev/null
    wait "$sleep_pid" 2>/dev/null
  fi
  # EOF tells the detached remote supervisor to stop this run, including when
  # the GitHub entry process is cancelled. Its lease also covers a broken link.
  if [[ "$heartbeat_open" == true ]]; then
    exec 3>&-
  fi
  if [[ -n "$ssh_pid" ]]; then
    for ((attempt=0; attempt<30; attempt++)); do
      kill -0 "$ssh_pid" 2>/dev/null || break
      sleep 0.1
    done
    kill "$ssh_pid" 2>/dev/null
    # Do not block cancellation waiting for an unresponsive SSH client.
  fi

  if [[ "$exit_code" == 130 || "$exit_code" == 143 ]]; then
    cleanup_timeout=2s
    # Cancellation has a short grace period. Logs already drained while the
    # SSH client was stopping; do not wait on a stalled log writer here.
    if [[ -n "$log_pid" ]]; then
      kill -KILL "$log_pid" 2>/dev/null
    fi
  elif [[ -n "$log_pid" ]]; then
    if ! wait "$log_pid" && [[ "$exit_code" == 0 ]]; then
      echo '::error::The ARM32 log could not be written completely.'
      exit_code=1
    fi
  fi

  if [[ "$tests_started" == true && "$exit_code" != 130 && "$exit_code" != 143 ]]; then
    if timeout --kill-after=2s 60s tailscale ssh "$target" "cd && cat '$remote_directory/results/Arm32TestResults.trx'" \
        > "$scratch_directory/Arm32TestResults.trx"; then
      if [[ -s "$scratch_directory/Arm32TestResults.trx" ]]; then
        if ! cp -- "$scratch_directory/Arm32TestResults.trx" "$results_directory/Arm32TestResults.trx"; then
          exit_code=1
        fi
      elif [[ "$exit_code" == 0 ]]; then
        echo '::error::The ARM32 test run returned an empty report.'
        exit_code=1
      fi
    elif [[ "$exit_code" == 0 ]]; then
      echo '::error::The ARM32 test report could not be retrieved.'
      exit_code=1
    fi
  fi

  if [[ "$remote_prepared" == true ]]; then
    # A completed run can be removed even if another run now owns the device.
    # Without its completion marker, keep the device-lock guard in case the
    # supervisor is still stopping children after losing its connection.
    timeout --kill-after=1s "$cleanup_timeout" tailscale ssh "$target" \
      "cd && if [ ! -d '$remote_directory' ]; then
         :
       elif [ -f '$remote_directory/finished' ]; then
         rm -rf -- '$remote_directory'
       else
         flock -n .cache/cankit-arm32/device.lock rm -rf -- '$remote_directory'
       fi" \
      || echo '::warning::Remote workspace retained; the test machine may still be busy.'
  fi
  rm -rf -- "$scratch_directory"
  exit "$exit_code"
}
trap finish EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
# A completed SSH process can close the heartbeat pipe between two writes.
trap '' PIPE

git -C "$source_directory" rev-parse HEAD | tee "$results_directory/commit.txt"
git -C "$source_directory" archive --format=tar --output="$scratch_directory/source.tar" HEAD

timeout 60s tailscale ssh "$target" \
  "cd && mkdir -p -- '$remote_directory/source' '$remote_directory/results'"
remote_prepared=true
timeout 5m tailscale ssh "$target" \
  "cd && tar -xf - -C '$remote_directory/source'" < "$scratch_directory/source.tar"
# Use the controller's test harness, including when testing an older PR commit.
timeout 60s tailscale ssh "$target" \
  "cd && cat > '$remote_directory/Test-SocketCanArm32.sh'" < "$script_directory/Test-SocketCanArm32.sh"
timeout 60s tailscale ssh "$target" \
  "cd && cat > '$remote_directory/Run-SocketCanArm32.py'" < "$script_directory/Run-SocketCanArm32.py"

tests_started=true
# Fork a separate remote session so killing the SSH session's entry process
# cannot kill the supervisor before it stops its children and releases the lock.
mkfifo "$scratch_directory/heartbeat" "$scratch_directory/log"
# Keep an explicit child PID so ordinary completion waits for the final bytes.
tee "$results_directory/remote.log" < "$scratch_directory/log" &
log_pid=$!
timeout --signal=TERM --kill-after=10s 41m tailscale ssh "$target" \
  "cd && setsid --fork --wait bash '$remote_directory/Test-SocketCanArm32.sh' '$remote_directory/source' '$remote_directory/results' --managed-run" \
  < "$scratch_directory/heartbeat" > "$scratch_directory/log" 2>&1 &
ssh_pid=$!
exec 3> "$scratch_directory/heartbeat"
heartbeat_open=true
while kill -0 "$ssh_pid" 2>/dev/null; do
  printf '.\n' >&3 2>/dev/null || break
  # wait is interruptible, unlike waiting on a foreground SSH pipeline.
  sleep 2 3>&- &
  sleep_pid=$!
  wait "$sleep_pid"
  sleep_pid=''
done
status=0
wait "$ssh_pid" || status=$?
ssh_pid=''
exit "$status"
