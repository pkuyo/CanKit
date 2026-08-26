# Contributing to CanKit

Thanks for helping! This project aims to provide a single, clean C# API for CAN 2.0 and CAN-FD across multiple vendors.

## Quick start (build & run)

- Prereqs: .NET SDK 10.x (pinned by `global.json` with `rollForward: latestFeature`). SDK 10 can still build and test the `net8.0` target.
- Build: `dotnet build`
- List available endpoints:  
  `dotnet run --project samples/CanKit.Sample.ListEndpoints`
- Sniff frames on an endpoint:  
  `dotnet run --project samples/CanKit.Sample.Sniffer -- --endpoint <your-endpoint> --bitrate 500000`
- Loopback demo (no hardware):  
  `dotnet run --project samples/CanKit.Sample.QuickStartTxRx -- --src virtual://alpha/0 --dst virtual://alpha/1 --count 5`

> The samples accept flags like `--scheme`, `--fd`, `--brs`, `--bitrate`, etc. See their `Program.cs` for full usage.

## Target frameworks

Library packages multi-target `netstandard2.0;net8.0;net8.0-windows;net10.0;net10.0-windows`. `net8.0` stays until its EOL (November 2026).

Tests run on `net8.0` and `net10.0`. The `net48` test target is kept on purpose for Windows hardware tests against .NET Framework (vendor SDKs and classic Windows CAN stacks). It is not a library TFM.

C# language version is 14, coupled to the SDK 10 pin.

## Filing issues

- Use the **Bug report** or **Compatibility test report** templates.
- For questions, please use **Discussions**.

## Coding

- Follow C# conventions. Keep public API minimal and well-documented.
- Prefer small, focused PRs.
- Use **Conventional Commits** in PR titles (e.g., `feat(core): add periodic TX API`).

# Tests

* **Run unit/integration tests:** `dotnet test` (defaults to every test TFM). Hardware-free work is covered by `net8.0` / `net10.0`; use `-f net48` on Windows when exercising .NET Framework hardware tests.
  To run tests for a modified adapter, set `CANKIT_TEST_ADAPTERS` to the adapter’s project name.


* **Adapter-specific dependencies:** Some adapter tests may require the vendor SDK and/or real hardware to be installed and connected. Test packages stay on xunit v2 (`2.9.x`) and FluentAssertions 7.x; FluentAssertions v8 is commercial (Xceed) for commercial use.

* **Configurations:** You can run adapter tests with a **fake configuration** (no hardware). When conditions allow, prefer running in **Release** configuration against real hardware for end-to-end validation:

  ```bash
  dotnet test -c Release
  ```


## Areas / labels

- `area: core`, `area: pcan`, `area: kvaser`, `area: socketcan`, `area: zlg`
