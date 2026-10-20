# ARM32 SocketCAN CI

Runs the SocketCAN tests on the ARM32 Raspberry Pi from a hosted GitHub runner
over Tailscale SSH. The same build of this tool runs on both sides:

- `controller` (hosted runner): uploads `git archive HEAD` and the tool to
  `~/.cache/cankit-arm32/run-*` on the Pi, streams the log, downloads
  `Arm32TestResults.trx` and removes the run directory.
- `supervise` (Pi): takes `~/.cache/cankit-arm32/device.lock`, runs the worker
  in its own process group and kills that group when the run ends. A run stops
  after 40 minutes, or when the controller disconnects or sends no heartbeat
  for 30 seconds.
- `worker` (Pi): checks ARM32 and `vcan0`–`vcan2`, then runs
  `dotnet test CanKitAdapters.slnf -c Release -f net8.0`.

## Requirements

- Repository secrets: `ARM32_HOST` (Tailscale address), `TS_ARM32_CLIENT_ID`,
  `TS_ARM32_AUDIENCE`, and optionally `ARM32_USER` (default `actions-runner`).
- Pi: .NET 10, the SDK/runtime needed by the tested commit, util-linux `setsid`
  and `flock`, and `vcan0`–`vcan2`. Proxy or `DOTNET_ROOT` settings go in
  `~/.config/cankit-arm32.env`, which `Start-Arm32Ci.sh` loads.

## Running manually

```bash
dotnet publish eng/CanKit.Arm32Ci -c Release -o artifacts/arm32-ci
ARM32_HOST=<tailscale-host> dotnet artifacts/arm32-ci/CanKit.Arm32Ci.dll controller
```

On the Pi, `bash Start-Arm32Ci.sh supervise <source> <results>` tests an existing
checkout and keeps both directories.
