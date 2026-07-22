# AGENTS.md

Guidance for AI coding agents working in this repository. Assumes no prior knowledge of the project.

## Project overview

**CanKit.Pro** is a C#/.NET **NuGet library/SDK** for the CAN bus (Classic CAN 2.0 and CAN FD) — not a hosted service: there is no web server, database, or daemon. It is a fork of [CanKit](https://github.com/pkuyo/CanKit) (this repo: `github.com/dborgards/CanKit.Pro`) that extends the vendor-neutral raw-CAN core with higher-layer protocol stacks: ISO-TP, J1939-TP, UDS, CANopen, J1939, and a generic HAWE protocol framework.

The architecture follows a canonical layer nomenclature (**L0–L4**), defined in `docs/architecture/arc42-CanKit.md` and `docs/requirements/SRS-CanKit.md`:

- **L0 – Adapters** (`src/adapters/`): 7 backends wrapping vendor SDKs behind one API — `ControlCAN`, `Kvaser`, `PCAN`, `SocketCAN`, `Vector`, `ZLG`, plus `Virtual` (pure in-memory loopback, the only one needing no hardware/native driver).
- **L1 – Raw-CAN core** (`src/core/CanKit.Abstractions`, `src/core/CanKit.Core`): `ICanBus`, `CanFrame`, endpoint strings (`pcan://PCAN_USBBUS1`, `socketcan://can0`, `virtual://session/channel`, …), `CanBus.Open(...)` entry point, and a registry that auto-discovers referenced adapter assemblies via a generated preload list (`buildTransitive/CanKit.Core.targets`).
- **L2 – Raw-CAN service layer** (`src/core/CanKit.Pro.*`): `RawCan` (multi-consumer demux `ICanBusService`/`ISubscription`, TX-confirm `SendConfirmed`), `Actor` (single-mailbox `ProtocolActor` scheduler; no CanKit dependencies), `Addressing` (CAN-ID/J1939 PGN helpers; no dependencies), `Reliability` (`DeadlineScheduler`, `BusStateMonitor`).
- **L3 – Transports** (`src/transports/`): `CanKit.Pro.IsoTp` (ISO 15765-2), `CanKit.Pro.J1939Tp` (TP.BAM/TP.CM).
- **L4 – Application protocols** (`src/protocols/`): `CanKit.Pro.Uds` (ISO 14229 over ISO-TP), `CanKit.Pro.CANopen` (CiA 301: OD, SDO expedited/segmented/block, PDO, NMT, Heartbeat, SYNC, EMCY, node guarding), `CanKit.Pro.J1939`, `CanKit.Pro.Hawe`. L4/L3 packages compose on the L2 pipeline (`ICanBusService` + `IProtocolActor` + `DeadlineScheduler`).

Other top-level dirs: `tests/CanKit.Tests/` (single xUnit test project covering everything), `samples/` (7 raw-CAN console samples + 4 Pro quickstarts `IsoTp/Uds/CanOpen/J1939Quickstart`), `eng/` (version props, release scripts, package smoke test), `docs/` (see "Documentation" below), `.github/workflows/` (per-package CI).

## Toolchain

- **.NET SDK 8.x** per `CONTRIBUTING.md` and CI (`8.0.x`); there is no `global.json`, so newer SDKs (e.g. 10.x) also build it.
- C# 12, `Nullable` enabled, .NET analyzers on with `EnforceCodeStyleInBuild` (see `src/Directory.Build.props`).
- Libraries multi-target **`netstandard2.0;net8.0;net8.0-windows`**; the test project targets **`net8.0;net48`**. On Linux/macOS always pass `-f net8.0` to test/run commands to avoid the Windows-only `net48` target.
- MSBuild configurations: `Debug`, `Release`, **`Fake`** (defines the `FAKE` constant; adapter projects swap their real vendor-SDK references/native P/Invoke for `Native/*.Fake.cs` stubs so they compile and test without vendor SDKs — e.g. PCAN drops its `Peak.PCANBasic.NET` PackageReference under `-c Fake`).

## Build commands

```bash
dotnet build CanKit.sln                  # everything
dotnet build CanKitProUds.slnf -c Release # one area, via solution filter
```

- Prefer the per-area **solution filters** over the full solution for faster loops: `CanKitAdapters.slnf` (core + all adapters), `CanKitRawCan.slnf`, `CanKitActor.slnf`, `CanKitAddressing.slnf`, `CanKitReliability.slnf`, `CanKitProIsoTp.slnf`, `CanKitProJ1939Tp.slnf`, `CanKitProUds.slnf`, `CanKitProJ1939.slnf`, `CanKitProCANopen.slnf`, `CanKitProHawe.slnf`. Each Pro filter pulls in its L2 dependency chain, the Virtual adapter, and the test project.
- `GeneratePackageOnBuild=true`: building a packable project also produces `.nupkg`/`.snupkg` under `artifacts/nuget/`. Several experimental Pro packages set `IsPackable=false` (e.g. CANopen, IsoTp) until their surface stabilizes.
- `UseLocalProjectReferences` (default `true`) switches between `ProjectReference`s within the repo and `PackageReference`s to published CanKit packages; don't flip it without a reason.

## Testing

Test stack: xUnit + FluentAssertions in `tests/CanKit.Tests/`, which references **all** adapters and Pro packages. Test cases live in `TestCases/` (protocol suites under `CANopen/`, `IsoTp/`, `J1939/`, `Uds/`), parameter matrices in `Matrix/`.

**Key gotcha — adapter tests self-skip** unless the `CANKIT_TEST_ADAPTERS` environment variable names an adapter project (`tests/CanKit.Tests/TestCaseProvider.cs` loads that assembly's `Tests.TestDataProvider`). Hardware-free full run:

```bash
CANKIT_TEST_ADAPTERS=CanKit.Adapter.Virtual dotnet test CanKitAdapters.slnf -c Release -f net8.0
```

- The **Virtual adapter is the only backend that runs without hardware/native drivers**. PCAN/Kvaser/ZLG/Vector/ControlCAN need vendor SDKs + physical devices (mostly Windows); SocketCAN needs a Linux `vcan` interface. Build/test those with `-c Fake` when no SDK/hardware is present.
- Protocol (L2–L4) tests run entirely on `virtual://` loopback endpoints, so the command above exercises them too.
- CI (`.github/workflows/*-ci.yml`, one path-filtered workflow per package) runs Windows `net8.0` + `net48` and Ubuntu `net8.0` in Release.

## Running samples

```bash
dotnet run --project samples/CanKit.Sample.ListEndpoints -f net8.0
dotnet run --project samples/CanKit.Sample.QuickStartTxRx -f net8.0 -- --src virtual://alpha/0 --dst virtual://alpha/1 --count 5
```

Pro protocol-stack quickstarts (all run hardware-free on the Virtual loopback and exit on their own):

```bash
dotnet run --project samples/CanKit.Sample.IsoTpQuickstart -f net8.0
dotnet run --project samples/CanKit.Sample.UdsQuickstart -f net8.0
dotnet run --project samples/CanKit.Sample.CanOpenQuickstart -f net8.0
dotnet run --project samples/CanKit.Sample.J1939Quickstart -f net8.0
```

Several samples (`QuickStartTxRx`, `Sniffer`, …) end with `Console.ReadLine()` ("Press Enter to exit"). When running non-interactively, pipe empty stdin so they exit cleanly:

```bash
echo "" | dotnet run --project samples/CanKit.Sample.QuickStartTxRx -c Release -f net8.0 -- --src virtual://alpha/0 --dst virtual://alpha/1 --count 5
```

## Code style and conventions

- `.editorconfig` is enforced in-build: 4-space indent, LF endings, **Allman braces** (opening brace on a new line), `var` preferred, `System.*` usings first outside the namespace.
- Naming rules (warnings): private fields `_camelCase`, interfaces `IPascalCase`, consts `PascalCase`, type parameters `TPascalCase`. Nullable warnings (CS8618/CS8602) are enabled — keep new code null-clean.
- `dotnet format CanKit.sln --verify-no-changes` reports many **pre-existing** formatting diffs (IDE0055 etc.) across the repo; a non-zero result does not necessarily mean your change is at fault — scope format checks to files you touched.
- **Conventional Commits** in PR titles, e.g. `feat(core): add periodic TX API`; area labels like `area: core`, `area: pcan`. Keep PRs small and public APIs minimal and documented.
- Code comments and doc comments are written in **English** (a few carry Chinese parenthetical translations). Requirement IDs from the SRS (`FR-RAW-*`, `FR-TP-*`, `FR-UDS-*`, `FR-CO-*`, `FR-J1939-*`, `FR-HAWE-*`, `NFR-*`, `CON-*`) and arc42 references (`arc42 §x.y`, `ADR-n`) are cited in doc comments and package descriptions — preserve/update them when changing the corresponding behavior.

## Documentation

- `docs/getting-started.md` — English user guide (states English is the primary docs language), incl. an L2–L4 protocol-stack chapter with quickstart pointers; `docs/zh/` has the Chinese version.
- `docs/architecture/arc42-CanKit.md` — German arc42 architecture doc; **the source of truth for the L0–L4 model and ADRs**. Note it deliberately describes both current state and target state (marked "NEU / Ziel").
- `docs/requirements/SRS-CanKit.md` — German SRS with the requirement IDs used throughout the code.
- `docs/reviews/` — deep code review findings that motivated L2 hardening, plus the 2026-07-21 implementation gap review; HIL run reports go to `docs/reviews/hil/` (see `docs/hil-test-strategy.md`).
- `docs/release-1.0-criteria.md` — pack gates per Pro package, breaking-change policy, and the public-API tracking rule (`tests/CanKit.Tests/ApiApprovals/`, enforced by `PublicApiSurfaceTests`).
- `docs/hil-test-strategy.md` — the hardware-in-the-loop sampling plan (SRS assumption A-5) for L3/L4 packages before first productive release.
- Each package directory has its own English `README.md` describing its scope and usage.
- The root `README.md` (German) is partly aspirational — trust the actual `src/` layout and this file over its "Projektstruktur" section.

## Release and versioning

Process details in `docs/release-process.md`. Summary:

1. Package versions live centrally in **`eng/package-versions.props`** (one MSBuild property per package; dependency versions are separate properties so leaf packages can release independently).
2. Release flow: bump the version property → prepend entry to `CHANGELOG.md` → add `eng/release-notes/<PackageId>/<Version>.md` (auto-embedded as `PackageReleaseNotes` by `src/Directory.Build.targets`) → push.
3. `.github/workflows/nuget-pipeline.yml` handles pack/validate/publish, but is **currently disabled** (since 2026-07-16; only a no-op manual dispatch remains) — do not assume pushes publish anything.
4. `eng/packages.json` maps packages → projects → version properties for the PowerShell release scripts in `eng/scripts/`; it lists all 19 packages (core, adapters, and the `CanKit.Pro.*` packages). The six experimental Pro packages (IsoTp, J1939Tp, Uds, CANopen, J1939, Hawe — `IsPackable=false`) are marked `"publish": false`, which the pack/test/graph scripts honor by skipping them.
5. Treat `eng/package-versions.props` and `CHANGELOG.md` as code-owner-protected files.

## Security considerations

- No secrets, credentials, or network services live in this repo; the only CI secret is `NUGET_API_KEY` for the (disabled) publish pipeline.
- Adapters P/Invoke into vendor native libraries (PCAN-Basic, Kvaser CANlib, `zlgcan.dll`, Vector XL Driver, ControlCAN, SocketCAN/`libsocketcan`) and talk to physical bus hardware — validate all data coming from the bus (bounds-checked parsing is a deliberate pattern, e.g. `IsoTpFrameCodec`), and never trust frame lengths.
- Native DLL load failures are environment issues (OS, x86/x64 bitness, `PATH`/`LD_LIBRARY_PATH`, missing vendor SDK) — diagnose those before changing code.
- Keep the `netstandard2.0` target compatible: no APIs newer than that in shared code without `#if` guards (polyfills live in `tests/CanKit.Tests/Utils/`, adapters use multi-targeting).
