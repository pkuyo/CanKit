# Contributing to CanKit

Thanks for helping! This project aims to provide a single, clean C# API for CAN 2.0 and CAN-FD across multiple vendors.

## Quick start (build & run)

- Prereqs: .NET SDK 8.x
- Build: `dotnet build`
- List available endpoints:  
  `dotnet run --project samples/CanKit.Sample.ListEndpoints`
- Sniff frames on an endpoint:  
  `dotnet run --project samples/CanKit.Sample.Sniffer -- --endpoint <your-endpoint> --bitrate 500000`
- Loopback demo (no hardware):  
  `dotnet run --project samples/CanKit.Sample.QuickStartTxRx -- --src virtual://alpha/0 --dst virtual://alpha/1 --count 5`

> The samples accept flags like `--scheme`, `--fd`, `--brs`, `--bitrate`, etc. See their `Program.cs` for full usage.

## Filing issues

- Use the **Bug report** or **Compatibility test report** templates.
- For questions, please use **Discussions**.

## Coding

- Follow C# conventions. Keep public API minimal and well-documented.
- Prefer small, focused PRs.
- Use **Conventional Commits** in PR titles (e.g., `feat(core): add periodic TX API`).

# Tests

* **Run unit/integration tests:** `dotnet test`.
  To run tests for a modified adapter, set `CANKIT_TEST_ADAPTERS` to the adapter’s project name.


* **Adapter-specific dependencies:** Some adapter tests may require the vendor SDK and/or real hardware to be installed and connected.

* **Configurations:** You can run adapter tests with a **fake configuration** (no hardware). When conditions allow, prefer running in **Release** configuration against real hardware for end-to-end validation:

  ```bash
  dotnet test -c Release
  ```


## Areas / labels

- `area: core`, `area: pcan`, `area: kvaser`, `area: socketcan`, `area: zlg`
