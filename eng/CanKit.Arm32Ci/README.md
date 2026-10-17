# ARM32 SocketCAN CI

One console project runs on both the hosted controller and the ARM32 machine.
The workflow publishes it once and sends that same build alongside the source
archive. It has no package dependencies and is separate from the CanKit solution.

Read `Controller.Run` for upload, execution, report retrieval and cleanup.
`Device.Supervise` owns the device lock and test-process lifetime; `Device.Test`
checks the machine and runs restore/test. `Program` contains the command entry
point and signal handling. Linux interop is confined to `LinuxNative`.

`Start-Arm32Ci.sh` only loads the existing `~/.config/cankit-arm32.env`, sets the
SDK path and starts the tool. Python is no longer needed by this workflow.

```bash
dotnet publish eng/CanKit.Arm32Ci -c Release -o artifacts/arm32-ci
ARM32_HOST=<tailscale-host> dotnet artifacts/arm32-ci/CanKit.Arm32Ci.dll controller
```

For direct tests on the device, prepare source and results directories, then run
`bash Start-Arm32Ci.sh supervise <source> <results>`. Direct runs keep both
directories. CI uses `--managed-run` with its own directory under
`~/.cache/cankit-arm32`; successful and failed tests keep reports until download,
while cancellation removes that run after stopping its children.

The controller needs .NET 10, Git, Tailscale and the existing Linux command-line
utilities. The ARM32 machine needs .NET 10, the SDK/runtime needed by the test
commit, and `vcan0`–`vcan2`. Native interop targets Linux/glibc 2.34+ (the current
device uses glibc 2.36).

The supervisor uses [glibc's spawn API](https://raw.githubusercontent.com/bminor/glibc/master/posix/spawn.h)
to start a separate process group with closed inherited descriptors.
[waitid with WNOWAIT](https://man7.org/linux/man-pages/man2/waitpid.2.html)
keeps the leader PID reserved until group termination, before releasing the lock.
