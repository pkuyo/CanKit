# CanKit – Tiefes Code-Review (komplettes Repository)

**Datum:** 2026-07-14 · **Stand:** `master` @ `36866ff` · **Umfang:** ~245 C#-Dateien, ~33.600 Zeilen (Core, Abstractions, ISO-TP-Transport, 7 Adapter, Tests, CI/Eng)

---

## Gesamteinschätzung

Die Architektur ist durchdacht: saubere Trennung in `CanKit.Abstractions` (API/SPI), `CanKit.Core` (Registry, Endpoints, Utilities) und Vendor-Adapter mit einheitlichem Muster (Bus + Transceiver + Optionen + Fake-Native-Schicht für Tests). Die Fake-Konfiguration (`-c Fake`) für hardwarelose CI ist ein Pluspunkt, ebenso die Test-Matrix und die pfadgefilterten CI-Workflows.

Deutliches Gefälle bei der Reife: **Core und die Adapter sind produktionsnah, der ISO-TP-Transport (`CanKit.Transport.IsoTp`) ist unfertig und in der aktuellen Form nicht funktionsfähig** – er enthält mehrere Fehler, die jede Übertragung verhindern (Details unten). Solange das so ist, sollte das Paket nicht veröffentlicht werden oder klar als experimentell markiert sein.

Querschnittsthema Nr. 1 ist die **Ownership-Semantik von `CanFrame`**: ein `readonly record struct` mit `IMemoryOwner<byte>` und `Dispose()` wird kopiert, geteilt (Events + Async-Pipe + Hub-Broadcast) und an mehreren Stellen unterschiedlich freigegeben – daraus entstehen Use-after-free- und Double-Dispose-Risiken.

---

## 1. Kritische Befunde

### 1.1 ISO-TP-Transport ist funktional defekt (WIP)

Der öffentliche Einstieg wirft `NotImplementedException` (`src/transports/CanKit.Transport.IsoTp/IsoTp.cs:11`), ebenso `RequestAsync`/`ReceiveAsync`/`GetFramesAsync` in `DefaultIsoTpChannel`. Darüber hinaus:

1. **CAN/CAN-FD-Erzeugung invertiert** – alle vier Frame-Builder geben bei `canfd == true` einen *Classic*-Frame zurück und umgekehrt:
   `FrameCodec.cs:127-129` (BuildSF), `:175-177` (BuildFF), `:207-209` (BuildCF), `:236-238` (BuildFC):
   ```csharp
   return canfd ? CanFrame.Classic(...) : CanFrame.Fd(...);   // invertiert
   ```

2. **Flow-Control-Frames tragen den PCI-Typ „First Frame“** – `FrameCodec.cs:224`:
   ```csharp
   span[pciStart] = (byte)(((byte)PciType.FF << 4) | ((byte)fs & 0xF)); // muss PciType.FC (0x30) sein
   ```

3. **FC-Padding überschreibt BS/STmin** – `FrameCodec.cs:227-234`: `InitBlockUnaligned(dst + pciStart + 1, 0, …)` nullt die gerade geschriebenen Bytes `BS` (`pciStart+1`) und `STmin` (`pciStart+2`).

4. **FF-Längenparsing verliert das High-Nibble** – `FrameCodec.cs:71`: `data[pciStart] & 0xF << 8` bindet als `data & (0xF<<8)` und ergibt für ein Byte immer 0. Jede FF-Länge > 255 wird falsch dekodiert. Korrekt: `(data[pciStart] & 0xF) << 8`.

5. **`EncodeStmin` wirft bei den häufigsten Werten** – `FrameCodec.cs:241-250`: `STmin = 0` (Default vieler Policies) und exakt 1 ms (`micro == 1000`, Lücke zwischen `< 1000` und `> 1000`) landen im `_ => throw`. Damit schlägt bereits das Erzeugen jedes CTS-Flow-Controls fehl.

6. **`DecodeStmin` wirft bei reservierten Werten (0x80–0xF0, 0xFA–0xFF)** – `FrameCodec.cs:252-260`. ISO 15765-2 verlangt, reservierte Werte als 0x7F (127 ms) zu behandeln; hier kann eine fehlerhafte Gegenstelle den RX-Pfad crashen. Generell fehlen in `TryParsePci` Längenprüfungen (`data[pciStart+1]`, `+2` ohne Bounds-Check → `IndexOutOfRangeException` durch Remote-Frames).

7. **CF-Segmentierung falsch** – `IsoTpChannelCore.SendAsync` (`IsoTpChannelCore.cs:497-508`):
   - `index = sfMax` (7 bzw. 62), aber der FF trägt bei Classic-CAN nur 6 Nutzbytes → **Byte 6 jeder Multi-Frame-Nachricht geht verloren**.
   - Erster CF wird mit `sn = 0` gebaut; ISO-TP verlangt SN = 1 – der eigene Empfänger (`_rxNextSn = 1`, `OnRxCF`-Reset bei Mismatch) verwirft die Übertragung sofort.

8. **Multi-Frame-TX kann nie starten** – `SendAsync` setzt `_tx = TxState.WaitFc` schon beim Einreihen (`IsoTpChannelCore.cs:498`), `IsReadyToSendData` (`:466`) liefert für `WaitFc` aber `false` → der Scheduler wählt den Kanal nie aus, der FF wird nie gesendet, der `Task` hängt für immer (Timeouts, s. Punkt 10, greifen nicht).

9. **Scheduler ist ein 100 %-CPU-Busy-Loop** – `IsoTpScheduler.RunAsync` (`IsoTpScheduler.cs:80-117`) enthält keinerlei Wartepunkt; das Feld `_txOrTimeOutEvent` (`:23`) wird nie benutzt. Außerdem wird `RunAsync` nirgends aufgerufen.

10. **Deadlines (N_As/N_Bs/N_Cr …) werden gepflegt, aber nie geprüft** – kein Codepfad wertet `Deadline.TimeOut` aus. OVFLW setzt `TxState.Failed` (`IsoTpChannelCore.cs:363-364`), ohne die Operation abzuschließen → hängender `Task`. FS=WT wird unbegrenzt akzeptiert (kein WFTmax).

11. **`TxOperation.TryPeek` für netstandard2.0 invertiert** – `IsoTpChannelCore.cs:55-66`: bei leerer Queue wird `Peek()` aufgerufen (`InvalidOperationException`), bei gefüllter Queue `false` zurückgegeben. Auf .NET Framework/netstandard-Konsumenten ist der TX-Pfad damit komplett defekt (das Paket targetet `netstandard2.0`, s. `src/Directory.Build.props`).

12. **`QueuedDeadline.Enqueue` crasht bei identischen Frames** – `QueuedDeadline.cs:52`: `Dictionary.Add` wirft `ArgumentException`, sobald zwei inhaltsgleiche Frames (gleiche ID/Payload – bei zyklischen Daten oder gepaddeten CFs normal) gleichzeitig „in flight“ sind.

13. **`BuildSF` nutzt `ArrayPool.Rent` statt des Allocators und gibt nie zurück** – `FrameCodec.cs:102`. Zusätzlich: `Rent(8)` liefert i. d. R. ein 16-Byte-Array; `CanFrame.Classic` validiert `Data.Length > 8` → **wirft `ArgumentOutOfRangeException` für jeden klassischen SF**. (`CanFrame.Validate`, `CanFrame.cs:358-365`.)

14. **Nicht-Try-TCS-Aufrufe in Race-Situationen** – `OnTx`/`OnTxFailed` (`IsoTpChannelCore.cs:225,240`) verwenden `SetResult`/`SetException`; die Cancellation-Registration (`SendAsync`, `:486`) kann parallel `OnTxFailed` auslösen → `InvalidOperationException` auf dem Scheduler-Thread. Generell werden `_tx`, `_pendingOperations`, `Router._channels` und `IsoTpScheduler._channels` (List!) ohne Synchronisation von mehreren Threads mutiert (`Register`/`Unregister` vs. Loop → `InvalidOperationException` bei Enumeration).

15. **`OnBackgroundExceptionOccurred` wirft im Event-Handler** – `IsoTpScheduler.cs:151-154`: Die Exception landet im Event-Aufrufer (RX-Loop des Adapters) bzw. als unbeobachtete Task-Exception – niemals beim Nutzer.

16. **Packaging:** `CanKit.Transport.IsoTp.csproj` referenziert `Peak.PCANBasic.NET` (Copy-Paste aus dem PCAN-Adapter) und hat `<Description>TODO</Description>`. Das ISO-TP-NuGet zieht damit grundlos das PEAK-Vendor-Paket als Abhängigkeit.

> **Empfehlung:** ISO-TP-Paket bis zur Fertigstellung aus der Release-Pipeline nehmen (bzw. `IsPackable=false`), Protokoll-Ebene mit Loopback-Tests gegen den Virtual-Adapter absichern (SF/FF/CF/FC-Roundtrip, STmin-Grenzwerte, SN-Folge, N_Bs/N_Cr-Timeouts).

### 1.2 `QueuedCanBus`: Batch-Reste hängen bis zum nächsten Enqueue

`src/core/CanKit.Core/Utils/QueuedTxCanBus.cs:199-226` – nach Teil-Akzeptanz (`accepted < index`) oder Busy (`accepted == 0`) verbleiben Frames im lokalen `batch`, aber die nächste Iteration blockiert zuerst in `WaitToReadAsync`. Ist der Channel leer, werden die restlichen Frames **erst gesendet, wenn irgendwann ein neuer Frame eingereiht wird** – ggf. nie. Der Backoff-Retry funktioniert dadurch faktisch nicht.
Fix-Skizze: `WaitToReadAsync` nur betreten, wenn `index == 0`; sonst direkt Retry mit Backoff.

Weitere Punkte in derselben Klasse:
- `batch[i].Dispose()` (`:211`) gibt Frames frei, deren Memory ggf. noch dem Aufrufer gehört (s. 2.1).
- `KickWorker`/`SleepWithResetAsync`-Race (`:255-277`): `KickWorker` kann `Cancel()` auf einer bereits disposten CTS aufrufen → `ObjectDisposedException` im `Transmit`-Aufrufer.
- Die im Konstruktor an `_inner` angehängten Event-Lambdas werden nie abgemeldet → bei `OwnsInnerBus=false` hält der innere Bus den Wrapper am Leben (Leak).
- `Dispose()` ist nicht idempotent gegen Doppel-Aufruf nach `_cts.Dispose()`.

### 1.3 SocketCAN: Sende-Timeout ohne gestartete Stopwatch

`src/adapters/CanKit.Adapter.SocketCAN/SocketCanBus.cs:233` und `:277` – `var stopWatch = new Stopwatch();` wird **nie gestartet**; `stopWatch.Elapsed` bleibt 0, `remainingTime` wird nie kleiner. Konsequenz: `poll()` wird bei `timeOut > 0` in jeder Runde wieder mit dem vollen Timeout aufgerufen; bei einem Bus, der schreibbar bleibt, aber nichts annimmt (`wrote == 0`), droht eine Endlosschleife. Dasselbe Muster in den ZLG-Transceivern (`ZlgCanClassicTransceiver.cs:104`, `ZlgCanFdTransceiver.cs:97`, `ZlgCanMergeTransceiver.cs:74`) – dort wird `remaining = timeOut - 0` an `ZCAN_Receive` gereicht, die Gesamtwartezeit ist also unbegrenzt.

### 1.4 BCMPeriodicTx: `Update()` mit CAN-FD-Frame wirft immer

`src/adapters/CanKit.Adapter.SocketCAN/Utils/BCMPeriodicTx.cs:173-188` – Copy-Paste-Fehler:
```csharp
if (_frame.FrameKind is CanFrameType.Can20) { ... }
else if (_frame.FrameKind is CanFrameType.Can20) { ... }   // muss CanFd sein
else throw new NotSupportedException(...);
```
Jedes `Update(frame: fdFrame)` endet in `NotSupportedException`. Außerdem: `RemainingCount` (`:199-228`) liest sofort nach dem Write von einem **non-blocking** Socket – `read` liefert dann typischerweise `EAGAIN` → `ThrowErrno`, die Abfrage ist unzuverlässig.

### 1.5 `CanFrame.Dispose()` ignoriert `ownMemory`

`src/core/CanKit.Abstractions/API/Can/Definitions/CanFrame.cs:370` – `public void Dispose() => _memoryOwner?.Dispose();` gibt den Owner **immer** frei, obwohl die Factory-Überladungen `ownMemory=false` dokumentieren („If true, disposing the frame disposes memoryOwner“). Das Flag wird gespeichert (`OwnMemory`, `:147`), aber nie ausgewertet. Jeder Aufrufer, der `ownMemory=false` übergibt (z. B. `FrameCodec` via `allocator.FrameNeedDispose`), verliert trotzdem sein Memory, sobald irgendjemand `Dispose()` aufruft.

---

## 2. Wichtige Befunde

### 2.1 Frame-Ownership/Lifetime uneinheitlich (Querschnitt)

- **Virtual-Adapter:** `VirtualTransceiver.Transmit` reicht den Frame des Senders **ohne Kopie** an `VirtualBusHub.Broadcast` weiter (`VirtualTransceiver.cs:22,37,50`; `VirtualBusHub.cs:57-77`). Dieselbe `CanReceiveData` (inkl. `IMemoryOwner`) geht an N Empfänger-Pipes; jede Pipe hat einen `onDropped`-Callback, der `CanFrame.Dispose()` aufruft (`VirtualBus.cs:47-48`). Ein Drop in Bus A gibt Memory frei, das Bus B (und der Sender!) noch liest → Use-after-free bei gepoolten Buffern; Sender, die den Frame nach `Transmit` wiederverwenden/disposen, korrumpieren die Empfänger.
- **Scheduler (ISO-TP):** `TransmitTxOperation` disposed den Frame (`using var frame`, `IsoTpScheduler.cs:59`), obwohl `_nAs.Enqueue(frame)` ihn danach für Echo-Matching referenziert.
- **Adapter-RX:** Dieselbe `CanReceiveData` wird an `FrameReceived`-Subscriber **und** die Async-Pipe verteilt (`SocketCanBus.DrainReceive`, `ZlgCanBus.PollLoop` u. a.); der Pipe-`onDropped` disposed, während Event-Handler den Frame noch halten dürfen.

**Empfehlung:** Ownership-Vertrag zentral definieren (z. B. „RX-Frames gehören der Pipe; Event-Handler bekommen nur `CanFrameView`; TX-Frames gehören dem Aufrufer, Adapter kopieren vor Rückkehr“) und `Dispose`-Aufrufe entsprechend ausdünnen. Der `OwnMemory`-Fix (1.5) ist die Voraussetzung.

### 2.2 `AsyncFramePipe`

`src/core/CanKit.Core/Utils/AsyncFramePipe.cs`
- `ReceiveBatchAsync` erzeugt pro Iteration `ReadAsync(...).AsTask()`; gewinnt der Exception-Pulse das `WhenAny` (`:83-105`), bleibt der verwaiste Reader registriert und **konsumiert später einen Frame, der verworfen wird** (Frame-Verlust nach Hintergrundfehlern).
- Der innere `catch (OperationCanceledException) return list;` (`:91-94`) schluckt auch die **Nutzer**-Cancellation und liefert stillschweigend eine Teil-Liste, während der äußere Filter (`:108`) sie eigentlich propagieren will – inkonsistenter Kontrakt.
- `ExceptionOccurred` (`:154-160`) weckt nur aktuell wartende Leser; ein unmittelbar danach startender `ReceiveBatchAsync` sieht den Fehler nie (verlorene Fehlersignalisierung, je nach Absicht ok – dokumentieren).

### 2.3 `SoftwarePeriodicTx`

`src/core/CanKit.Core/Utils/SoftwarePeriodicTx.cs`
- **macOS:** Der Nicht-Windows-Pfad ruft `clock_nanosleep` aus `libc` auf (`:594`), das auf macOS nicht existiert. Die `EntryPointNotFoundException` wird von `catch { }` (`:564`) verschluckt → `PreWait` kehrt sofort zurück → **Busy-Loop, der Frames so schnell wie möglich sendet**. Mindestens per `OperatingSystem.IsMacOS()` auf `Thread.Sleep`-Fallback ausweichen.
- `Stop()` disposed den Plattform-Kontext (`_sDispose`, `:87`), während der Loop-Thread nach `Wait(200)`-Timeout noch `ctx.hTimer` benutzen kann (Use-after-close des Timer-Handles).
- `DecreaseAndMaybeFinish` feuert `Completed` **innerhalb** von `_gate` (`:203`) – Deadlock-/Reentranz-Risiko für Handler, die `Update/Stop` aufrufen.
- Nach Cancel sendet der Loop noch genau einmal (Sendung vor erneutem Token-Check, `:156-165`).
- `Win_PreWait`-Endspurt (`:418-424`): `sw.Elapsed < target && sw.Elapsed < spinUntil` bricht bei `spinUntil` (= `target - SpinBudget`) ab – Frames gehen systematisch bis ~30 µs zu früh raus; gemeint war vermutlich `>= spinUntil … spin bis target`.

### 2.4 Weitere Adapter-Punkte

- **SocketCAN `ApplyDeviceConfig`** (`SocketCanBus.cs:600-605`): `Encoding.ASCII.GetString(name)` übernimmt die NUL-Padding-Bytes in `_ifName`; alle libsocketcan-Aufrufe und Logs arbeiten mit dem verunreinigten Namen. Bei NUL-terminierter Marshalling-Konvention geht es zufällig gut – trotzdem bis zum ersten `\0` trimmen.
- **SocketCAN**: `SO_SNDTIMEO` wird aus `ReadTimeoutMs` gesetzt (`:153-162`) – Sende-Timeout aus Lese-Timeout-Option (Property-Typo `ReadTImeOutMs` wurde per NFR-011 korrigiert).
- **ZLG-Transceiver Teil-Erfolg**: Bei fehlgeschlagenem Zwischen-Flush wird `sent` ohne die tatsächlich akzeptierten `re` Frames zurückgegeben (`ZlgCanClassicTransceiver.cs:32-36`) → Aufrufer senden bereits übertragene Frames erneut (Duplikate auf dem Bus). Korrekt: `sent + re` zurückgeben.
- **ZLG ctor**: `handle.SetDevice(...)`/Logging „Initialize succeeded“ **vor** `ZlgErr.ThrowIfInvalid` (`ZlgCanBus.cs:129-134`); `ZCAN_SetValue`-Aufrufe für `work_mode`/`initenal_resistance` prüfen den Rückgabewert nicht.
- **ZLG `GetReceiveCount`** (`:588`): `(byte)((byte)ProtocolMode - 1)` – gefährlich, falls `ProtocolMode`-Enum bei 0 beginnt (Underflow zu 255); explizites Mapping wäre robuster.
- **BCMPeriodicTx ctor**: Frame-Kind-Validierung erst nach Erzeugung dreier FDs (`:43-69`); bei Throw bleiben die Handles bis zur Finalisierung offen. Außerdem verbietet die Prüfung Classic-Frames auf FD-Bussen, was SocketCAN eigentlich erlaubt.
- **Virtual**: `Reset()`/`ClearBuffer()` leeren die Async-Pipe nicht (`VirtualBus.cs:101-109`), totes Feld `_rxQueue` (`:29`), Dispatcher-Name „Vector CAN bus“ (Copy-Paste, `:52`). `VirtualBusHub._hubs` (static, `VirtualBusHub.cs:20`) entfernt leere Hubs nie → Leak über Sessions.
- **SocketCAN `EPollLoop`**: Cancel-Log-Meldung „ControlCAN poll loop canceled“ (Copy-Paste, `SocketCanBus.cs:854`).

### 2.5 Core-Punkte

- **`CanBus.Open<TBus,...>(DeviceType)`** (`CanBus.cs:146-152`): Wirft `Open(device, …)` nach erfolgreichem `CreateDevice`, wird das Device nie disposed (Leak eines nativen Handles).
- **`BitTimingSolver.FromSamplePoint`** (`BitTimingSolver.cs:44`): Ist `Tseg1Min > ntq-2`, wirft `Clamp` `ArgumentException` statt die NTQ-Iteration zu überspringen (`continue`). Kleine NTQ-Werte in den Limits crashen so die gesamte Suche. (`tqNs`, `:64`, ist tot.)
- **`CanEndpoint.Parse`** (`CanEndpoint.cs:51-54`): `Uri` **lowercased den Host** – aus `zlg://USBCANFD-200U` wird `usbcanfd-200u`. Alle Adapter müssen Gerätenamen case-insensitiv behandeln (nicht überall garantiert); Namen mit Sonderzeichen/Leerzeichen werfen `UriFormatException`.
- **`CanRegistry`**: Registrierung nutzt plain `Dictionary`s ohne Lock (`CanRegistry.cs:169-184`); solange nach dem Lazy-Build nichts mehr registriert wird, ok – `RegisterEndPoint` & Co. sind aber `internal` erreichbar und `IsoTpRegistry` u. Ä. könnten später zur Laufzeit registrieren. Mindestens dokumentieren, besser `ConcurrentDictionary`.
- **`CanReceiveData.SystemTimestamp = DateTime.Now`** (`CanStructs.cs:253`) und diverse Fehlerinfos nutzen lokale Zeit; gemischt mit UTC-Berechnung in `SocketCanBus.DrainReceive` (`:884-887`). Einheitlich `DateTime.UtcNow` empfohlen.

---

## 3. Geringfügiges / Kosmetik

| Fundstelle | Problem |
|---|---|
| ~~`CanKit.Abstractions/API/Transport/Excpetions/`~~ | ✅ entfällt mit Legacy-ISO-TP-Abbau (Namespace-Typo entfernt) |
| Namespaces im ISO-TP-Projekt | Mischung `CanKit.Transport.IsoTp.*` und `CanKit.Protocol.IsoTp.*` |
| ~~`IBusRTOptionsConfigurator.ReadTImeOutMs`~~ | ✅ umbenannt zu `ReadTimeoutMs` (NFR-011) |
| ~~`AsyncFramePipe.ExceptionOccured`~~ | ✅ umbenannt zu `ExceptionOccurred` (NFR-011) |
| `SoftwarePeriodicTx` | toter Code: `_jitterLastNs`-Getter liest unsynchronisiert; `CREATE_WAITABLE_TIMER_MANUAL_RESET` ungenutzt |
| `Deadline` (`IsoTp/Defines/Deadline.cs`) | `_actived`/`Actived` tot; `actived`-Parameter ohne Wirkung auf Stopwatch-Start |
| `IsoTpScheduler.Score` | konstant für alle Kandidaten (TODO), Sortierung wirkungslos |
| `ZlgCanBus` | auskommentierter Block `ZCAN_GetDeviceInf` (`:93-100`); `"initenal_resistance"` ist zwar der echte ZLG-Property-String, verdient aber einen Kommentar |
| `VirtualBus`/`SocketCanBus` | `FrameReceived`-Events teils mit `_evtGate`-Lock, teils als Auto-Event – uneinheitlich |
| Chinesische Kommentare/Exception-Texte (z. B. `BitTimingSolver.cs:74`) | für ein englischsprachig dokumentiertes OSS-Paket vereinheitlichen |
| `eng/…`, `nuget-pipeline.yml` | Push-Trigger auf `branches: [main]`, Default-Branch ist `master` – der Branch-Trigger ist tot (Tags funktionieren) |

---

## 4. Tests & CI

- Gute Grundlage: Matrix-Tests (`tests/CanKit.Tests/Matrix/*`), Fake-Native-Schichten je Adapter, adapterweise CI-Workflows mit Pfadfiltern, Windows (net8.0/net48) + Linux.
- **Lücken:** Keinerlei Tests für `CanKit.Transport.IsoTp` (erklärt Abschnitt 1.1), keine Tests für `QueuedCanBus` (Backoff/Teil-Akzeptanz), `AsyncFramePipe`-Fehlerpfade oder `BitTimingSolver`-Grenzfälle.
- CI-Workflows testen `CanKitAdapters.slnf`; das ISO-TP-Projekt (`CanKitTransports.slnf`) hat keinen eigenen Workflow.

---

## 5. Priorisierte Empfehlungen

1. **ISO-TP aus dem Release nehmen** (oder `IsPackable=false` + „experimental“), `Peak.PCANBasic.NET`-Referenz entfernen. *(sofort)*
2. `CanFrame.Dispose()` auf `if (OwnMemory) _memoryOwner?.Dispose();` fixen und Ownership-Vertrag dokumentieren; danach die `Dispose`-Aufrufe in `QueuedCanBus`, Pipes und Virtual-Hub darauf ausrichten. *(hoch)*
3. `QueuedCanBus.SendLoop` so umbauen, dass Batch-Reste ohne neuen Enqueue erneut versucht werden. *(hoch)*
4. `Stopwatch.StartNew()` in `SocketCanBus.Transmit` (2×) und den drei ZLG-Transceivern. *(hoch, trivial)*
5. `BCMPeriodicTx.Update` FD-Zweig (`Can20`→`CanFd`) fixen; `RemainingCount` mit `poll` absichern. *(hoch, trivial)*
6. ISO-TP-Protokollfehler (invertiertes `canfd`, FC-PCI, FC-Padding, FF-Länge, STmin, SN/FF-Offset, `TryPeek`-netstandard) beheben und mit Virtual-Loopback-Tests abdecken; Scheduler auf ereignisgetrieben (das vorhandene `AsyncAutoResetEvent`) umstellen und Deadline-Prüfung implementieren. *(mittel – zusammen mit Fertigstellung)*
7. macOS-Fallback in `SoftwarePeriodicTx`; `Completed` außerhalb des Locks feuern. *(mittel)*
8. ~~Typos in öffentlicher API (`Excpetions`, `ReadTImeOutMs`, `ExceptionOccured`) vor 1.0 bereinigen~~ — `ReadTimeoutMs`/`ExceptionOccurred` umbenannt (NFR-011); `Excpetions` entfällt mit Legacy-ISO-TP-Abbau.
9. `nuget-pipeline.yml` Branch-Trigger auf `master` korrigieren. *(niedrig, trivial)*
