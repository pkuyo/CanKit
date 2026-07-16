# AGENTS.md

## Cursor Cloud specific instructions

CanKit is a C# NuGet **library/SDK** (not a hosted service). There is no web server, database, or long-running daemon. The "apps" you can run are the console samples in `samples/`. Standard build/test/run commands are in `CONTRIBUTING.md`; the notes below are the non-obvious caveats.

### Toolchain
- Requires the **.NET SDK 8.x**. It is preinstalled at `/usr/local/dotnet` and symlinked to `/usr/local/bin/dotnet` (on `PATH`). The update script only runs `dotnet restore`; it does not reinstall the SDK.
- Builds target `netstandard2.0`, `net8.0`, and `net8.0-windows`; tests also target `net48`. On Linux, prefer `-f net8.0` for tests/samples to avoid the `net48` path.

### Testing gotchas
- Adapter tests **self-skip unless `CANKIT_TEST_ADAPTERS` is set** to an adapter project name. For hardware-free end-to-end tests, use the Virtual adapter:
  `CANKIT_TEST_ADAPTERS=CanKit.Adapter.Virtual dotnet test CanKitAdapters.slnf -c Release -f net8.0`
- The **Virtual adapter is the only backend that runs without hardware/native drivers**. PCAN/Kvaser/ZLG/Vector/ControlCAN need vendor SDKs + physical devices (mostly Windows); SocketCAN needs a Linux `vcan` interface. Those adapters can otherwise be built/tested in the `Fake` MSBuild configuration (`-c Fake`) which defines the `FAKE` constant so no native libs are required.

### Running samples
- Several samples (e.g. `CanKit.Sample.QuickStartTxRx`, `Sniffer`) finish their work and then block on `Console.ReadLine()` ("Press Enter to exit"). When running non-interactively, pipe empty stdin so they exit cleanly, e.g.:
  `echo "" | dotnet run --project samples/CanKit.Sample.QuickStartTxRx -c Release -f net8.0 -- --src virtual://alpha/0 --dst virtual://alpha/1 --count 5`

### Lint / formatting
- `dotnet format CanKit.sln --verify-no-changes` currently reports many **pre-existing** formatting differences (IDE0055 etc.) across the repo. A non-zero result there does not necessarily mean your changes are at fault; scope the check to files you touched.
