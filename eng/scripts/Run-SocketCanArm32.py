#!/usr/bin/env python3
"""Own the device lock and lifetime of one native ARM32 test run.

Requires Linux and Python 3 (fcntl and waitid/WNOWAIT). The SSH entry point
also requires Bash and util-linux setsid with --fork/--wait support.
"""

import argparse
import fcntl
import json
import os
from pathlib import Path
import re
import selectors
import shutil
import signal
import subprocess
import sys
import time

LOCK_WAIT_SECONDS = 300


class RunInterrupted(Exception):
    def __init__(self, message, exit_code):
        super().__init__(message)
        self.exit_code = exit_code


def test_runner(source):
    path = source / "global.json"
    if not path.exists():
        return "VSTest"
    # Match strings first so comment markers and commas inside strings survive.
    quoted = r'"(?:\\.|[^"\\])*"'
    text = path.read_text(encoding="utf-8-sig")
    text = re.sub(quoted + r"|//[^\r\n]*|/\*[\s\S]*?\*/",
                  lambda m: m[0] if m[0].startswith('"') else re.sub(r"[^\r\n]", " ", m[0]), text)
    text = re.sub(quoted + r"|,(?=\s*[}\]])",
                  lambda m: m[0] if m[0].startswith('"') else "", text)
    try:
        config = json.loads(text)
        if not isinstance(config, dict) or not isinstance(config.get("test", {}), dict):
            raise ValueError("the root and test properties must be objects")
        runner = config.get("test", {}).get("runner", "VSTest")
        if runner not in ("VSTest", "Microsoft.Testing.Platform"):
            raise ValueError(f"unsupported test.runner: {runner!r}")
        return runner
    except ValueError as error:
        raise ValueError(f"Invalid global.json: {error}") from error


def stop_process_group(child):
    # Do not reap the leader until after signalling its group: the unreaped
    # child PID cannot be reused for an unrelated process group.
    try:
        os.killpg(child.pid, signal.SIGTERM)
        time.sleep(1)
        os.killpg(child.pid, signal.SIGKILL)
    except ProcessLookupError:
        pass
    finally:
        child.wait()


def run(source, results, managed):
    cache = Path.home() / ".cache/cankit-arm32"
    cache.mkdir(parents=True, exist_ok=True)
    source = source.resolve(strict=True)
    results = results.resolve(strict=True)
    run_directory = source.parent
    if managed and not (
        run_directory.parent == cache.resolve()
        and re.fullmatch(r"run-[a-zA-Z0-9_-]+", run_directory.name)
        and source.name == "source" and results == run_directory / "results"
    ):
        raise ValueError("Managed runs must use a dedicated run-* directory under the ARM32 cache")

    stop_signal = None

    def interrupted(signum, _frame):
        nonlocal stop_signal
        stop_signal = signum

    for signum in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP):
        signal.signal(signum, interrupted)
    deadline = time.monotonic() + 40 * 60
    last_heartbeat = time.monotonic()
    heartbeat = selectors.DefaultSelector()
    if managed:
        heartbeat.register(sys.stdin, selectors.EVENT_READ)

    def check_connection():
        nonlocal last_heartbeat
        if stop_signal is not None:
            raise RunInterrupted("ARM32 test run interrupted", 128 + stop_signal)
        now = time.monotonic()
        if now >= deadline:
            raise RunInterrupted("ARM32 test run exceeded 40 minutes", 124)
        if managed:
            if heartbeat.select(timeout=0):
                if not os.read(sys.stdin.fileno(), 4096):
                    raise RunInterrupted("ARM32 controller disconnected", 130)
                last_heartbeat = now
            if now - last_heartbeat >= 30:
                raise RunInterrupted("ARM32 controller heartbeat expired", 124)

    child = None
    cancelled = False
    # Python opens this close-on-exec; close_fds below is an additional guard.
    with (cache / "device.lock").open("a") as lock:
        try:
            lock_deadline = time.monotonic() + LOCK_WAIT_SECONDS
            while True:
                check_connection()
                try:
                    fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
                    break
                except BlockingIOError:
                    if time.monotonic() >= lock_deadline:
                        raise RuntimeError("Another ARM32 test run is still using the vcan interfaces")
                    time.sleep(0.1)
            runner = test_runner(source)
            harness = Path(__file__).with_name("Test-SocketCanArm32.sh")
            child = subprocess.Popen(
                ["bash", str(harness), "--worker", str(source), str(results), runner],
                stdin=subprocess.DEVNULL, start_new_session=True, close_fds=True,
            )
            while True:
                status = os.waitid(os.P_PID, child.pid, os.WEXITED | os.WNOHANG | os.WNOWAIT)
                if status is not None:
                    return status.si_status if status.si_code == os.CLD_EXITED else 128 + status.si_status
                check_connection()
                time.sleep(0.1)
        except RunInterrupted as error:
            cancelled = True
            print(str(error), file=sys.stderr, flush=True)
            return error.exit_code
        finally:
            # Finish terminating this run even if another cancellation arrives.
            for signum in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP):
                signal.signal(signum, signal.SIG_IGN)
            if child is not None:
                stop_process_group(child)
            heartbeat.close()
            if managed:
                if cancelled or child is None:
                    # No test report is expected if the worker never started,
                    # including when another run holds the device lock too long.
                    shutil.rmtree(run_directory)
                else:
                    # Keep reports for download. Publish completion only after
                    # every test process has been stopped and source cleanup ends.
                    shutil.rmtree(source)
                    (run_directory / "finished").touch()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source_directory", type=Path)
    parser.add_argument("results_directory", type=Path)
    parser.add_argument("--managed-run", action="store_true")
    args = parser.parse_args()
    try:
        return run(args.source_directory, args.results_directory, args.managed_run)
    except (OSError, ValueError, RuntimeError) as error:
        print(f"ARM32 test run failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
