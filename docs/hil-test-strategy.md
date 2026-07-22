# HIL-Teststrategie (Hardware-in-the-Loop)

**Stand:** 2026-07-21 · Bezug: SRS Annahme A-5 (`docs/requirements/SRS-CanKit.md` §2.5),
Verifikationsstrategie (SRS §7), Gap-Review `docs/reviews/2026-07-21-implementation-gap-review.md`.

Die SRS fordert stichprobenhafte HIL-Validierung der L4-Stacks vor Produktivfreigabe
(Annahme A-5, explizit außerhalb des CI-Standardlaufs). Dieses Dokument legt Umfang,
Hardware-Matrix, Vorgehen und Abnahmekriterien fest.

## 1. Grundprinzip

CI bleibt hardwarefrei (Virtual-Loopback + Fake-Native). HIL ist ein **manuell ausgelöster,
dokumentierter Stichprobenlauf** gegen reale Bus-Hardware — keine Voraussetzung für jeden
Merge, aber Voraussetzung vor dem ersten Produktiveinsatz (`IsPackable=true` + 1.0) eines
L3/L4-Pakets (siehe `docs/release-1.0-criteria.md`).

## 2. Stufen

| Stufe | Was | Wann |
|---|---|---|
| S0 | Virtual-Loopback-Testsuite (CI-Standard) | jedes Push |
| S1 | SocketCAN auf Linux-`vcan` (echter Kernel-CAN-Pfad, `socketcan-ci.yml` Job „vcan") | CI auf relevanten Änderungen |
| S2 | HIL-Stichprobe mit echter Hardware (dieses Dokument) | vor `IsPackable=true` je L3/L4-Paket, danach vor jedem Major/Minor-Release mit Protokolländerungen |

## 3. Hardware-Matrix (Beispiel-Bestand)

| Rolle | Beispiel | Adapter |
|---|---|---|
| Host-Kanal 1+2 | Peak PCAN-USB (×2) | `CanKit.Adapter.PCAN` |
| Alternative | Kvaser Leaf (×2) | `CanKit.Adapter.Kvaser` |
| Alternative | Vector CANcaseXL (×2) | `CanKit.Adapter.Vector` |
| Linux-Bank | 2× CANable/SLCAN (SocketCAN) | `CanKit.Adapter.SocketCAN` |
| Gegenstelle UDS | reale ECU oder kommerzieller ECU-Simulator | — |
| Gegenstelle CANopen | reales CANopen-Gerät (z. B. CiA-401-E/A-Modul) | — |
| Gegenstelle J1939 | reales Nutzfahrzeug-Steuergerät oder J1939-Simulator | — |

Zwei Host-Kanäle werden über ein kurzes Twisted-Pair-Kabel mit 2 × 120 Ω Terminierung
verbunden; externe Gegenstelle hängt am selben Mini-Bus.

## 4. Stichproben je Stack (S2)

Jede Stichprobe wird mit Datum, Hardware, Firmware-/Treiberständen und Ergebnis in
`docs/reviews/hil/` (eine kurze Markdown-Datei je Lauf) protokolliert.

### 4.1 ISO-TP (FR-TP-001..020)
- SF- und Multi-Frame-Roundtrip zwischen zwei Host-Kanälen (1, 7, 200, 4095 Byte; Classic
  und, falls Hardware-FD-fähig, CAN FD).
- STmin-Genauigkeit: Peer-FC mit STmin 1 ms/5 ms/20 ms, gemessene CF-Abstände gegen die in
  `src/transports/CanKit.Pro.IsoTp/README.md` dokumentierte Genauigkeit (NFR-003).
- Gegen reale Diagnose-ECU: `ReadDataByIdentifier` (0x22) einer bekannten DID,
  mehrfach mit NRC 0x78-Verhalten, falls die ECU es zeigt.

### 4.2 UDS (FR-UDS-001..010)
- Reale Diagnosesitzung: Session-Wechsel (0x10), DID-Read (0x22), TesterPresent-Kette (0x3E)
  über ≥ 10 min ohne Session-Timeout, P2/P2*-Verhalten der echten ECU.
- Negative-Pfad: absichtlich ungültige DID → strukturierte NRC-Weiterreichung (0x31).

### 4.3 CANopen (FR-CO-001..012)
- Gegen ein reales CANopen-Gerät: NMT-Start/Stop/Reset, SDO-Read/Write (expedited +
  segmented), dynamisches PDO-Re-Mapping per SDO (0x1600/0x1A00), SYNC-getriggerte PDOs.
- Heartbeat-Verlust: Gerät stoppen → Timeout-Ereignis in konfigurierter Frist.
- Optional: Block-Transfer großer Werte (> 512 Byte) inkl. Teilverlust durch kurzes
  Abklemmen (Retransmission beobachten).

### 4.4 J1939-TP + J1939 (FR-TP-030..035, FR-J1939-001..007)
- TP.BAM und TP.CM zwischen zwei Host-Kanälen (9, 112, 300, 1785 Byte).
- Address Claiming mit zwei Nodes gleicher bevorzugter Adresse (Konfliktauflösung nach
  NAME) und Arbitrary-Address-Fallback gegen eine belegte Adressliste.
- Periodische PGN: gemessene Inter-Frame-Zeit gegen konfigurierte Rate (kein Drift).

### 4.5 NFR-001 Hardware-Validierung
- `SoftwarePeriodicTx`-Jitter (p99) auf dem HIL-Host gegen Bus-Last ~0 % und ~50 %, über
  ≥ 500 Perioden — Abgleich mit dem SRS-Zielband (p99 ≤ 1,0 ms bei Periode ≥ 1 ms auf
  Referenzhosts) und Dokumentation des Ergebnisses.
- Hardware-periodic-TX (PCAN/Kvaser/Vector nativ, SocketCAN-BCM): Periode gegen Oszi oder
  zweiten Kanal mit Hardware-Zeitstempeln vermessen.

## 5. Abnahmekriterien

- Alle Stichprobenpunkte des betroffenen Pakets **bestanden** oder als begründete
  Abweichung dokumentiert (mit Ticket/ADR-Verweis).
- Kein unerklärter Frame-Verlust, kein Protokoll-Timeout außerhalb der dokumentierten
  Grenzen, keine nicht behandelten Ausnahmen (alle Fehler kommen über die
  `CanKitException`-Hierarchie bzw. `BackgroundExceptionOccurred` — ADR-12).
- Protokoll des Laufs liegt unter `docs/reviews/hil/` vor.

## 6. Nicht-Ziele

- Dauerhafte HIL-Racks im CI (Kosten/Flake-Risiko); Echtzeit-Garantien auf
  General-Purpose-OS (siehe NFR-001-Dokumentation der Softwarepfade); HAWE-Protokolltests
  (vertrauliche Spezifikation, CON-006).
