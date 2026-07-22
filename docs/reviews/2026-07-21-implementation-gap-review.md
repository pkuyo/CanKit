# Review: Implementierungsstand vs. SRS-Zielbild (Gap-Analyse)

**Dokumenttyp:** Verifizierendes Gap-Review (Ist-Stand ↔ Soll-Zielbild)
**Bezug:** `docs/requirements/SRS-CanKit.md` (Stand 2026-07-14), `docs/architecture/arc42-CanKit.md`, Vorgänger-Review `docs/reviews/2026-07-14-deep-code-review.md`
**Stand:** 2026-07-21 · Branch `main`
**Methode:** Statische Code-Inspektion aller L1/L2-Kern- und L3/L4-Pro-Pakete samt Testsuite, je Schicht mit eigenem Teilreview; Stichproben der Schlüsselbefunde händisch gegengeprüft. Tests wurden nicht lokal ausgeführt; CI-Status (`.github/workflows`) stichprobenartig grün.

---

## 1. Gesamteinschätzung

Das SRS-Zielbild ist **funktional weitgehend erreicht**: Von ~75 normierten Anforderungen sind alle Must-Anforderungen implementiert — mit **einer substanziellen Ausnahme** (FR-CO-005, dynamisches PDO-Mapping) und einem realen L1-Defekt aus der als geschlossen geglaubten Ownership-Fehlerklasse (Use-after-free im Periodic-TX-Pfad). Die Architektur (L2-Demux, Actor-Modell, TX-Confirm, Deadline-/BusState-Infrastruktur) ist konsistent umgesetzt und ungewöhnlich tief gegen Races abgesichert.

Die kritischen Lücken liegen weniger im „ob" als im „beweisen" und im „ausliefern":

1. **Verifikation**: Mehrere Must-Anforderungen haben implementierte, aber **ungetestete Kernpfade** (STmin-Pacing, CAN-FD-Roundtrip, J1939-TP-Timer T1/T2/T4, P2*-Timeout, BusOff-Abbruch). Eine Regression dort wäre im CI unsichtbar.
2. **Release-Fähigkeit**: Der gesamte Pro-Stack (L2–L4, 10 Pakete) ist **prozessual nicht auslieferbar** — `eng/packages.json`, Release-Skripte, Release-Notes und CHANGELOG erfassen ihn nicht; die (derzeit deaktivierte) NuGet-Pipeline würde ihn ignorieren.
3. **Ein realer Defekt**: `SoftwarePeriodicTx`/`VirtualBus.TransmitPeriodic` halten Aufrufer-Frames über die Rückkehr hinaus ohne Kopie — die Use-after-free-Fehlerklasse, deren Beseitigung Kern der L2-Arbeit (FR-RAW-001..005) war, lebt im produktiven L1/L0-Periodic-Pfad fort.

## 2. Umsetzungsstand im Überblick

| Schicht / Paket | Anforderungen | Umgesetzt | Teilweise / Lücken |
|---|---|---|---|
| L2 RawCan/Actor/Addressing/Reliability (FR-RAW-001..052) | 25 | 22 voll | 005 (UAF Periodic-TX), 033 (kein BusOff-Test), 051 (kein L3-Abbruch-Test); 052 bewusst zurückgestellt → im IsoTp-Codec umgesetzt |
| L3 ISO-TP (FR-TP-001..020) | 20 | 17 voll, 013 entfallen (Legacy) | 001 (nur Classic, Längen nicht gefegt), 003 (Akzeptanztest fehlt), 010 (N_As-Test fehlt) |
| L3 J1939-TP (FR-TP-030..035) | 6 | 5 voll | 032 (Timer T1/T2/T3-WaitEom/T4/Th ungetestet) |
| L4 UDS (FR-UDS-001..012) | 12 | 11 voll | 008 (P2*-Timeout ungetestet); 012 (Upload-Vollzyklus ungetestet, kein One-shot `UploadAsync`) |
| L4 CANopen (FR-CO-001..012) | 12 | 10 voll | **005 (dynamisches PDO-Mapping fehlt, Must)**, 006 (EventTimer ungetestet, kein Auto-CoS) |
| L4 J1939 (FR-J1939-001..007) | 7 | 5 voll | 004 (kein Adressraum-Scan/Arbitrary-Fallback), 007 (Task.Delay-Drift statt DeadlineScheduler; SRS-Text ≠ Code) |
| L4 HAWE (FR-HAWE-001..005) | 5 | 4 voll | 005 = Prozessanforderung (SRS-Nachführung bei Spez-Verfügbarkeit) |
| NFR-001..012 | 12 | 7 voll | 002 (kein macOS-CI), 003 (keinerlei Verifikation), 005 (keine kuratierten Plattformfehler), 006 (uneinheitliche Fehlerarchitektur), 007 (kein Allokations-Benchmark), 008 (Registry ohne Lock, Stress nur Actor) |
| CON-001..008 | 8 | 5 voll | 007 (Hawe-`.slnf` defekt), 008 (SRS sagt `master`, Repo nutzt `main`); Release-Prozess erfasst Pro-Pakete nicht (§ 5) |

## 3. Kritische Lücken (P1)

### K1 — Use-after-free: TX-Lease-Verletzung im Periodic-TX-Pfad (FR-RAW-005)

`SoftwarePeriodicTx` speichert den Aufrufer-`CanFrame` unverändert (`src/core/CanKit.Core/Utils/SoftwarePeriodicTx.cs:44`, `:112`) und transmittiert ihn später in der Loop (`:199`ff.). Disposed der Aufrufer einen *owning* Frame nach `Start()`/`Update()`, sendet die Loop aus zurückgepooltem Speicher. Dasselbe Muster in `VirtualBus.TransmitPeriodic` (`src/adapters/CanKit.Adapter.Virtual/VirtualBus.cs:137`) sowie laut arc42 §8.1 („Noch offen") bei ZLG/ControlCAN-Periodic-TX und den Vector/PCAN-Fallbacks.
**Einordnung:** FR-RAW-005 ist nur Should, aber es ist exakt die Fehlerklasse, die FR-RAW-001..004 (Must) beseitigt haben — im produktiven L1-Pfad trivial auslösbar. Fix ist klein (Frame im Ctor/`Update` duplizieren, Kopie besitzen und beim Stop/Dispose freigeben) plus Regressionstest.

### K2 — Pro-Stack ist nicht release-fähig (Prozess, CON-004/007/008)

- `eng/packages.json` listet nur `CanKit.Abstractions`, `CanKit.Core` und die 7 Adapter — **alle 10 `CanKit.Pro.*` fehlen**; die PowerShell-Release-Skripte (`eng/scripts/Pack-/Test-/Publish-NuGetPackages.ps1`, `Get-PackageGraph.ps1`) iterieren ausschließlich `packages.json`.
- `eng/release-notes/` enthält nur 0.5.5-Notes für Core/Adapter; `CHANGELOG.md` führt Pro-Pakete nur unter „Unreleased"; `eng/package-smoke` referenziert nur Core+Adapter.
- Die `PackageReference`-Fallbacks der L3/L4-csproj zeigen auf nie publizierte 0.1.0-Versionen → `UseLocalProjectReferences=false` ist für Pro derzeit unbenutzbar.
- Folge: Selbst die vier technisch packbaren L2-Pakete (`CanKit.Pro.Actor/Addressing/RawCan/Reliability`, je 0.1.0) würden bei Wiederaktivierung der (seit 2026-07-16 deaktivierten) Pipeline nicht gebaut/validiert/publiziert.

### K3 — FR-CO-005 (Must): dynamisches PDO-Mapping fehlt

PDO-Re-Mapping über OD 0x1600/0x1A00 per SDO existiert nicht (dokumentiert in `src/protocols/CanKit.Pro.CANopen/README.md:32-34`, `Pdo/PdoMapping.cs:14-16`); nur API-seitiges (Re-)Mapping, nur byte-aligned. Einzige substanziell unerfüllte Must-Anforderung — für Geräte, die vom Master per SDO um-gemappt werden, ist der Stack nicht CiA-301-konform. Aktuell bewusst als Pack-Gate eingepreist (`CanKit.Pro.CANopen.csproj:17-19`).

### K4 — NFR-003 (Must): STmin-Pacing ohne jede Verifikation

Der STmin-Pacing-Pfad (`src/transports/CanKit.Pro.IsoTp/IsoTpChannel.cs:719-729`) wird in **keinem** Test durchlaufen (alle Peer-FCs nutzen `stMinRaw: 0`); kein Test misst Inter-Frame-Zeiten; die geforderte dokumentierte Genauigkeit (±1 ms o. ä.) existiert nirgends. Ein falsch gepacter Sender würde langsame reale ECUs überfluten, ohne dass CI es bemerkt.

### K5 — CAN-FD-Blindfläche ISO-TP/UDS (FR-TP-001/003)

Kein positiver CAN-FD-Roundtrip-Test im gesamten Testprojekt (`useCanFd: true` → 0 Treffer); FD-Pfade (FD-SF-Escape, Long-FF > 4095, `CanFrame.Fd`-Emission `IsoTpChannel.cs:528-532`, `:1103-1107`, FD-DLC-Padding) laufen nur in Codec-Unit-Tests, nie mit Actor/TX-Confirm/FC-Handshake. Das SRS-Akzeptanzkriterium „`CanFd=true` erzeugt ausschließlich `CanFd`-Frames für SF/FF/CF/FC" ist als Test nicht realisiert; der Längenbereich 1..4095 wird nicht gefegt (nur 3/20/200 Byte). UDS-Tests sind ebenfalls Classic-only.

## 4. Verifikations- und Robustheitslücken (P2)

| # | Befund | Anforderung | Beleg |
|---|---|---|---|
| K6 | J1939-TP: Timer T1 (BAM+CM), T2, T3-WaitEom, T4/CTS(0)-Hold, Th implementiert, aber ohne einen Test (nur T3-initial + Tr belegt); CTS-Segment-Aushandlung (`maxCts < cap`, `J1939TpChannel.cs:415-417`) ungetestet | FR-TP-031/032 (Must) | `tests/CanKit.Tests/TestCases/J1939TpTests.cs` |
| K7 | BusOff-Pfade ungetestet: `SendConfirmed`-BusOff-FailureReason (`CanBusService.cs:419-450`) ohne Test (Simulationshaken `VirtualBusHub.SetBusState` existiert); SRS geforderter L3-Abbruch-Integrationstest „BusOff bricht aktiven Sendevorgang" fehlt | FR-RAW-033/051 (Must) | `tests/.../TxConfirmTests.cs` |
| K8 | `CanRegistry` weiterhin ohne Synchronisation (`CanRegistry.cs:169-184`) — nur faktisch sicher (Lazy-Ctor-Registrierung, `internal` API); NFR-008 nennt die Registry ausdrücklich, Stresstests existieren nur für den Actor, nicht für `CanBusService` (paralleles Subscribe/Dispose vs. Dispatch) | NFR-008 (Must) | `src/core/CanKit.Core/Registry/CanRegistry.cs` |
| K9 | Zeitkritische Pfade ungetestet: P2*-Timeout (`UdsClientImpl.cs:816`, einzige `Timer`-Assertion ist P2); TPDO-EventTimer (`CanOpenNode.cs:1553-1576`, kein Test); kein automatisches Change-of-State bei OD-Schreibzugriff (SRS-Verifikation „TPDO bei OD-Wertänderung" nur per manuellem `TriggerTpdoAsync` erfüllt — Interpretationslücke) | FR-UDS-008, FR-CO-006 (Must) | `tests/.../UdsClientTests.cs:407`, `CanOpenNodeIntegrationTests.cs:841` |
| K10 | Klassische SDO-Server-Sessions laufen nie in Timeout: `SdoServerSession.Deadline` wird nirgends armiert (`CanOpenNode.cs:1738`, Erstellung `:988`/`:1068` ohne `Arm`) — verstummender Client hinterlässt Session unbegrenzt offen (Block-Server macht es korrekt, `CanOpenNode.SdoBlock.cs:637`) | Robustheit (CiA-301-implizit) | `src/protocols/CanKit.Pro.CANopen/CanOpenNode.cs` |
| K11 | macOS-Fix (NFR-002) existiert (`SoftwarePeriodicTx.cs:357-364`), aber **kein macOS-Runner** in `.github/workflows/` — geforderte Verifikationsmethode nicht umgesetzt; `SleepCoarse`-Jitter-Budget (~1,5 ms) nicht gegatet | NFR-002 (Must) | `.github/workflows/*-ci.yml` (nur windows/ubuntu) |

## 5. Prozess-/Release-Lücken (Detail zu K2)

- `eng/packages.json` + `eng/scripts/*`: Pro-Pakete ergänzen (Abhängigkeitsgraph: Actor/Addressing → RawCan/Reliability → IsoTp/J1939Tp → Uds/CANopen/J1939/Hawe), `sharedPathPrefixes` um `src/Directory.Build.targets` prüfen.
- `eng/release-notes/<PackageId>/<Version>.md` fehlt für alle Pro-Pakete; `CHANGELOG.md` hat keine Released-Sektion für Pro.
- `eng/package-smoke` deckt Pro-Pakete nicht ab.
- Branch-Realität: `main` ist HEAD (`develop`, `upstream-master` existieren); SRS CON-008 sagt `master` — SRS-Text veraltet, nicht der Code.
- Die deaktivierte `nuget-pipeline.yml` (nur No-op-Dispatch) ist Vorsatz; bei Reaktivierung Branch-Trigger (`main` vs. auskommentiertes `main`/`master`) klären.

## 6. Weitere Befunde (P3)

**Robustheit/Feldverhalten**
- SDO-Block-Transfer: Teil-ACK (`ackseq != SegmentsInFlight`) → Abbruch statt CiA-301-Retransmission (`CanOpenNode.SdoBlock.cs:207-217`, `:824-831`; als MVP-Entscheidung kommentiert); `pst=0` erzwungen → kein Fallback Block→segmented. Gegen reale Peers mit kleinen Blockfenstern der wahrscheinlichste Felddefekt.
- FR-J1939-007: periodische PGNs laufen als `Task.Delay`-Schleife (`J1939NodeImpl.cs:1042-1064`) → Intervall = Periode + Sendezeit (Drift); SRS behauptet `DeadlineScheduler`-Zeitgebung. `IPeriodicTx.Faulted`-Blocker ist inzwischen behoben, Re-Integration steht aus.
- FR-J1939-004: kein Adressraum-Scan/Arbitrary-Address-Fallback; SRS-Verifikation „alle Adressen belegt" nicht als Test (Must).
- FR-J1939-006: Per-Send-`Priority` wird auf dem TP-Pfad ignoriert (`J1939Message.cs:62-68`, dokumentiert).
- NFR-005: keine kuratierten Plattformfehler — Kvaser/PCAN/Vector/ControlCAN propagieren rohe `DllNotFoundException` (`KvaserBus.cs:412-417` schluckt Initialisierungsfehler zunächst); SocketCAN-`Enumerate()` liefert außerhalb Linux still nichts.
- NFR-006: L1 hat strukturierte `CanKitException`-Hierarchie mit `CanKitErrorCode`; L2–L4 definieren sechs eigene Hierarchien direkt ab `System.Exception` ohne gemeinsame Basis oder Fehlercodes. Konsistent ist nur das `BackgroundExceptionOccurred`-Muster.
- ISO-TP RX-seitige Block-FC-Generierung (`LocalBlockSize > 0`, `IsoTpChannel.cs:936-947`) testseitig tot; N_As nur indirekt über L2-Test belegt.
- UDS FR-UDS-012: kein voller Upload-Zyklus-Test (0x35→0x36→0x37), kein One-shot `UploadAsync` (nur `DownloadAsync`).
- CANopen: kein Toggle-Bit-Negativtest klassisches SDO; NMT `ResetCommunication` (0x82) nicht explizit getestet.

**Kleinigkeiten/Hygiene**
- `CanKitProHawe.slnf:14` Trailing-Comma → `dotnet sln … list` bricht (MSBuild toleriert es, händisch verifiziert).
- `RepositoryUrl` aller Pro-csproj zeigt auf Upstream `pkuyo/CanKit` statt `dborgards/CanKit.Pro`.
- Veraltete csproj-Kommentare: IsoTp („Runtime added in 0.2.x", Version ist 0.1.0), Uds („pre-release until multi-DID / upload-download … land" — längst gelandet).
- Private Rename-Reste `_errorOccured` (`PcanBus.cs:26`, `KvaserBus.cs:32`); SocketCAN-Fluent-Methode `ReadTimeOut` (`Options.cs:100,116`).
- `SoftwarePeriodicTx.cs:630`: Catch-All um `clock_nanosleep` — persistenter Fehler ließe PreWait zum No-Op werden (theoretischer Sende-Sturm auf Linux).
- Prädikat-Exceptions im Demux werden kommentarlos verschluckt (`CanBusService.cs:176-184`, Code-Kommentar: „least-bad until a fault channel exists").
- NFR-007: kein Allokations-Benchmark (`samples/CanKit.Sample.Benchmark` misst nur Durchsatz; kein `MemoryDiagnoser`).

## 7. Doc-Drift (SRS/arc42 ↔ Code)

| Stelle | Behauptung | Ist |
|---|---|---|
| SRS §4.3.3 / Traceability FR-J1939-007 | „DeadlineScheduler-Zeitgebung" für periodische PGNs | `Task.Delay`-Loop mit Drift (`J1939NodeImpl.cs:1042-1064`) |
| SRS FR-TP-002 | API `GetFramesAsync` | Tatsächlich `ReceiveAsync`/`ReceiveAllAsync` (`IIsoTpChannel.cs:64/84`) |
| SRS CON-008 | Standard-Branch `master` | HEAD ist `main` |
| SRS §4.3.1 (UDS) | — | kein „Ist-Zustand"-Absatz (alle anderen L3/L4-Abschnitte haben einen) |
| SRS Anhang Pkt. 4 | Traceability-Matrix gegen arc42 gegenzuprüfen „beim Erscheinen" | arc42 liegt vor; Gegenprüfung nicht dokumentiert erfolgt |
| arc42 ADR-9 | „teilweise umgesetzt" (Ownership) | zutreffend — offene Punkte = K1 (Periodic-TX-Lease) |
| csproj-Kommentare IsoTp/Uds | siehe § 6 | driftet vom Ist-Stand ab |

## 8. Jenseits der SRS: sinnvolle nächste Schritte

1. **Adoption/Doku (hoher Hebel):** `docs/getting-started.md` (+ `docs/zh/`) behandelt nur L0/L1; **keines** der 7 Samples nutzt ein `CanKit.Pro.*`-Paket. QuickStart-Samples je Protokoll (IsoTp-Roundtrip, UDS-DID, CANopen-Node, J1939-Claim — alles über `virtual://`) plus Getting-Started-Kapitel L2–L4.
2. **Release-Strategie 0.x → 1.0:** API-Stabilisierung mit Public-API-Tracking (z. B. PublicApiGenerator-Approved-Files), Versionierungs-/Breaking-Change-Policy, Kriterienkatalog pro Paket für `IsPackable=true` (Tests, Doku, Smoke).
3. **CI-Erweiterung:** macOS-Runner (NFR-002-Verifikation, `SleepCoarse`-Jitter-Gate), Code-Coverage-Reporting, Testlaufzeit-Optimierung (derzeit ~1 h je Workflow, volle Suite pro Paket), optional SocketCAN-`vcan`-Integrationstest auf Ubuntu-Runner (hardwarefreier echter Kernel-CAN-Pfad, Zwischenschritt Richtung HIL).
4. **Fehlerarchitektur-ADR:** einheitliche Exception-Basis/Fehlercodes über L2–L4 (NFR-006) oder bewusste Abweichung dokumentieren.
5. **HIL-Strategie konkretisieren (A-5):** Stichproben-Plan L4 gegen reale Geräte vor Produktivfreigabe; NFR-001-Hardware-BCM-Validierung darüber abbilden.
6. **Fachliche Vertiefungen (optional):** J1939-SPN-Katalog (derzeit nur pro-Aufruf-Skalierung, kein Definitions-Katalog), dynamisches PDO-Mapping (ohnehin Must, K3), CAN-FD-Testmatrix auch für UDS-over-FD, Arbitrary-Address-Claiming (J1939-81).
7. **SRS/arc42-Pflegeprozess:** Ist-Zustands-Absätze und Traceability-Matrix bei jedem Merge nachführen (SRS Anhang Pkt. 4 einlösen); FR-HAWE-005-Nachverfeinerung bei Spez-Verfügbarkeit vorsehen.

## 9. Anhang: Statusmatrizen je Anforderungs-ID

Legende: ✅ umgesetzt (Code+Test) · 🟡 teilweise/ungetestet (Details im Fließtext) · ❌ nicht umgesetzt · ➖ entfallen/zurückgestellt

| L2 (FR-RAW-) | 001 | 002 | 003 | 004 | 005 | 010 | 011 | 012 | 013 | 014 | 020 | 021 | 022 | 023 | 024 | 030 | 031 | 032 | 033 | 034 | 040 | 041 | 050 | 051 | 052 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| | ✅ | ✅ | ✅ | ✅ | 🟡 | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | 🟡 | ✅ | ✅ | ✅ | ✅ | 🟡 | ➖ |

| ISO-TP (FR-TP-) | 001 | 002 | 003 | 004 | 005 | 006 | 007 | 008 | 009 | 010 | 011 | 012 | 013 | 014 | 015 | 016 | 017 | 018 | 019 | 020 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| | 🟡 | ✅ | 🟡 | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | 🟡 | ✅ | ✅ | ➖ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |

| J1939-TP (FR-TP-) | 030 | 031 | 032 | 033 | 034 | 035 |
|---|---|---|---|---|---|---|
| | ✅ | 🟡 | 🟡 | ✅ | ✅ | ✅ |

| UDS (FR-UDS-) | 001 | 002 | 003 | 004 | 005 | 006 | 007 | 008 | 009 | 010 | 011 | 012 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | 🟡 | ✅ | ✅ | ✅ | 🟡 |

| CANopen (FR-CO-) | 001 | 002 | 003 | 004 | 005 | 006 | 007 | 008 | 009 | 010 | 011 | 012 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| | ✅ | ✅ | ✅ | ✅ | ❌/🟡 | 🟡 | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |

| J1939 (FR-J1939-) | 001 | 002 | 003 | 004 | 005 | 006 | 007 |
|---|---|---|---|---|---|---|---|
| | ✅ | ✅ | ✅ | 🟡 | ✅ | 🟡 | 🟡 |

| HAWE (FR-HAWE-) | 001 | 002 | 003 | 004 | 005 |
|---|---|---|---|---|---|
| | ✅ | ✅ | ✅ | ✅ | ➖ (Prozess) |

| NFR- | 001 | 002 | 003 | 004 | 005 | 006 | 007 | 008 | 009 | 010 | 011 | 012 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| | ✅ | 🟡 | ❌ (Verifikation) | ✅ | 🟡 | 🟡 | ❌ | 🟡 | ✅ | ✅ | ✅ | ✅ |

| CON- | 001 | 002 | 003 | 004 | 005 | 006 | 007 | 008 |
|---|---|---|---|---|---|---|---|---|
| | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | 🟡 | 🟡 |

---

**Prioritäten zusammengefasst:** P1 = K1 (UAF Periodic-TX), K2 (Release-Fähigkeit Pro), K3 (dynamisches PDO-Mapping), K4 (STmin-Verifikation), K5 (CAN-FD-Tests). P2 = K6–K11 (Verifikations-/Robustheitslücken J1939-TP-Timer, BusOff, NFR-008, P2*/EventTimer, SDO-Server-Timeout, macOS-CI). P3 = § 6 (Feldrobustheit, Hygiene). Darüber hinaus = § 8 (Adoption, Release-Strategie, CI, HIL).

---

## Nachtrag 2026-07-21: Umsetzungsstand nach Gap-Schließung (Phase A+B)

Am selben Tag wurden die P1-/P2-Lücken geschlossen. Stand der Nachtrag: alle genannten
Maßnahmen sind implementiert und durch die genannten Tests abgesichert (Suite lokal grün).

| # | Status | Umsetzung |
|---|---|---|
| K1 (UAF Periodic-TX) | **geschlossen** | `SoftwarePeriodicTx` (Ctor/`Update` duplizieren per `CanFrame.Duplicate(BufferAllocator)`, Freigabe bei Tausch/`Stop`; `Update` nach `Stop` wirft `CanBusDisposedException`), analog `ZlgPeriodicTx`/`ControlCanPeriodicTx` (Native-Pfade). Vector/PCAN/Kvaser-Fallbacks + `VirtualBus.TransmitPeriodic` über `SoftwarePeriodicTx` mitabgedeckt. Regressionstests `SoftwarePeriodicTxOwnershipTests.cs` (Poison-on-Dispose, mutationsverifiziert), arc42 §8.1 aktualisiert. |
| K2 (Release-Fähigkeit) | **geschlossen** | `eng/packages.json` um alle 10 Pro-Pakete ergänzt (Abhängigkeitsgraph, 6 experimentelle mit `publish:false` — von Pack-/Test-/Graph-Skripten unterstützt). Bereits vorhanden waren: Release-Notes `eng/release-notes/<Pro>/0.1.0.md`, CHANGELOG-Einträge, package-smoke-Pro-Referenzen, `$(CanKitPro*Version)`-Fallbacks. macOS-Job existierte bereits in `rawcan-ci.yml` (B6 damit belegt). |
| K3 (dyn. PDO-Mapping) | **geschlossen** | SDO-Zugriff auf 0x1600–0x1603/0x1A00–0x1A03 (CiA-301-Sequenz sub0=0 → Einträge → sub0=N, Read-back, strikte Abort-Codes inkl. neuer 0x06040041/0x06040042/0x06070010) in `CanOpenNode.PdoMapping.cs`. Tests `CanOpenDynamicMappingTests.cs` (TPDO/RPDO-Re-Mapping, 3 Abort-Pfade). |
| K4 (STmin/NFR-003) | **geschlossen** | `IsoTpStminTimingTests.cs` (CF-Spacing-Messung, weiche CI-Grenzen; STmin=0-Regression) + dokumentierte Genauigkeit in `CanKit.Pro.IsoTp/README.md`. |
| K5 (CAN-FD-Blindfläche) | **geschlossen** | `IsoTpCanFdTests.cs`: FD-SF-Escape, FD-MF 200 B, Long-FF 5000 B, FR-TP-003-Akzeptanz (nur `CanFd`-Frames für SF/FF/CF/FC), Classic-Längensweep 1..4095. |
| K6 (J1939-TP-Timer) | **geschlossen** | 6 neue Tests in `J1939TpTests.cs`: T1 BAM, T1 CM (inkl. Wire-Abort), T2 (Folge-CTS aus), T3-WaitEom, T4 (CTS(0)-Hold), CTS-Kappung an RTS-Maximum. |
| K7 (BusOff) | **geschlossen** | `TxConfirmTests`: sofortige BusOff-Auflösung ausstehender Confirms (Reflexions-Helfer `tests/CanKit.Tests/Utils/VirtualBusControl.cs`); `IsoTpBusOffTests.cs`: aktiver MF-Send faultet definiert mit BusOff-`IsoTpException` statt zu hängen. |
| K8 (NFR-008) | **geschlossen** | `CanRegistry` vollständig lock-synchronisiert (Reader-Snapshots); Stresstests `RawCanConcurrencyTests.cs` (parallele Registrierung vs. Leser; Subscribe/Dispose-Churn unter Verkehr). |
| K9 (P2*/EventTimer/CoS) | **geschlossen** | P2*-Timeout-Test (`UdsClientTests.cs`, `EcuResponsePendingThenSilent`-Sentinel, `Timer==P2Star` + Restart-Nachweis); TPDO-EventTimer-Test; Auto-CoS implementiert (`EnableChangeOfStateTpdo`, Default an; Echo-Guard über Actor-Thread-Erkennung; **Lastsicherheit:** Relevanz-Pre-Filter + Dirty-Set-Koaleszierung — ungebremstes Posten hätte die unbegrenzte Actor-Mailbox geflutet, Regression an `Tpdo_Emission_UnderConcurrentOdWrites_NeverTears` verifiziert). |
| K10 (SDO-Server-Timeout) | **geschlossen** | `SdoServerTimeout` (Default 5 s) armiert/re-armt klassische Server-Sessions; Ablauf → Session-Drop + Timeout-Abort auf dem Bus. Test mit verstummendem Raw-Client. |
| K11 (macOS-CI) | **bereits erledigt** | `rawcan-ci.yml` enthält seitdem einen `macos-latest`-Job (NFR-002); kein Handlungsbedarf. |

**Korrekturen zum ursprünglichen Review-Befundstand (Feststellung bei Umsetzung):**
Release-Notes 0.1.0.md für die vier L2-Pro-Pakete, package-smoke-Pro-Abdeckung, macOS-CI-Job
und die `publish`-Flag-Unterstützung in den Release-Skripten existierten bereits — das Review
hatte sie als fehlend bewertet (Stand: sie waren nach dem Review-Stichtag gelandet bzw. im
Review übersehen worden). Der einzige reale Release-Gap war `eng/packages.json`.

**Verbleibend offen (bewusst nicht Teil von Phase A+B):** P3-Liste §6 (SDO-Block-Retransmission,
FR-J1939-007 Fixed-Rate, FR-J1939-004 Arbitrary-Address, NFR-005-Plattform-Guards,
NFR-006-Fehlerarchitektur-ADR, NFR-007-Allokations-Benchmark, Hygiene-Punkte,
SRS/arc42-Doc-Drift-Bereinigung) sowie Phase D (§8: Samples/Getting-Started L2–L4,
Coverage/vcan-CI, 1.0-Fahrplan, HIL-Strategie).

---

## Nachtrag 2026-07-21 (2): Umsetzung Phase C (Robustheit, Hygiene, Doc-Drift)

Auch die P3-Liste wurde am selben Tag abgearbeitet:

| Plan-Item | Umsetzung |
|---|---|
| C1 SDO-Block-Retransmission | CiA-301-konformer Rewind auf das erste unbestätigte Segment mit **originalen Sub-Block-Seqnos** (kumulativ pro Sub-Block — ein Neustart bei 1 würde vom Peer endlos ge-NACKt), client- (Download) und serverseitig (Upload), gebunden über neue Option `SdoBlockMaxRetransmissions` (Default 3, 0 = altes Abbruchverhalten). Fake-Peer-Tests für Rewind (beide Richtungen) und Retry-Bound. |
| C2 FR-J1939-007 Fixed-Rate | `PeriodicSchedule` von `Task.Delay`-Schleife auf festes Zeitraster über `DeadlineScheduler` umgestellt (t0 + n × Periode; In-Flight-Ticks übersprungen, Nachhänge-Ticks koalesziert) — keine Drift durch Sendezeit mehr. Drift-sensitiver TP.BAM-Test; SRS §4.3.3-Text stimmt jetzt mit dem Code überein. |
| C3 FR-J1939-004 Arbitrary-Address | Fallback-Scan über das Arbitrary-Address-Feld (0x80..0xF7, einmalig wrapend) nach verlorenem Contest; Cannot-Claim erst bei Erschöpfung. Option `EnableArbitraryAddressClaiming` (Default: aus NAME-AAC-Bit). Tests: erfolgreiche Fallback-Claim sowie Erschöpfung („alle Adressen belegt", SRS-Verifikation). |
| C4 NFR-005 Plattform-Guards | `PlatformNotSupportedException` mit Adapter-Hinweis vor dem ersten P/Invoke in Kvaser/Vector/ControlCAN (Windows-only) und PCAN (Windows+Linux), `#if !FAKE`-geschützt. Smoke-Tests `AdapterPlatformGuardTests` (TestCaseProvider lädt Vendor-Assemblies jetzt, damit ihre Endpunkte im Testhost registriert sind). |
| C5 NFR-006 Fehlerarchitektur | ADR-12 in arc42 §9: alle L3/L4-Ausnahmen leiten von `CanKitException` ab; neue Fehlercodes `ProtocolTimeout=6002`, `ProtocolPeerAbort=6003`, `ProtocolNegativeResponse=6004`, `AddressClaimFailed=6005`; paketlokale Basistypen mit strukturierten Nutzdaten bleiben. Reflexions-/Mapping-Test `Nfr006ErrorArchitectureTests`. |
| C6 NFR-007 Allokations-Benchmark | `--alloc`-Modus im Benchmark-Sample (`GC.GetTotalAllocatedBytes` über alle Threads — BenchmarkDotNet-MemoryDiagnoser würde die Aktor-Threads nicht erfassen; bewusste Abweichung vom Plan-Wortlaut, keine neue Paketabhängigkeit). Baseline: L1 ~404 B/op, ISO-TP-SF ~2,1 kB/op, ISO-TP-MF(200 B) ~50 kB/op. |
| C7 Hygiene | `CanKitProHawe.slnf` Trailing-Comma; `RepositoryUrl` aller 19 csproj auf `dborgards/CanKit.Pro`; veraltete csproj-Kommentare (IsoTp/Uds); `_errorOccured`→`_errorOccurred`; `clock_nanosleep`-Catch-All → Log + dauerhafter Coarse-Sleep-Fallback (NFR-002); Demux-Prädikat-Exceptions jetzt über `ICanBusService.BackgroundExceptionOccurred` beobachtbar (Interface-Erweiterung, Wrapper angepasst, Test). |
| C8 Kleinere Testlücken | ISO-TP N_As direkt (`NeverConfirmService` honoriert das N_As-Timeout), RX-seitige Block-FC-Generierung (`LocalBlockSize>0`), UDS voller Upload-Zyklus + One-shot `UploadAsync`, SDO-Toggle-Negativtest, NMT ResetCommunication. |
| C9 Doc-Drift | SRS: §4.3.3 auf Fixed-Rate + Arbitrary-Address gezogen, §4.3.1-Ist-Zustand ergänzt, FR-TP-002 `GetFramesAsync`→`ReceiveAsync`, CON-008 `master`→`main`, Anhang Pkt. 4 Traceability-Gegenprüfung dokumentiert. arc42: ADR-9 auf „umgesetzt" (Ownership vollständig; einziger Design-Restpunkt deprecated `FrameReceived`), ADR-12 neu. READMEs (CANopen/J1939) nachgezogen. |

**Damit verbleiben offen:** nur Phase D (§8: Samples/Getting-Started L2–L4, Coverage/vcan-CI,
1.0-Fahrplan mit Public-API-Tracking, HIL-Strategie) sowie der dokumentierte Restpunkt pst>0
(Block→segmented-Fallback) und HAWE-Nachverfeinerung bei Spez-Verfügbarkeit.
