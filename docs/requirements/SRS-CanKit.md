# Software Requirements Specification – CanKit Protokoll-Stacks

**Dokumenttyp:** Software Requirements Specification (SRS), angelehnt an ISO/IEC/IEEE 29148
**Methodik:** IREB-Grundprinzipien, MASTeR-Satzschablonen, MoSCoW-Priorisierung
**Projekt:** CanKit – .NET-CAN-Bus-Bibliothek
**Stand:** 2026-07-14 · Bezug: `master` @ `36866ff`, Review `docs/reviews/2026-07-14-deep-code-review.md`
**Status:** Entwurf

---

## 1. Einleitung

### 1.1 Zweck des Dokuments

Dieses Dokument spezifiziert die Anforderungen an CanKit, um auf der bestehenden Raw-CAN-Bibliothek eine gehärtete **Raw-CAN-Dienstebene** sowie darauf aufbauende **Transport-** und **Anwendungsprotokoll-Stacks** zu realisieren. Es dient als verbindliche Grundlage für Architektur (`docs/architecture/arc42-CanKit.md`, parallel in Erstellung), Implementierung und Abnahmetests.

### 1.2 Geltungsbereich (Scope)

Das Dokument spezifiziert:

- **L2 Raw-CAN-Dienstebene** (neu zu bauen) – die Härtung und Erweiterung von `ICanBus`/`CanFrame` um Multi-Consumer-Demultiplexing, einen verbindlichen Frame-Ownership-Vertrag, TX-Bestätigung, Adressierungs-Helfer und ein einheitliches Threading-Modell je Protokollinstanz.
- **L3 Transport-Ebene** – Fertigstellung von ISO-TP (ISO 15765-2) sowie Neubau von J1939-TP (TP.BAM/TP.CM).
- **L4 Anwendungsprotokoll-Ebene** – UDS (ISO 14229 über ISO-TP), CANopen (CiA 301), J1939-Applikation (SAE J1939/ISO 11783), sowie ein generischer Rahmen für ein proprietäres HAWE-Privatprotokoll.

Nicht im Scope: Neuimplementierung von L0 (Vendor-Adapter) und der bereits vorhandenen L1-Kernfunktionalität (`ICanBus`, `CanFrame`, `CanRegistry`) – diese werden als gegeben vorausgesetzt und nur dort referenziert, wo L2 auf ihnen aufsetzt oder bestehende Defekte (siehe Review) die L2-Anforderungen begründen.

### 1.3 Definitionen, Akronyme, Abkürzungen

| Begriff | Bedeutung |
|---|---|
| CAN | Controller Area Network (ISO 11898) |
| CAN-FD | CAN with Flexible Data-Rate (ISO 11898-1) |
| ISO-TP | ISO 15765-2, Transportprotokoll für Multi-Frame-PDUs über CAN |
| PDU | Protocol Data Unit |
| SF/FF/CF/FC | Single Frame / First Frame / Consecutive Frame / Flow Control (ISO-TP-PCI-Typen) |
| STmin | Separation Time Minimum (ISO-TP Flow-Control-Parameter) |
| N_As/N_Bs/N_Cr | ISO-TP-Zeitüberwachungen (Sender-, Empfänger-seitige Deadlines) |
| UDS | Unified Diagnostic Services (ISO 14229) |
| DID | Data Identifier (UDS) |
| P2/P2* | UDS-Antwortzeiten (Server-Timing, Default bzw. erweitert nach `0x78`) |
| NRC | Negative Response Code (UDS) |
| SDO/PDO | Service/Process Data Object (CANopen, CiA 301) |
| NMT | Network Management (CANopen) |
| EMCY | Emergency Object (CANopen) |
| OD | Objektverzeichnis (Object Dictionary, CANopen) |
| PGN/SPN | Parameter Group Number / Suspect Parameter Number (SAE J1939) |
| TP.BAM/TP.CM | Broadcast Announce Message / Connection Mode (J1939-Transportprotokoll) |
| CTS/RTS | Clear To Send / Request To Send (J1939-TP) |
| Address Claiming | J1939-Adressvergabeverfahren |
| SID | Service-ID |
| ECU | Electronic Control Unit (Steuergerät) |
| SPI | Service Provider Interface (hier: CanKit-interne Erweiterungsschnittstellen, nicht „Serial Peripheral Interface“) |
| HIL | Hardware-in-the-Loop |
| MoSCoW | Must/Should/Could/Won't-Priorisierung |
| L0…L4 | Kanonische Schichtnomenklatur dieses Projekts, siehe Abschnitt 2.1 |

### 1.4 Referenzen

- ISO 11898-1/-2: Controller Area Network (CAN), CAN-FD
- ISO 15765-2: Road vehicles – Diagnostic communication over CAN – Part 2: Transport protocol and network layer services (ISO-TP)
- ISO 15765-4: Diagnostic communication over CAN – Part 4: Requirements for emissions-related systems (UDS on CAN)
- ISO 14229-1: Unified Diagnostic Services (UDS) – Specification and requirements
- SAE J1939 / ISO 11783: Serial Control and Communications Vehicle Network (Nutzfahrzeuge/Agrartechnik)
- CiA 301: CANopen Application Layer and Communication Profile
- CiA 302: CANopen Additional Application Layer Functions (Netzwerkmanagement, Boot-up)
- HAWE-Privatprotokoll: proprietär, vertraulich, extern verwaltet – in dieser SRS nur als generischer Rahmen mit Erweiterungspunkten spezifiziert (siehe Annahme A-6)
- `docs/reviews/2026-07-14-deep-code-review.md` – Ist-Zustands-Review, Quelle der L2-Architekturlücken
- ISO/IEC/IEEE 29148:2018 – Requirements Engineering
- IREB CPRE-Lehrplan (Satzschablonen, MoSCoW)

---

## 2. Gesamtbeschreibung

### 2.1 Produktperspektive – Schichtenmodell

CanKit wird in fünf Ebenen strukturiert. Diese Nomenklatur ist verbindlich und wird identisch im Architekturdokument verwendet:

| Ebene | Name | Inhalt | Ist-Zustand |
|---|---|---|---|
| L0 | Adapter-Ebene | 7 Vendor-Adapter (SocketCAN, ZLG, PCAN, Kvaser, Vector, ControlCAN, Virtual) + Fake-Native-Schicht für CI | **vorhanden**, produktionsnah |
| L1 | Raw-CAN-Kern | `ICanBus`, `CanFrame`/`CanFrameView`, `ITransceiver`, `ICanDevice`, `IPeriodicTx`, `CanRegistry`, Utilities (`AsyncFramePipe`, `QueuedTxCanBus`, `SoftwarePeriodicTx`, `PreciseDelay`, `BitTimingSolver`) | **vorhanden**, mit bekannten Defekten (siehe Review) |
| L2 | Raw-CAN-Dienstebene | Multi-Consumer-Demultiplexing, Frame-Ownership-Vertrag, TX-Bestätigung, Adressierungs-Helfer, Threading-Modell je Protokollinstanz, Fehler-/Timeout-Infrastruktur | **neu zu bauen** – Gegenstand dieser SRS |
| L3 | Transport-Ebene | ISO-TP (ISO 15765-2), J1939-TP (TP.BAM/TP.CM) | ISO-TP: **MVP vorhanden** (`CanKit.Pro.IsoTp`); J1939-TP: **MVP vorhanden** (`CanKit.Pro.J1939Tp`); Legacy-`CanKit.Transport.IsoTp` entfernt |
| L4 | Anwendungsprotokoll-Ebene | UDS (auf ISO-TP), CANopen, J1939 (Applikation), HAWE-Privatprotokoll | **nicht vorhanden** |

L2 ist die vom Auftraggeber geforderte zusätzliche „raw-CAN“-Schicht: Sie kapselt alles, was mehrere Protokoll-Stacks gemeinsam benötigen und was heute nicht (oder nicht korrekt) in L1 existiert.

### 2.2 Produktfunktionen (Übersicht)

- Gehärteter, Multi-Consumer-fähiger Zugriff auf einen physischen CAN-Bus für mehrere gleichzeitig aktive Protokollinstanzen (z. B. UDS-Client + CANopen-Master auf demselben Kanal).
- Vollständiges, spezifikationskonformes ISO-TP für Diagnose- und generische PDU-Übertragung.
- J1939-Transportprotokoll für PGN-basierte Nutzfahrzeugkommunikation.
- UDS-Client-Funktionalität für Diagnosewerkzeuge/Testautomatisierung.
- CANopen-Basisdienste (SDO, PDO, NMT, Heartbeat, EMCY) für Automatisierungsanwendungen.
- J1939-Applikationsschicht (PGN/SPN-Zugriff, Address Claiming, Request-PGN).
- Erweiterungsrahmen für das HAWE-Privatprotokoll.

### 2.3 Nutzer-/Stakeholder-Charakteristik

Siehe Abschnitt 3.

### 2.4 Randbedingungen (Übersicht)

Siehe Abschnitt 6 (`CON-xxx`). Zentral: Multi-Targeting (netstandard2.0, net8.0, net8.0-windows), P/Invoke-basierte Vendor-SDKs, plattformabhängiges Zeitverhalten.

### 2.5 Annahmen & Abhängigkeiten

| ID | Annahme/Abhängigkeit |
|---|---|
| A-1 | Die vier in Abschnitt 4.1 hergeleiteten L2-Architekturlücken (Frame-Ownership, Demultiplexing, Threading-Modell, TX-Confirm) müssen geschlossen sein, bevor L3/L4-Stacks produktiv gebaut werden; L3/L4-Anforderungen in dieser SRS setzen eine funktionsfähige L2 voraus. |
| A-2 | Der defekte ISO-TP-Prototyp (`CanKit.Transport.IsoTp`) wurde zugunsten von `CanKit.Pro.IsoTp` (Codec + aktorgetriebene Runtime auf L2) ersetzt und aus dem Tree entfernt; die Review-§1.1-Defekte gelten als durch den Neubau adressiert. |
| A-3 | J1939-TP wird als eigenständiger Transport (`CanKit.Pro.J1939Tp`) nach demselben L2-Kompositionsmuster wie `CanKit.Pro.IsoTp` realisiert. |
| A-4 | Vendor-SDK-Lizenzen (Peak PCANBasic, Kvaser CANlib, Vector XL-Driver) bleiben proprietär und werden nicht Teil dieser Spezifikation; nur die Integrationspunkte werden betrachtet. |
| A-5 | HIL-Testinfrastruktur mit realer Hardware wird für Abnahmetests der L3/L4-Ebenen benötigt, ist aber nicht Gegenstand dieser SRS (siehe Abschnitt 7). |
| A-6 | Das HAWE-Protokoll ist vertraulich und dem Requirements-Team nicht im Detail bekannt; entsprechende Anforderungen (`FR-HAWE-xxx`) sind bewusst generisch als Rahmen mit Erweiterungspunkten formuliert und müssen bei Verfügbarkeit der Protokollspezifikation verfeinert werden. |
| A-7 | `netstandard2.0` bleibt Ziel-TFM für Kernbibliothek und L2/L3, um .NET-Framework-Konsumenten (Windows-Altsysteme mit Kvaser/Vector/PCAN) zu unterstützen. |

---

## 3. Stakeholder & Akteure

| Akteur | Rolle |
|---|---|
| **Applikationsentwickler** | Nutzt CanKit-APIs (L1–L4), um Anwendungen/Werkzeuge auf CAN-Bussen zu bauen. |
| **Diagnose-Tester (UDS-Anwendung)** | Nutzt L4-UDS-API, um Steuergeräte zu diagnostizieren (Read/Write DID, Routinen, Sicherheit). |
| **Steuergerät (ECU, extern)** | Kommunikationspartner auf dem Bus; kein CanKit-Nutzer, aber Quelle von Antworten/Fehlerbedingungen, gegen die CanKit robust sein muss. |
| **CANopen-Master/-Node-Anwendung** | Nutzt L4-CANopen-API für NMT/PDO/SDO-Interaktion. |
| **J1939-Anwendung** | Nutzt L4-J1939-API für PGN/SPN-Zugriff und Address Claiming. |
| **HAWE-Integrationsanwendung** | Nutzt den generischen HAWE-Rahmen für ein proprietäres Frame-Protokoll. |
| **Bibliotheks-Maintainer** | Verantwortlich für Architektur, Code-Qualität, SPI-Erweiterbarkeit, Release-Prozess. |
| **CI/Testautomatisierung** | Führt Unit-, Virtual-Loopback- und (wo verfügbar) HIL-Tests automatisiert aus; Konsument der Fake-Native-Schicht. |

---

## 4. Funktionale Anforderungen

### 4.1 L2 – Raw-CAN-Dienstebene (NEU)

Die folgenden Anforderungen leiten sich direkt aus den vier im Review identifizierten Architekturlücken ab (Review, Gesamteinschätzung und §2.1–2.3), die vor dem Bau der Protokoll-Stacks (L3/L4) geschlossen werden müssen.

#### 4.1.1 Frame-Ownership- und Lifetime-Vertrag

Hintergrund: Review §1.5 und §2.1 zeigen, dass `CanFrame.Dispose()` das `ownMemory`-Flag ignoriert und Frames ungeklärt zwischen Sender, Broadcast-Hub, Event-Handlern und Async-Pipes geteilt werden, was zu Use-after-free/Double-Dispose führt.

| ID | Anforderung | Priorität | Verifikation | Quelle |
|---|---|---|---|---|
| FR-RAW-001 | Das System MUSS einen verbindlichen, dokumentierten Frame-Ownership-Vertrag definieren, der für jede Frame-Übergabestelle (TX-Aufrufer→Adapter, Adapter-RX→Event/Pipe/Hub, Hub→Multi-Consumer) eindeutig festlegt, wer den Speicher besitzt und wann `Dispose()` aufgerufen werden darf. | Must | Dokumentationsreview + Architektur-Entscheidungsprotokoll; Vertrag ist in `docs/architecture/arc42-CanKit.md` referenzierbar. | Review §2.1, Empfehlung Pkt. 2 |
| FR-RAW-002 | Das System MUSS sicherstellen, dass `CanFrame.Dispose()` den zugrunde liegenden Speicher nur freigibt, wenn die Frame-Instanz laut Ownership-Vertrag Eigentümerin ist (Korrektur des in Review §1.5 beschriebenen Defekts). | Must | Unit-Test: Frame mit `ownMemory=false` erzeugen, `Dispose()` aufrufen, prüfen dass der zugrunde liegende `IMemoryOwner<byte>` nicht disposed wurde. | Review §1.5 |
| FR-RAW-003 | Das System MUSS dem Protokollentwickler die Möglichkeit bieten, empfangene Frames über eine reine Lesesicht (`CanFrameView`) zu konsumieren, ohne Ownership oder Dispose-Verantwortung zu übernehmen. | Must | Unit-Test: `FrameObserved`-Handler erhält `CanFrameView`; Aufruf von `Dispose()` ist auf dem View-Typ nicht möglich (Compile-Zeit-Prüfung). | Review §2.1; vorhandene `CanFrameView` in `ICanBus.FrameObserved` |
| FR-RAW-004 | Das System MUSS beim Multi-Consumer-Broadcast (z. B. Virtual-Bus-Hub) jedem Consumer eine eigenständig lebensfähige Kopie oder eine referenzgezählte, erst nach Konsum aller Empfänger freigegebene Instanz liefern, sodass das Dispose eines Consumers die Daten anderer Consumer/des Senders nicht invalidiert. | Must | Integrationstest (Virtual-Loopback): 2 Consumer + Sender abonnieren denselben Frame; ein Consumer disposed sofort, die übrigen lesen danach unverändert weiter. | Review §2.1 (VirtualBusHub-Use-after-free) |
| FR-RAW-005 | Das System SOLLTE für TX-Pfade dokumentieren und durchsetzen, dass der Aufrufer Eigentümer des übergebenen Frames bleibt, sofern der Adapter keine Kopie anfertigt, und dass Adapter interne Kopien anfertigen, bevor sie den Frame asynchron weiterverwenden (Scheduler-Queues, Echo-Matching). | Should | Codereview-Checkliste je Adapter/Transport; Regressionstest für ISO-TP-Scheduler-Echo-Matching (Review §2.1, Punkt „Scheduler (ISO-TP)“). | Review §2.1 |

#### 4.1.2 Multi-Protokoll-Demultiplexing (Subscription/Filterung)

Hintergrund: Heute können mehrere Protokollinstanzen (z. B. ISO-TP + CANopen) nicht unabhängig voneinander gefilterte Sichten auf denselben `ICanBus` erhalten, ohne sich gegenseitig Frames wegzunehmen oder Callbacks zu blockieren.

| ID | Anforderung | Priorität | Verifikation | Quelle |
|---|---|---|---|---|
| FR-RAW-010 | Das System MUSS dem Protokollentwickler die Möglichkeit bieten, eine unabhängige, gefilterte Frame-Subscription (ID-Bereich, Maske oder Prädikat) auf einem gemeinsam genutzten `ICanBus` zu registrieren, ohne andere Subscriptions zu beeinträchtigen. | Must | Integrationstest: 2 Subscriptions mit disjunkten ID-Filtern auf demselben Virtual-Bus; jede erhält ausschließlich die zu ihr passenden Frames. | Review, Gesamteinschätzung („Multi-Protokoll-Demultiplexing“) |
| FR-RAW-011 | Das System MUSS sicherstellen, dass jede Subscription einen eigenen, unabhängigen Puffer/Pipe besitzt, sodass eine langsame oder blockierte Subscription den Empfang anderer Subscriptions oder des Basis-`FrameObserved`-Events nicht verzögert. | Must | Lasttest: eine Subscription verarbeitet künstlich langsam, zweite Subscription bleibt in Latenz/Durchsatz unbeeinträchtigt. | Abgeleitet aus Architekturlücke „Demultiplexing“ |
| FR-RAW-012 | Das System MUSS eine geordnete Ab-/Wiederanmeldung von Subscriptions (Dispose-Pattern) unterstützen, sodass beendete Protokollinstanzen (z. B. ISO-TP-Kanal geschlossen) keine Ressourcen (Threads, Puffer, Hub-Einträge) hinterlassen. | Must | Unit-Test: N Subscriptions erzeugen und disposen, danach Ressourcenzähler (Handles/Threads) unverändert gegenüber Ausgangszustand. | Review §2.4 (Virtual: `VirtualBusHub._hubs` Leak als Negativbeispiel) |
| FR-RAW-013 | Das System SOLLTE eine Standard-Demultiplex-Strategie für den häufigen Fall „ein 11/29-Bit-CAN-ID-Bereich pro Protokollinstanz“ bereitstellen (Fast-Path ohne generisches Prädikat), um Overhead bei hoher Busfrequenz zu vermeiden. | Should | Performance-Test: Durchsatzvergleich ID-Bereichsfilter vs. generisches Prädikat bei ≥ 1000 Frames/s. | NFR-Performance, Abschnitt 5.1 |
| FR-RAW-014 | Das System KANN dem Protokollentwickler erlauben, Subscriptions zur Laufzeit umzukonfigurieren (Filterkriterien ändern), ohne die Subscription neu zu erzeugen. | Could | Unit-Test: Filterkriterium zur Laufzeit ändern, nachfolgende Frames folgen neuem Kriterium (`RawCanSubscriptionTests.Reconfigure_Filter_At_Runtime_Subsequent_Frames_Follow_New_Criterion`). | — |

#### 4.1.3 Threading-/Aktor-Modell pro Protokollinstanz

Hintergrund: Review §1.1 Punkt 9/14 und §2.2/§2.3 zeigen unsynchronisierte Zugriffe auf geteilte Zustände (Scheduler-Listen, `_tx`-State, Timer-Kontexte) aus mehreren Threads sowie einen Busy-Loop-Scheduler ohne definiertes Nebenläufigkeitsmodell.

| ID | Anforderung | Priorität | Verifikation | Quelle |
|---|---|---|---|---|
| FR-RAW-020 | Das System MUSS für jede Protokollinstanz (z. B. einen ISO-TP-Kanal, einen CANopen-Node) ein definiertes, dokumentiertes Threading-Modell bereitstellen, das festlegt, auf welchem Thread/Kontext RX-Verarbeitung, TX-Scheduling und Timeout-Prüfung stattfinden. | Must | Architektur-Review + Nebenläufigkeitstest (stress test mit ThreadSanitizer-Äquivalent/`Interlocked`-Zähler) ohne Datenrennen-Berichte. | Review §1.1 Punkt 9, 14; Gesamteinschätzung „Threading-Modell“ |
| FR-RAW-021 | Das System MUSS interne, von mehreren Threads erreichbare Zustände einer Protokollinstanz (Kanal-Register, Zeitgeber-Queues, Zustandsautomaten) gegen nebenläufige Lese-/Schreibzugriffe absichern (z. B. Lock, Concurrent-Collection oder Single-Writer-Aktor). | Must | Unit-/Stresstest: paralleles `AddChannel`/`RemoveChannel` und laufende Verarbeitungsschleife ohne `InvalidOperationException` bei Enumeration. | Review §1.1 Punkt 14 (`Router._channels`, `IsoTpScheduler._channels` als `List` ohne Synchronisation) |
| FR-RAW-022 | Das System MUSS einen ereignisgetriebenen (nicht Busy-Loop-basierten) Scheduling-Mechanismus für periodische/zeitgesteuerte Protokollaufgaben (z. B. STmin-Wartezeiten, Timeout-Prüfung) bereitstellen, der bei fehlender Arbeit blockiert statt CPU zu verbrauchen. | Must | Performance-Test: CPU-Auslastung eines inaktiven Kanals über 60 s < 1 % (Referenzwert, anzupassen an Zielplattform). | Review §1.1 Punkt 9 (100 %-CPU-Busy-Loop des `IsoTpScheduler`) |
| FR-RAW-023 | Das System MUSS Hintergrundausnahmen einer Protokollinstanz konsistent über einen definierten Kanal (Event/Callback) an den Nutzer weiterreichen, statt sie im Aufrufer-Thread des auslösenden Adapters zu werfen oder als unbeobachtete Task-Exception zu verlieren. | Must | Unit-Test: künstlich ausgelöste Hintergrundausnahme wird über `BackgroundExceptionOccurred`-Äquivalent der Protokollinstanz beobachtet, RX-Loop des Adapters bleibt unbeeinträchtigt. | Review §1.1 Punkt 15 |
| FR-RAW-024 | Das System SOLLTE dem Protokollentwickler erlauben, den Ausführungskontext (z. B. dedizierter Thread vs. Thread-Pool vs. Nutzer-`SynchronizationContext`) je Protokollinstanz zu konfigurieren. | Should | Konfigurationstest: Instanz mit dediziertem Thread erzeugt, Callback wird nachweislich auf diesem Thread ausgeführt. | Abgeleitet aus Threading-Modell-Anforderung |

#### 4.1.4 TX-Bestätigungs-Abstraktion (TX-Confirm)

Hintergrund: Zustandsautomaten in L3/L4 (z. B. ISO-TP-Sendepfad, CANopen-SDO) benötigen eine verlässliche Aussage, wann ein Frame tatsächlich auf dem Bus gesendet wurde – nicht nur, dass er vom Treiber angenommen wurde. `ICanBus.Transmit` liefert nur die Anzahl akzeptierter Frames (`ITransceiver.Transmit`), keine Sende-Bestätigung; Echo-Unterstützung ist optional (`CanFeature.Echo`).

| ID | Anforderung | Priorität | Verifikation | Quelle |
|---|---|---|---|---|
| FR-RAW-030 | Das System MUSS dem Protokollentwickler eine einheitliche TX-Bestätigungs-Abstraktion bieten, die unabhängig davon funktioniert, ob der zugrunde liegende Adapter Hardware-Echo (`CanFeature.Echo`) unterstützt. | Must | Integrationstest gegen zwei Adapter-Konfigurationen (mit/ohne `CanFeature.Echo`), jeweils erfolgreicher TX-Confirm-Callback. | Review, Gesamteinschätzung „TX-Confirm-Abstraktion“; `CanFeature.Echo` in `CanEnums.cs` |
| FR-RAW-031 | Das System MUSS bei Adaptern mit Echo-Unterstützung die tatsächliche Sendebestätigung (Echo-Frame) für das TX-Confirm nutzen und dabei Frames anhand geeigneter Kriterien (z. B. ID + Payload + Sequenznummer/Zeitfenster) korrekt dem ursprünglichen Sende-Aufruf zuordnen. | Must | Integrationstest (Virtual-Loopback mit Echo aktiviert): N gleichzeitig gesendete, inhaltsgleiche Frames werden korrekt einzeln bestätigt (kein Fehlmatching). | Review §1.1 Punkt 12 (`QueuedDeadline.Enqueue` crasht bei identischen Frames) als Negativbeispiel |
| FR-RAW-032 | Das System MUSS bei Adaptern ohne Echo-Unterstützung eine dokumentierte Approximation des TX-Confirm bereitstellen (z. B. „angenommen vom Treiber“ als bestmögliche Bestätigung), die diesen Unterschied explizit an den Aufrufer kommuniziert (kein stillschweigendes Vortäuschen von Hardware-Bestätigung). | Must | Unit-Test: TX-Confirm-Ergebnis enthält Bestätigungsart (Echo vs. Treiber-Akzeptanz); Dokumentationsprüfung. | Abgeleitet aus Architekturlücke „TX-Confirm“ |
| FR-RAW-033 | Das System MUSS TX-Confirm-Fehlschläge (Timeout, Bus-Off, Ablehnung) als beobachtbares Ergebnis (z. B. fehlgeschlagenes Future/Task) an den Aufrufer melden, statt den Vorgang unbestimmt hängen zu lassen. | Must | Unit-Test: TX-Confirm-Timeout-Szenario liefert innerhalb der konfigurierten Frist ein Fehlerergebnis. | Review §1.1 Punkt 8, 10 (hängende Tasks bei ISO-TP als Negativbeispiel) |
| FR-RAW-034 | Das System SOLLTE dem Protokollentwickler erlauben, das TX-Confirm-Timeout je Sendevorgang zu konfigurieren. | Should | Konfigurationstest: unterschiedliche Timeout-Werte führen zu proportional unterschiedlichem Fehlschlagzeitpunkt. | — |

#### 4.1.5 Adressierungs-/ID-Helfer

| ID | Anforderung | Priorität | Verifikation | Quelle |
|---|---|---|---|---|
| FR-RAW-040 | Das System MUSS dem Protokollentwickler Hilfsfunktionen zur Erzeugung, Validierung und Zerlegung von 11-Bit-(Standard-) und 29-Bit-(Extended-)CAN-IDs bereitstellen, einschließlich der für J1939 nötigen PGN/Priority/Source-Address-Aufteilung der 29-Bit-ID. | Must | Unit-Tests: Roundtrip Kodierung/Dekodierung für repräsentative Standard-, Extended- und J1939-PGN-IDs inkl. Grenzwerte (0, 0x7FF, 0x1FFFFFFF). | Gesamtbeschreibung, Auftrag „Adressierungs-/ID-Helfer“ |
| FR-RAW-041 | Das System SOLLTE dem Protokollentwickler Hilfsfunktionen zur Erkennung von ID-Kollisionen/Überlappungen zwischen registrierten Subscriptions anbieten (z. B. zur Fehlerdiagnose bei Fehlkonfiguration mehrerer Protokollinstanzen). | Should | Unit-Test: zwei überlappende Filterbereiche werden erkannt und gemeldet. | Abgeleitet |

#### 4.1.6 Fehler-/Timeout-Infrastruktur

| ID | Anforderung | Priorität | Verifikation | Quelle |
|---|---|---|---|---|
| FR-RAW-050 | Das System MUSS eine wiederverwendbare Deadline-/Timeout-Primitive bereitstellen, die von L3/L4-Protokollen für zeitgebundene Zustandsübergänge (z. B. N_Bs, N_Cr, P2, SDO-Timeout) genutzt werden kann und deren Ablauf zuverlässig geprüft und gemeldet wird. | Must | Unit-Test: Deadline mit kurzer Frist löst nachweislich Timeout-Callback aus; Regressionstest gegen Review-Befund „Deadlines werden gepflegt, aber nie geprüft“. | Review §1.1 Punkt 10 |
| FR-RAW-051 | Das System MUSS Bus-Fehlerzustände (`BusState`: `ErrWarning`, `ErrPassive`, `BusOff`) den L3/L4-Protokollinstanzen so zur Verfügung stellen, dass diese aktive Übertragungen kontrolliert abbrechen oder pausieren können. | Must | Integrationstest: simulierter `BusOff`-Zustand führt bei aktivem L3-Sendevorgang zu definiertem Abbruch statt Hängenbleiben. | Abgeleitet aus `ICanBus.BusState`, `ErrorCounters()` |
| FR-RAW-052 | Das System SOLLTE reservierte/ungültige Protokollwerte in eingehenden Frames (z. B. reservierte ISO-TP-STmin-Werte) gemäß Spezifikation robust behandeln, statt mit einer Ausnahme abzubrechen. | Should | Unit-Test analog Review §1.1 Punkt 6 (reservierte STmin-Werte 0x80–0xF0/0xFA–0xFF werden als 127 ms interpretiert statt Exception). | Review §1.1 Punkt 6 |

### 4.2 L3 – Transport-Ebene

#### 4.2.1 ISO-TP (ISO 15765-2)

Ist-Zustand: MVP umgesetzt als `CanKit.Pro.IsoTp` (`src/transports/CanKit.Pro.IsoTp`, `IsPackable=false`).
Der Legacy-Prototyp `CanKit.Transport.IsoTp` sowie die zugehörige Abstractions-`API/Transport`-Oberfläche
(inkl. Namespace-Typo `Excpetions`), `IIsoTpRegister` und der PCAN-native ISO-TP-Register-Pfad
(`PcanIsoTp*`) wurden entfernt. ISO-TP läuft ausschließlich über `CanKit.Pro.IsoTp` auf den L2-Diensten
(RawCan-Demux, `SendConfirmed`, `IProtocolActor`, `IDeadlineScheduler`).

> **Abdeckung:** Codec-Unit-Tests decken **FR-TP-003..007, FR-TP-015, FR-RAW-052** ab; Virtual-Loopback-
> Integrationstests decken **FR-TP-001/002/008..012/016..018** ab. **FR-TP-013** (netstandard2.0-
> `TryPeek`-Polyfill im alten TX-Queue-Ablauf) entfällt mit dem Legacy-Paket — `CanKit.Pro.IsoTp`
> nutzt `SendConfirmed` + `SemaphoreSlim`-Send-Gate. **FR-TP-014** ist durch L2-`SendConfirmed`
> (FIFO-Matching je (ID, Payload)) abgedeckt. **FR-TP-019** (Functional Addressing, Could) ist implementiert: `IsoTpFunctionalClient` / `IsoTp.OpenFunctional` — Integrationstest `IsoTpFunctionalClientTests` verifiziert SF-Anfrage mit Antworten von ≥ 2 simulierten ECUs.

| ID | Anforderung | Priorität | Verifikation | Quelle |
|---|---|---|---|---|
| FR-TP-001 | Das System MUSS dem Protokollentwickler die Möglichkeit bieten, eine PDU beliebiger zulässiger Länge (Single Frame bis Multi-Frame, klassisches CAN und CAN-FD) über einen ISO-TP-Kanal zu senden (`SendAsync`) und den Abschluss der Übertragung asynchron zu erfahren. | Must | Integrationstest (Virtual-Loopback): SF- und Multi-Frame-Roundtrip für Nutzlasten 1–4095 Byte. | ISO 15765-2; Review §1.1 (Fehlerkatalog als Ausgangspunkt) |
| FR-TP-002 | Das System MUSS eingehende ISO-TP-PDUs korrekt aus Single-, First- und Consecutive-Frames zusammensetzen und über `DatagramReceived`/`ReceiveAsync` bereitstellen. | Must | Integrationstest: Multi-Frame-Empfang mit korrekter Reassemblierung inkl. Sequenznummern-Prüfung. | ISO 15765-2 §6; Review §1.1 Punkt 7 (SN-Fehler als Negativbeispiel) |
| FR-TP-003 | Das System MUSS Classic-CAN- und CAN-FD-Frames gemäß dem in den Kanaloptionen konfigurierten Modus korrekt erzeugen (Korrektur der in Review §1.1 Punkt 1 beschriebenen Invertierung `canfd ? Classic : Fd`). | Must | Unit-Test: `IsoTpOptions` mit `CanFd=true` erzeugt ausschließlich `CanFrameType.CanFd`-Frames für SF/FF/CF/FC. | Review §1.1 Punkt 1 |
| FR-TP-004 | Das System MUSS Flow-Control-Frames mit korrektem PCI-Typ (`FC`, nicht `FF`) und ohne Überschreiben von BS/STmin durch Padding erzeugen. | Must | Unit-Test: erzeugter FC-Frame hat PCI-Nibble `0x3`, BS- und STmin-Byte entsprechen den übergebenen Werten. | Review §1.1 Punkt 2, 3 |
| FR-TP-005 | Das System MUSS First-Frame-Längenangaben > 255 Byte korrekt kodieren und dekodieren (Korrektur des High-Nibble-Bindungsfehlers). | Must | Unit-Test: FF-Länge-Roundtrip für Werte 256, 512, 4095. | Review §1.1 Punkt 4 |
| FR-TP-006 | Das System MUSS STmin-Werte gemäß ISO 15765-2 kodieren, einschließlich der häufigen Werte 0 ms und 1 ms, ohne Ausnahme zu werfen. | Must | Unit-Test: `EncodeStmin(0)`, `EncodeStmin(1000µs)` liefern gültige Kodierung ohne Exception. | Review §1.1 Punkt 5 |
| FR-TP-007 | Das System MUSS reservierte STmin-Werte beim Dekodieren gemäß ISO 15765-2 als 0x7F (127 ms) behandeln und darf bei fehlerhaften/unerwarteten PCI-Längen keine unbehandelte `IndexOutOfRangeException` auslösen. | Must | Unit-Test: reservierte Werte 0x80–0xF0, 0xFA–0xFF liefern 127 ms; Fuzz-Test mit verkürzten PCI-Bytes löst keine unbehandelte Ausnahme aus. | Review §1.1 Punkt 6 |
| FR-TP-008 | Das System MUSS Consecutive Frames mit korrektem Nutzdaten-Offset (kein Datenverlust am FF/CF-Übergang) und korrekt beginnender Sequenznummer (SN=1 am ersten CF) senden. | Must | Integrationstest: Multi-Frame-Nachricht wird byteweise verlustfrei über Virtual-Loopback übertragen und verglichen. | Review §1.1 Punkt 7 |
| FR-TP-009 | Das System MUSS Multi-Frame-Sendevorgänge zuverlässig starten (First Frame wird gesendet und der Kanal wartet korrekt auf Flow Control), ohne dass der Sendevorgang unbeobachtet hängen bleibt. | Must | Integrationstest: `SendAsync` für Payload > SF-Grenze schließt innerhalb konfigurierter Zeit erfolgreich ab. | Review §1.1 Punkt 8 |
| FR-TP-010 | Das System MUSS alle ISO-TP-Zeitüberwachungen (N_As, N_Bs, N_Cr mind.) aktiv prüfen und bei Überschreitung den Sende-/Empfangsvorgang mit einem für den Aufrufer beobachtbaren Fehler abschließen. | Must | Integrationstest: künstlich verzögerte/ausbleibende Gegenstelle löst innerhalb der konfigurierten Deadline einen Fehler statt eines hängenden Tasks aus. | Review §1.1 Punkt 10 |
| FR-TP-011 | Das System MUSS Flow-Control „Wait“ (FS=WT) nur bis zu einer konfigurierbaren Obergrenze (WFTmax) akzeptieren und danach den Vorgang abbrechen. | Must | Unit-Test: Gegenstelle sendet WT häufiger als WFTmax → Abbruch mit Fehler. | ISO 15765-2 §6.3 (WFTmax); Review §1.1 Punkt 10 |
| FR-TP-012 | Das System MUSS auf Overflow-Flow-Control (FS=OVFLW) den laufenden Sendevorgang mit einem beobachtbaren Fehlerergebnis abschließen statt ihn nur intern als „Failed“ zu markieren. | Must | Unit-Test: FS=OVFLW-Antwort führt zu fehlgeschlagenem `SendAsync`-Task innerhalb definierter Zeit. | Review §1.1 Punkt 10 |
| FR-TP-013 | Das System MUSS auf `netstandard2.0`-Zielplattformen denselben korrekten TX-Warteschlangen-Ablauf wie auf `net8.0` liefern (Korrektur der invertierten `TryPeek`-Polyfill-Logik). | Must | Unit-Test explizit gegen `netstandard2.0`-Build: TX-Queue liefert bei leerer Queue kein Element ohne Ausnahme, bei gefüllter Queue das erwartete Element. | Review §1.1 Punkt 11 |
| FR-TP-014 | Das System MUSS mehrere gleichzeitig „in flight“ befindliche Frames mit identischem Inhalt (z. B. gepaddete Consecutive Frames) im Deadline-/Confirm-Tracking unterstützen, ohne abzustürzen. | Must | Unit-Test: zwei inhaltsgleiche Frames gleichzeitig in Bearbeitung, keine Ausnahme, korrekte getrennte Zeitüberwachung. | Review §1.1 Punkt 12; siehe auch FR-RAW-031 |
| FR-TP-015 | Das System MUSS den ISO-TP-internen Frame-Buffer über den in FR-RAW-002 definierten Ownership-Vertrag beziehen (kein direktes `ArrayPool.Rent` ohne Rückgabe, keine Frames > 8 Byte für Classic-CAN). | Must | Unit-Test: SF-Erzeugung für Classic-CAN erzeugt gültigen ≤8-Byte-Frame ohne `ArgumentOutOfRangeException`; Speicher-Leak-Test über N Sendevorgänge. | Review §1.1 Punkt 13 |
| FR-TP-016 | Das System MUSS das in FR-RAW-020..023 definierte Threading-Modell für den ISO-TP-Scheduler umsetzen: ereignisgetrieben statt Busy-Loop, synchronisierter Zugriff auf Kanalregister, konsistente Fehlerweiterleitung. | Must | Wie FR-RAW-022/023, angewandt auf `IsoTpScheduler`. | Review §1.1 Punkt 9, 14, 15 |
| FR-TP-017 | Das System MUSS die TX-Bestätigung für ISO-TP-Sendevorgänge über die in FR-RAW-030..033 definierte Abstraktion realisieren (kein direktes `SetResult`/`SetException` in Race-Situationen mit Cancellation). | Must | Unit-Test: gleichzeitige Cancellation und TX-Fehlschlag führen zu genau einem konsistenten Ergebnis, keine `InvalidOperationException`. | Review §1.1 Punkt 14 |
| FR-TP-018 | Das System SOLLTE dem Protokollentwickler erlauben, mehrere ISO-TP-Kanäle mit unterschiedlichen Adresspaaren gleichzeitig über denselben physischen Bus zu betreiben (Nutzung des L2-Demultiplexing gemäß FR-RAW-010ff.). | Should | Integrationstest: zwei ISO-TP-Kanäle mit unterschiedlichen CAN-IDs auf demselben Virtual-Bus arbeiten unabhängig und korrekt. | Abgeleitet aus L2-Anforderungen |
| FR-TP-019 | Das System KANN Funktionale Adressierung (Functional Addressing, 1:n-Anfragen gemäß ISO 15765-2/ISO 14229) unterstützen. | Could | Integrationstest: funktionale Anfrage wird von mehreren simulierten ECUs beantwortet. | ISO 15765-2 |
| FR-TP-020 | Das System MUSS das ISO-TP-Paket in seiner Abhängigkeitsliste keine unnötigen Vendor-SDK-Referenzen (z. B. `Peak.PCANBasic.NET`) enthalten. | Must | Build-/Paketprüfung: `CanKit.Pro.IsoTp.csproj` referenziert keine Adapter-spezifischen Vendor-Pakete. | Review §1.1 Punkt 16 |

#### 4.2.2 J1939-TP (TP.BAM/TP.CM)

Ist-Zustand: MVP umgesetzt als eigenständiges Paket `CanKit.Pro.J1939Tp` (`src/transports/CanKit.Pro.J1939Tp`, `IsPackable=false`, Version 0.1.0). Actor-getriebener Kanal (`IJ1939TpChannel`) auf Basis der L2-Dienste (`ICanBusService`, `IProtocolActor`, `IDeadlineScheduler`) sowie der J1939-Adressierungshelfer aus `CanKit.Pro.Addressing`; deckt FR-TP-030..035 gemäß Verifikationsspalte mittels Virtual-Loopback-Integrationstests in `tests/CanKit.Tests/TestCases/J1939TpTests.cs` ab. Weiterhin offen: Feinabstimmung sämtlicher §5.10.2.4-Zeitwerte gegen reale Hardware sowie L4-Applikationsschicht (FR-J1939-*).

| ID | Anforderung | Priorität | Verifikation | Quelle |
|---|---|---|---|---|
| FR-TP-030 | Das System MUSS dem Protokollentwickler die Möglichkeit bieten, PDUs bis 1785 Byte per Broadcast Announce Message (TP.BAM) an alle Netzteilnehmer zu senden. | Must | Integrationstest (Virtual-Loopback): BAM-Übertragung, mehrere Empfänger reassemblieren identisch. | SAE J1939-21 |
| FR-TP-031 | Das System MUSS dem Protokollentwickler die Möglichkeit bieten, PDUs per verbindungsorientiertem Transportprotokoll (TP.CM: RTS/CTS/EndOfMsgAck) an eine bestimmte Zieladresse zu senden, inkl. Segmentgrößen-Aushandlung über CTS. | Must | Integrationstest: RTS/CTS-Handshake, korrekte Segmentübertragung, EndOfMsgAck. | SAE J1939-21 |
| FR-TP-032 | Das System MUSS TP.CM-Zeitüberwachungen (T1–T4, Tr, Th) prüfen und Verbindungsabbrüche (Connection Abort) bei Überschreitung auslösen. | Must | Integrationstest: ausbleibende Gegenstelle löst Abort innerhalb der Spezifikationsfristen aus. | SAE J1939-21 |
| FR-TP-033 | Das System MUSS empfangsseitig sowohl TP.BAM- als auch TP.CM-Nachrichten korrekt anhand PGN 0xEC00/0xEB00 erkennen und den zugehörigen Reassemblierungs-Zustandsautomaten zuordnen. | Must | Unit-Test: gemischter Frame-Stream aus BAM- und CM-Sessions wird korrekt getrennt reassembliert. | SAE J1939-21 |
| FR-TP-034 | Das System MUSS das J1939-TP analog zu ISO-TP über das L2-Threading-Modell (FR-RAW-020ff.) und den Ownership-Vertrag (FR-RAW-001f.) implementieren. | Must | Wie FR-TP-016/017, angewandt auf J1939-TP. | Konsistenzanforderung |
| FR-TP-035 | Das System SOLLTE mehrere gleichzeitige TP.CM-Sessions mit unterschiedlichen Peer-Adressen unterstützen. | Should | Integrationstest: zwei parallele TP.CM-Sessions zu unterschiedlichen Zieladressen ohne gegenseitige Störung. | SAE J1939-21 |

### 4.3 L4 – Anwendungsprotokoll-Ebene

#### 4.3.1 UDS (ISO 14229, auf ISO-TP)

Ist-Zustand: MVP-Paket `CanKit.Pro.Uds` umgesetzt (`src/protocols/CanKit.Pro.Uds`, `IsPackable=false`, Version 0.1.0). `IUdsClient`/`UdsClient` auf `IIsoTpChannel` (L2/L3-Komposition wie die anderen Pro-Pakete); deckt alle Must-Anforderungen FR-UDS-001..010 vollständig ab (Session-Tracking, DID Read/Write, RoutineControl, ECU-Reset, SecurityAccess mit injiziertem Key-Callback, periodisches TesterPresent, P2/P2*-Überwachung, NRC-0x78-Handling mit `MaxResponsePendingCount`, strukturierte NRC-Weiterreichung). FR-UDS-011 (Multi-DID, Should) und FR-UDS-012 (Upload/Download 0x34/0x35/0x36/0x37, Could) sind ebenfalls umgesetzt, inkl. One-shot-Komfort `DownloadAsync`/`UploadAsync` mit automatischem Block-Sequence-Counter (Wrap 0xFF→0x00). Virtual-Loopback-Integrationstests in `tests/CanKit.Tests/TestCases/Uds/` (`UdsClientTests.cs`, `UdsTransferTests.cs`) inkl. simulierter ECU (`SimulatedUdsEcu`) decken alle IDs ab. Weiterhin offen: HIL-Stichprobe gegen reale Steuergeräte (Abschnitt 7).

| ID | Anforderung | Priorität | Verifikation | Quelle |
|---|---|---|---|---|
| FR-UDS-001 | Das System MUSS dem Diagnose-Tester die Möglichkeit bieten, eine Diagnose-Session zu wechseln (`DiagnosticSessionControl`, Service 0x10) und die aktive Session zu verfolgen. | Must | Integrationstest gegen simulierte ECU: Sessionwechsel Default→Extended, Bestätigung ausgewertet. | ISO 14229-1 §9.2 |
| FR-UDS-002 | Das System MUSS dem Diagnose-Tester die Möglichkeit bieten, einen Data Identifier zu lesen (`ReadDataByIdentifier`, Service 0x22). | Must | Integrationstest: DID-Lesevorgang liefert erwartete Nutzdaten. | ISO 14229-1 §9.3 |
| FR-UDS-003 | Das System MUSS dem Diagnose-Tester die Möglichkeit bieten, einen Data Identifier zu schreiben (`WriteDataByIdentifier`, Service 0x2E). | Must | Integrationstest: Schreibvorgang + positive Response. | ISO 14229-1 §9.6 |
| FR-UDS-004 | Das System MUSS dem Diagnose-Tester die Möglichkeit bieten, eine Routine zu steuern (`RoutineControl` – Start/Stop/RequestResults, Service 0x31). | Must | Integrationstest: alle drei Sub-Funktionen gegen simulierte ECU. | ISO 14229-1 §9.10 |
| FR-UDS-005 | Das System MUSS dem Diagnose-Tester die Möglichkeit bieten, einen ECU-Reset auszulösen (Service 0x11) und die Reset-Bestätigung zu verarbeiten. | Must | Integrationstest: Reset-Anfrage + positive Response vor Verbindungsabbruch der simulierten ECU. | ISO 14229-1 §9.1 |
| FR-UDS-006 | Das System MUSS dem Diagnose-Tester die Möglichkeit bieten, Security Access (Service 0x27) durchzuführen (Seed-Anfrage, Key-Übermittlung), wobei die Schlüsselberechnung außerhalb von CanKit liegt (Callback/Delegat). | Must | Integrationstest: Seed/Key-Austausch mit injiziertem Test-Algorithmus. | ISO 14229-1 §9.4 |
| FR-UDS-007 | Das System MUSS dem Diagnose-Tester die Möglichkeit bieten, Tester Present (Service 0x3E) periodisch zu senden, um eine Session am Leben zu erhalten. | Must | Integrationstest: periodische 0x3E-Sendung über konfigurierbares Intervall, Session bleibt aktiv. | ISO 14229-1 §9.12 |
| FR-UDS-008 | Das System MUSS P2- und P2*-Server-Timing (Default- und erweiterte Antwortzeit) überwachen und dem Aufrufer als Timeout melden, wenn die ECU nicht innerhalb der Frist antwortet. | Must | Integrationstest: verzögerte simulierte ECU-Antwort knapp über P2 löst Timeout aus, knapp darunter nicht. | ISO 14229-1 §7.3 (P2/P2*) |
| FR-UDS-009 | Das System MUSS Response Pending (NRC 0x78) korrekt behandeln, indem es die P2*-Frist neu startet und auf die endgültige Antwort wartet, statt vorzeitig zu terminieren. | Must | Integrationstest: simulierte ECU sendet 1..n mal 0x78, gefolgt von finaler Antwort; Aufrufer erhält die finale Antwort ohne Timeout. | ISO 14229-1 §7.3.3 |
| FR-UDS-010 | Das System MUSS Negative Response Codes (NRC) strukturiert an den Aufrufer weiterreichen (Service, angeforderter Service, NRC-Wert), statt sie als generische Ausnahme ohne Kontext zu melden. | Must | Unit-Test: simulierte NRC-Antwort (z. B. 0x31 „Request Out Of Range“) wird mit korrektem Kontext im Fehlerergebnis abgebildet. | ISO 14229-1 §8.7 |
| FR-UDS-011 | Das System SOLLTE Multi-DID-Lesevorgänge (Service 0x22 mit mehreren DIDs in einer Anfrage) unterstützen. | Should | Integrationstest: Anfrage mit 3 DIDs, korrekte Zuordnung der Antwortdaten. | ISO 14229-1 §9.3 |
| FR-UDS-012 | Das System KANN Upload/Download-Services (`RequestDownload`/`TransferData`/`RequestTransferExit`, Services 0x34/0x36/0x37) für Flash-/Datenübertragung unterstützen. | Could | Integrationstest: vollständiger Download-Zyklus gegen simulierte ECU. | ISO 14229-1 §14 |

#### 4.3.2 CANopen (CiA 301)

Ist-Zustand: MVP-Paket `CanKit.Pro.CANopen` umgesetzt (`src/protocols/CanKit.Pro.CANopen/`, `IsPackable=false`, Version 0.1.0). Deckt alle Must-Anforderungen FR-CO-001/002/003/005/006/007/008/010/011 sowie das L2-Multi-Consumer-Demultiplexing FR-CO-012 vollständig ab. FR-CO-004 (SDO-Block-Transfer, Should) ist implementiert (Client- und Server-Seite für Block-Download und -Upload, Blockgrößen-Aushandlung 1..127, optionale CRC-16/XMODEM, phasenbasiertes Dispatching für die überlappenden Command-Specifier). FR-CO-009 (Node-Guarding, Could) ist implementiert (RTR-getriebener Consumer inklusive Life-Time-Timeout, RTR-antwortender Producer mit Toggle-Bit; Heartbeat- und Node-Guarding-Producer sind gemäß CiA 301 §7.2.8.3 sich gegenseitig ausschließend). Details siehe `src/protocols/CanKit.Pro.CANopen/README.md`.

| ID | Anforderung | Priorität | Verifikation | Quelle |
|---|---|---|---|---|
| FR-CO-001 | Das System MUSS dem Applikationsentwickler die Möglichkeit bieten, ein lokales Objektverzeichnis (OD) mit Index/Subindex-Einträgen zu definieren und zur Laufzeit zu lesen/schreiben. | Must | Unit-Test: OD-Eintrag anlegen, lesen, schreiben, Typprüfung. | CiA 301 §7.4 |
| FR-CO-002 | Das System MUSS SDO-Expedited-Transfer (≤4 Byte) für Lese-/Schreibzugriff auf entfernte OD-Einträge unterstützen. | Must | Integrationstest gegen simulierten Node: SDO-Read/Write Expedited. | CiA 301 §7.2.4 |
| FR-CO-003 | Das System MUSS SDO-Segmented-Transfer für OD-Werte > 4 Byte unterstützen (Toggle-Bit-Handling gemäß Spezifikation). | Must | Integrationstest: Segmented-Transfer mit mehrfachem Segmentwechsel und korrektem Toggle-Bit. | CiA 301 §7.2.4.3 |
| FR-CO-004 | Das System SOLLTE SDO-Block-Transfer für große OD-Werte unterstützen. | Should | Integrationstest: Block-Transfer inkl. Blockgrößen-Aushandlung. | CiA 301 §7.2.4.3.15 (Block Transfer, CiA 302) |
| FR-CO-005 | Das System MUSS PDO-Mapping (statisch/dynamisch) für TPDO (Transmit) und RPDO (Receive) unterstützen, inklusive Zuordnung von OD-Einträgen zu PDO-Nutzdaten-Offsets. | Must | Unit-Test: PDO-Mapping-Konfiguration erzeugt korrekten Frame-Payload aus gemappten OD-Werten und umgekehrt. | CiA 301 §7.3 |
| FR-CO-006 | Das System MUSS Event- und zeitgesteuertes TPDO-Senden (Change-of-State, Timer) sowie SYNC-getriggertes PDO-Senden/-Empfangen unterstützen. | Must | Integrationstest: TPDO wird bei OD-Wertänderung bzw. bei SYNC-Empfang korrekt gesendet. | CiA 301 §7.3, §7.5 |
| FR-CO-007 | Das System MUSS NMT-Zustandswechsel (Pre-Operational, Operational, Stopped, Reset) sowohl als NMT-Master (Kommandos senden) als auch als NMT-Node (Kommandos empfangen und Zustand wechseln) unterstützen. | Must | Integrationstest: alle vier NMT-Kommandos gegen simulierten Node/Master mit korrektem Zustandsübergang. | CiA 301 §7.2.8.3 |
| FR-CO-008 | Das System MUSS Heartbeat-Produktion (periodisches Senden des eigenen Zustands) und Heartbeat-Konsumption (Timeout-Erkennung bei ausbleibendem Heartbeat eines überwachten Node) unterstützen. | Must | Integrationstest: ausbleibender Heartbeat löst innerhalb der konfigurierten Frist ein Timeout-Ereignis aus. | CiA 301 §7.2.8.3.4 |
| FR-CO-009 | Das System KANN Node-Guarding (Legacy-Alternative zu Heartbeat) unterstützen. | Could | Integrationstest: Guarding-Request/-Response-Zyklus. | CiA 301 §7.2.8.3.3 |
| FR-CO-010 | Das System MUSS SYNC-Objekte senden (als Sync-Producer) und empfangen (als Sync-Consumer) können. | Must | Integrationstest: SYNC-Frame löst konfigurierte SYNC-getriggerte PDOs aus. | CiA 301 §7.2.6 |
| FR-CO-011 | Das System MUSS Emergency-Objekte (EMCY) beim Auftreten interner Fehlerzustände senden und empfangene EMCY-Objekte strukturiert (Error Code, Error Register, Herstellerdaten) an den Applikationsentwickler weiterreichen. | Must | Unit-Test: EMCY-Frame wird korrekt kodiert/dekodiert inkl. aller Felder. | CiA 301 §7.2.7 |
| FR-CO-012 | Das System SOLLTE das L2-Multi-Consumer-Demultiplexing (FR-RAW-010ff.) nutzen, um NMT-, SDO-, PDO- und SYNC-Verkehr eines Node parallel und unabhängig zu verarbeiten. | Should | Integrationstest: gleichzeitiger SDO- und PDO-Verkehr auf demselben Node ohne gegenseitige Verzögerung. | Abgeleitet aus L2-Anforderungen |

#### 4.3.3 J1939 (Applikation)

Ist-Zustand: MVP umgesetzt als eigenständiges Paket `CanKit.Pro.J1939` (`src/protocols/CanKit.Pro.J1939`, `IsPackable=false`, Version 0.1.0). Actor-getriebener Node (`IJ1939Node`) auf Basis der L2-Dienste (`ICanBusService`, `IProtocolActor`, `IDeadlineScheduler`) sowie der J1939-Adressierungshelfer aus `CanKit.Pro.Addressing`; für Nutzlasten > 8 Byte wird automatisch die J1939-TP-Ebene (`CanKit.Pro.J1939Tp`) verwendet. Deckt FR-J1939-001..006 (Must) sowie FR-J1939-007 (Should) mittels Virtual-Loopback-Integrationstests in `tests/CanKit.Tests/TestCases/J1939/J1939NodeTests.cs` ab. FR-J1939-007 fährt für jede periodische PGN — Single-Frame wie Multi-Frame — ein festes Zeitraster auf Basis der L2-`DeadlineScheduler`-Zeitgebung (Emissionen bei t0 + n × Periode, ohne Drift durch die Sendezeit; Ticks mit noch laufendem Versand werden übersprungen, nachgehängte Ticks koalesziert); die Sende-Schleife respektiert bei jeder Emission die Pre-Flight-Claim-Prüfung von `SendAsync`, sodass Adressverlust die Wire-Emission sofort stoppt und ein späterer Claim sie automatisch unter der neuen SA fortsetzt (Fehler jederzeit über `BackgroundExceptionOccurred`). FR-J1939-004 ist inzwischen vollständig umgesetzt: nach einem verlorenen Contest scannt der Node das Arbitrary-Address-Feld (0x80..0xF7, einmalig wrapend) und meldet Cannot-Claim erst bei Erschöpfung — konfigurierbar über `J1939NodeOptions.EnableArbitraryAddressClaiming` (Default: aus dem AAC-Bit der NAME abgeleitet); die SRS-Verifikation „alle Adressen belegt" ist als Test abgedeckt. Eine native L1-Optimierung über `ICanBus.TransmitPeriodic` / `IPeriodicTx` ist zurückgestellt, bis der L1-Fallback (`SoftwarePeriodicTx`) `Transmit`-Fehler zuverlässig nach oben melden kann. Weiterhin offen: Feinabstimmung der SAE J1939-81 Arbitrationszeiten gegen reale Hardware.

| ID | Anforderung | Priorität | Verifikation | Quelle |
|---|---|---|---|---|
| FR-J1939-001 | Das System MUSS dem Applikationsentwickler die Möglichkeit bieten, Nachrichten anhand PGN zu senden/empfangen und dabei Priority, PDU-Format (PF), PDU-Specific (PS) und Source Address gemäß J1939-21 korrekt in die 29-Bit-ID zu kodieren/dekodieren. | Must | Unit-Test: PGN-Roundtrip für PDU1- und PDU2-Format-Nachrichten. | SAE J1939-21 |
| FR-J1939-002 | Das System MUSS dem Applikationsentwickler die Möglichkeit bieten, einzelne SPN-Werte aus einem empfangenen PGN-Payload gemäß konfigurierbarer Skalierung/Offset zu extrahieren. | Must | Unit-Test: SPN-Extraktion mit bekannten Skalierungsfaktoren liefert erwartete physikalische Werte. | SAE J1939-71 |
| FR-J1939-003 | Das System MUSS das Address-Claiming-Verfahren (Senden/Empfangen des Address-Claim-Frames PGN 0xEE00, Konfliktauflösung nach NAME-Priorität) unterstützen. | Must | Integrationstest: zwei simulierte Nodes mit kollidierender Adresse, korrekte Konfliktauflösung nach NAME. | SAE J1939-81 |
| FR-J1939-004 | Das System MUSS Cannot-Claim-Address-Verhalten (kein verfügbarer Adressplatz) gemäß Spezifikation signalisieren. | Must | Integrationstest: alle Adressen belegt, Node meldet Cannot-Claim korrekt. | SAE J1939-81 |
| FR-J1939-005 | Das System MUSS Request-PGN (PGN 0xEA00) senden und empfangen können, um gezielt eine PGN von einem oder allen Netzteilnehmern anzufordern. | Must | Integrationstest: Request-PGN löst erwartete Antwort-PGN aus. | SAE J1939-21 |
| FR-J1939-006 | Das System MUSS für PGN-Nachrichten > 8 Byte automatisch das J1939-TP (FR-TP-030ff.) verwenden, für ≤8 Byte den direkten Single-Frame-Pfad. | Must | Integrationstest: PGN mit 20-Byte-Payload nutzt TP.BAM/TP.CM, PGN mit 6-Byte-Payload nutzt Single Frame. | SAE J1939-21 |
| FR-J1939-007 | Das System SOLLTE periodisches Senden von PGNs mit spezifikationsgemäßer Standardrate unterstützen (Nutzung von `IPeriodicTx`/L2-Scheduling). | Should | Integrationstest: konfigurierte PGN wird mit korrekter Periodenrate gesendet. | SAE J1939-71; `IPeriodicTx` |

#### 4.3.4 HAWE-Privatprotokoll (generischer Rahmen)

Ist-Zustand: Der generische Rahmen (`src/protocols/CanKit.Pro.Hawe`) existiert und deckt FR-HAWE-001..004 auf L4 ab (öffentliche SPI `IHaweCodec`/`IHaweCodecRegistry`/`IHaweChannel`, Frame-Pattern-Send/-Receive über die L2-Demux-Dienste, Aktor-/Deadline-Anbindung, Platzhalter-Sitzungsautomat `Idle`/`Active`/`Fault`). Es sind **keinerlei** vertrauliche HAWE-Protokolldetails (Service-IDs, Frame-Layouts, Sitzungssemantik) in diesem Assembly oder öffentlichen Repository enthalten (CON-006). Konkrete HAWE-Codecs sind ausschließlich in einem separaten, nicht-öffentlichen Modul zu implementieren; das öffentliche Framework-Paket wird bis dahin `IsPackable=false` gehalten (CON-004). Protokolldetails bleiben extern/vertraulich (Annahme A-6). Anforderungen sind bewusst generisch gehalten und definieren Erweiterungspunkte statt konkreter Serviceinhalte.

| ID | Anforderung | Priorität | Verifikation | Quelle |
|---|---|---|---|---|
| FR-HAWE-001 | Das System MUSS dem Applikationsentwickler einen Erweiterungspunkt (SPI, analog `IHaweCodecRegistry`) bieten, über den ein HAWE-spezifisches Frame-Codec-Modul (Kodierung/Dekodierung proprietärer Nachrichtenformate) eingebunden werden kann, ohne den L2/L3-Kern zu verändern. | Must | Architekturtest: Referenz-Dummy-Codec wird über SPI registriert und im Registry gefunden. | Auftrag, Annahme A-6 |
| FR-HAWE-002 | Das System MUSS dem Applikationsentwickler die Möglichkeit bieten, beliebige rohe CAN-Frame-Payloads gemäß einem konfigurierbaren, frame-basierten Muster (ID-Bereich, Payload-Layout) zu senden und zu empfangen, ohne dass CanKit-Kern das konkrete HAWE-Format kennen muss. | Must | Integrationstest: generisches Frame-Muster wird über Virtual-Loopback korrekt gesendet/empfangen. | Auftrag |
| FR-HAWE-003 | Das System SOLLTE dem HAWE-Erweiterungsmodul Zugriff auf die L2-Dienste (Demultiplexing, TX-Confirm, Threading-Modell) auf gleicher Grundlage wie ISO-TP/CANopen/J1939 gewähren. | Should | Architekturreview: HAWE-Referenzmodul nutzt dieselben L2-SPI-Schnittstellen wie ISO-TP. | Konsistenzanforderung |
| FR-HAWE-004 | Das System KANN einen Platzhalter-Zustandsautomaten (Session/Handshake) als Vorlage für die spätere Umsetzung der tatsächlichen HAWE-Protokolllogik bereitstellen, sobald die Spezifikation verfügbar ist. | Could | Nicht verifizierbar vor Vorliegen der HAWE-Spezifikation; Platzhalter-Vorhandensein per Codereview. | Annahme A-6 |
| FR-HAWE-005 | Das System MUSS bei Verfügbarkeit der HAWE-Spezifikation eine Nachführung dieser Anforderungen (Verfeinerung von FR-HAWE-001..004) vorsehen; bis dahin gilt dieser Abschnitt als vorläufig. | Must | Dokumentationsprozess: SRS-Änderungsprotokoll. | Annahme A-6 |

---

## 5. Nicht-funktionale Anforderungen

| ID | Anforderung | Priorität | Verifikation | Quelle |
|---|---|---|---|---|
| NFR-001 | Periodisches Senden (L1 `IPeriodicTx`/L2-Scheduling) MUSS auf Windows und Linux einen Jitter innerhalb eines dokumentierten Toleranzbandes einhalten. Für den Softwarepfad `SoftwarePeriodicTx` gilt: p99 des absoluten Inter-Sende-Jitters ≤ 1,0 ms auf Windows- und Linux-Referenzhosts, wenn die Periode ≥ 1 ms ist, gemessen über ≥ 500 Perioden bei idle Prozess und Virtual- oder Null-Bus-artigem Transmit. Hardware-BCM bzw. Vendor-periodic-TX darf enger sein und wird separat per HIL validiert. | Must | Performance-/Timing-Test: Zeitstempelmessung von ≥ 500 periodischen Sendungen, Jitter-Histogramm, p99/max-Auswertung. CI führt einen synthetischen Virtual-Adapter-Softwaretest mit weicher **mittelwertrelativer** Grenze (p99 mean-relative AbsJitter ≤ 25,0 ms) aus — Shared Windows-Runner zeigen oft systematische Periodenverschiebung durch Timer-Auflösung/Last, daher kein absolutes CI-Gate gegen die konfigurierte Periode. | NFR-Vorgabe „Echtzeit/Jitter“ |
| NFR-002 | Das System MUSS auf macOS keinen Busy-Loop im Software-Timing-Pfad ausführen; bei fehlender `clock_nanosleep`-Verfügbarkeit MUSS auf `Thread.Sleep`-Fallback umgeschaltet werden (Korrektur Review §2.3). | Must | Unit-/Integrationstest auf macOS-Runner: CPU-Auslastung eines aktiven periodischen Senders bleibt in normalem Bereich, kein Sende-Sturm. | Review §2.3 (`SoftwarePeriodicTx`, verschluckte `EntryPointNotFoundException`) |
| NFR-003 | ISO-TP-STmin-Einhaltung MUSS innerhalb einer dokumentierten Genauigkeit (z. B. ±1 ms oder Adapter-Auflösung) eingehalten werden. | Must | Integrationstest: gemessene Inter-Frame-Zeit von Consecutive Frames vs. konfiguriertes STmin. | ISO 15765-2 |
| NFR-004 | Das System MUSS auf allen deklarierten Ziel-Frameworks (`netstandard2.0`, `net8.0`, `net8.0-windows`) funktional äquivalentes Verhalten für L2/L3-Kernlogik liefern (keine plattformspezifischen Logikfehler wie die invertierte `TryPeek`-Polyfill). | Must | CI-Testmatrix über alle drei TFMs mit identischen Testergebnissen für L2/L3-Kernszenarien. | Review §1.1 Punkt 11; CON-001 |
| NFR-005 | Vendor-adapterabhängige L0/L1-Funktionalität (Kvaser, PCAN, Vector, ControlCAN) MUSS auf ihre jeweils unterstützten Plattformen (i. d. R. Windows) beschränkt bleiben und darf auf nicht unterstützten Plattformen einen klaren Fehler statt eines unspezifizierten Crashs liefern. | Must | Buildmatrix-/Smoke-Test je Plattform. | CON-002 |
| NFR-006 | Fehlerzustände (Bus-Off, Timeout, Verbindungsabbruch) MÜSSEN in L2/L3/L4 konsistent über strukturierte Ausnahmen/Ergebnistypen kommuniziert werden, nicht durch stillschweigend verlorene oder auf falschem Thread geworfene Exceptions (Korrektur Review §1.1 Punkt 15, §2.2). | Must | Unit-Tests je Fehlerpfad; Regressionstest gegen „Exception im falschen Kontext“-Befunde. | Review §1.1 Punkt 15; §2.2 |
| NFR-007 | Ressourcenkritische Pfade (Frame-Erzeugung/-Versand in L2/L3 bei hoher Bus-Last) SOLLTEN Allokationen minimieren (Pooling gemäß Ownership-Vertrag, kein `ArrayPool.Rent` ohne Rückgabe). | Should | Benchmark: Allokationsmessung (Bytes/Frame) für ISO-TP-SF/CF-Pfad vor/nach Umsetzung. | Review §1.1 Punkt 13 |
| NFR-008 | Das System MUSS Thread-Safety für alle von mehreren Protokollinstanzen gleichzeitig genutzten L2-Komponenten (Demultiplex-Hub, Registry, Scheduler) gewährleisten. | Must | Stresstest mit parallelen Registrierungen/Abmeldungen und gleichzeitigem RX/TX-Verkehr ohne Datenrennen. | Review §1.1 Punkt 14; §2.5 (`CanRegistry` ohne Lock) |
| NFR-009 | Alle L2/L3/L4-Komponenten MÜSSEN gegen den bestehenden Virtual-Adapter (Loopback, ohne reale Hardware) testbar sein, um CI-taugliche Regressionstests zu ermöglichen. | Must | CI-Lauf: vollständige L2/L3/L4-Testsuite läuft ohne Hardware-Abhängigkeit gegen `CanKit.Adapter.Virtual`. | Auftrag; bestehendes Fake-Native-Muster (Review, Gesamteinschätzung) |
| NFR-010 | Neue Protokoll-Ebenen (L3/L4) MÜSSEN dem bestehenden SPI-Erweiterbarkeitsmuster folgen (eigenständiges Paket, Registrierung über `[CanRegistryEntry]`/`ICanRegistryEntry`-Analogon bzw. Pro-Factory/`Open`-Einstieg), um Wartbarkeit und lose Kopplung zum Kern zu sichern. | Must | Architekturreview: neues Transport-/Protokollpaket registriert sich ausschließlich über SPI bzw. Factory, ohne Änderungen an `CanKit.Core`. | Bestehendes Muster `CanRegistryEntry` / Pro-`Open`-Factories |
| NFR-011 | Öffentliche APIs neuer Ebenen (L2/L3/L4) SOLLTEN von Anfang an konsistente Namensgebung verwenden. **Umgesetzt (vor 1.0):** `ExceptionOccured`→`ExceptionOccurred`, `ReadTImeOutMs`→`ReadTimeoutMs`; Namespace-Typo `Excpetions` entfällt mit dem Legacy-ISO-TP-Abbau. | Should | API-Review/Linting-Checkliste vor 1.0-Release; Rename-Diff + Tests. | Review §3, Empfehlung Pkt. 8 |
| NFR-012 | Zeitstempel in neu geschaffenen L2/L3/L4-Datenstrukturen MÜSSEN einheitlich UTC verwenden (Korrektur der Inkonsistenz `DateTime.Now` vs. `DateTime.UtcNow` in L1). **Status:** für die berührten Pfade adressiert — `CanReceiveData.SystemTimestamp` verwendet standardmäßig `DateTime.UtcNow`, und sämtliche Diagnose-/Hot-Path-Aufrufe der Adapter (ZLG, Kvaser, PCAN, ControlCAN, SocketCAN-Fallback) liefern nun UTC; abgesichert durch `SystemTimestampUtcTests` gegen den Virtual-Adapter. | Should | Codereview-Checkliste; Unit-Test auf `DateTimeKind.Utc`. | Review §2.5 |

---

## 6. Randbedingungen (`CON-xxx`)

| ID | Randbedingung | Art | Quelle |
|---|---|---|---|
| CON-001 | Alle L2/L3-Pakete MÜSSEN mindestens `netstandard2.0`, `net8.0` und `net8.0-windows` als Ziel-Frameworks unterstützen (gemäß `src/Directory.Build.props`). | technisch | `src/Directory.Build.props` |
| CON-002 | Vendor-Adapter-Integrationen (PCAN, Kvaser, Vector, ControlCAN) basieren auf P/Invoke gegen proprietäre native SDKs/DLLs; L2/L3-Komponenten dürfen diese Abhängigkeiten nicht direkt referenzieren, sondern ausschließlich über `ICanBus`/`ITransceiver`-Abstraktionen nutzen. | technisch | Review §1.1 Punkt 16 (Negativbeispiel: ISO-TP referenziert `Peak.PCANBasic.NET` grundlos) |
| CON-003 | Lizenzbedingungen der Vendor-SDKs (Peak PCANBasic, Kvaser CANlib, Vector XL-Driver) sind proprietär; Distribution/Verwendung dieser SDKs unterliegt Drittanbieter-Lizenzen, die außerhalb der Kontrolle dieses Projekts liegen. | rechtlich | `CanKit.Adapter.PCAN.csproj` (`Peak.PCANBasic.NET`-Referenz) |
| CON-004 | Neue Pakete (L3/L4) MÜSSEN, solange sie funktional unvollständig sind, klar als experimentell gekennzeichnet oder von der Release-Pipeline ausgeschlossen werden (`IsPackable=false`), analog der Review-Empfehlung für den aktuellen ISO-TP-Stand. Stand: Der aktuelle ISO-TP-Prototyp ist als experimentell gekennzeichnet und mit `IsPackable=false` vom Packaging ausgeschlossen. | organisatorisch | Review §1.1, Empfehlung Pkt. 1 |
| CON-005 | Das Projekt verwendet Apache-2.0-Lizenzierung (`PackageLicenseExpression` in `Directory.Build.props`); neue L3/L4-Pakete MÜSSEN dieselbe Lizenz führen, sofern keine abweichende vertragliche Regelung (z. B. HAWE-Vertraulichkeit) entgegensteht. | rechtlich/organisatorisch | `src/Directory.Build.props` |
| CON-006 | Das HAWE-Protokoll ist vertraulich; jegliche konkrete Protokolldetails DÜRFEN NICHT in öffentlichen CanKit-Repositories oder NuGet-Paketen offengelegt werden. Nur der generische Rahmen (FR-HAWE-xxx) ist öffentlich. | rechtlich | Auftrag, Annahme A-6 |
| CON-007 | CI-Workflows testen aktuell projektweise über `.slnf`-Filterdateien (z. B. `CanKitAdapters.slnf`, `CanKitProIsoTp.slnf`); neue L3/L4-Pakete MÜSSEN in eine passende `.slnf`-Datei mit eigenem CI-Workflow aufgenommen werden. Stand: Legacy-`CanKitTransports.slnf` wurde mit dem Legacy-ISO-TP-Paket entfernt; Pro-Transports nutzen eigene Filter (`CanKitProIsoTp.slnf`, `CanKitProJ1939Tp.slnf`, …). | organisatorisch | Review §4 („ISO-TP-Projekt hat keinen eigenen Workflow“) |
| CON-008 | Der Standard-Git-Branch ist `main`; Release-/Paket-Pipelines MÜSSEN konsistent auf diesen Branch referenzieren. (Stand 2026-07-21: zuvor fälschlich `master` dokumentiert; `main` ist der tatsächliche HEAD-Branch des Repositorys.) | organisatorisch | Review §3, §5 Pkt. 9 (`nuget-pipeline.yml` Branch-Trigger-Fehler) |

---

## 7. Abnahme-/Verifikationsstrategie

| Testart | Zweck | Anwendbar auf | Voraussetzung |
|---|---|---|---|
| **Unit-Test** | Isolierte Prüfung von Kodierungs-/Dekodierungslogik, Zustandsautomaten-Übergängen, Randwerten (z. B. STmin-Grenzwerte, PGN-Kodierung, NRC-Mapping). | L2 (Ownership, Adressierung), L3 (Frame-Codec), L4 (Service-Kodierung) | Keine Hardware nötig; xUnit, bestehende Matrix-Test-Konventionen (`tests/CanKit.Tests/Matrix/*`). |
| **Virtual-Loopback-Integrationstest** | End-zu-End-Verhalten über den bestehenden `CanKit.Adapter.Virtual` ohne reale Hardware; Standardverfahren für CI. | L2 (Demultiplexing, TX-Confirm, Threading), L3 (ISO-TP-/J1939-TP-Roundtrips), L4 (UDS-/CANopen-/J1939-Sessions gegen simulierte Gegenstelle) | Simulierte Gegenstellen (Test-ECU/-Node) auf Basis des Virtual-Adapters. |
| **Fake-Native-Test** | Adapterverhalten (L0/L1) ohne reale Vendor-Hardware über `*.Fake.cs`-Schicht (`-c Fake`-Konfiguration). | L1 (bereits etabliert), indirekt L2/L3, wo adapterspezifisches Verhalten (Echo, Timeout) relevant ist. | Bestehende Fake-Konfiguration je Adapter. |
| **Stress-/Nebenläufigkeitstest** | Aufdeckung von Datenrennen, Deadlocks, Leaks unter paralleler Last (mehrere Protokollinstanzen, häufige Subscribe/Unsubscribe-Zyklen). | L2 (Threading-Modell, Registry), L3 (Scheduler) | Wiederholte Ausführung (N-fach), ggf. mit Race-Detection-Tools. |
| **Performance-/Timing-Test** | Messung von Jitter, Durchsatz, STmin-Genauigkeit, CPU-Auslastung im Leerlauf. | NFR-001..003, NFR-007 | Referenzplattformen (Windows, Linux; macOS für NFR-002). Für NFR-001: Softwarepfad-Akzeptanz p99 abs ≤ 1,0 ms bei Periode ≥ 1 ms über ≥ 500 Perioden; CI-Softwaretest gegen Virtual sammelt Statistik und nutzt nur eine weiche mittelwertrelative p99 ≤ 25,0 ms Grenze. |
| **HIL-Test (Hardware-in-the-Loop)** | Validierung gegen reale ECUs/Bus-Hardware für ausgewählte kritische Szenarien (z. B. reale UDS-Diagnosesitzung, reales CANopen-Gerät). | Stichprobenhaft für L4 (UDS, CANopen, J1939) vor Produktivfreigabe. | Reale Testhardware, außerhalb des CI-Standardlaufs; siehe Annahme A-5. |
| **Architektur-/Codereview** | Prüfung nicht automatisiert testbarer Anforderungen (Dokumentation, SPI-Konformität, Namenskonventionen). | FR-RAW-001, NFR-010, NFR-011 | Checkliste, Teil des PR-Reviewprozesses. |

**Grundprinzip:** Jede `Must`-Anforderung MUSS mindestens durch Unit- oder Virtual-Loopback-Integrationstest abgedeckt sein, bevor die jeweilige Ebene als „fertig“ gilt. HIL-Tests ergänzen für produktionskritische L4-Szenarien, sind aber wegen Hardwareabhängigkeit nicht Teil des Standard-CI-Gates.

---

## 8. Traceability-Matrix

Verweise auf Architektur-Bausteine nutzen die in Abschnitt 2.1 definierten Schichtnamen L0–L4 und sind konsistent mit den in `docs/architecture/arc42-CanKit.md` (parallel in Erstellung) zu erwartenden Bausteinnamen. Wo das Architekturdokument zum Zeitpunkt dieser SRS noch nicht existiert, wird der erwartete Baustein benannt (kursiv gekennzeichnet als *geplant*).

| Anforderung(en) | Architektur-Baustein (L0–L4) | Verifikation |
|---|---|---|
| FR-RAW-001..005 | L2 – *Frame-Ownership-Vertrag* (geplant), aufbauend auf L1 `CanFrame`/`CanFrameView` (`src/core/CanKit.Abstractions/API/Can/Definitions/CanFrame.cs`) | Unit-Test, Virtual-Loopback-Integrationstest |
| FR-RAW-010..014 | L2 – *Demultiplex-Hub/Subscription-Manager* (umgesetzt in `CanKit.Pro.RawCan`), aufbauend auf L1 `ICanBus.FrameObserved` (`src/core/CanKit.Abstractions/API/Can/ICanBus.cs`) | Virtual-Loopback-Integrationstest, Lasttest |
| FR-RAW-020..024 | L2 – *Protokollinstanz-Aktor/Scheduler* (umgesetzt als eigenständiges `CanKit.Pro.Actor`, `IProtocolActor`/`ProtocolActor`); von `CanKit.Pro.IsoTp` / `CanKit.Pro.J1939Tp` genutzt | Stress-/Nebenläufigkeitstest |
| FR-RAW-030..034 | L2 – *TX-Confirm-Abstraktion* (umgesetzt in `CanKit.Pro.RawCan`), aufbauend auf L1 `CanFeature.Echo`, `ITransceiver.Transmit` | Virtual-Loopback-Integrationstest (mit/ohne Echo) |
| FR-RAW-040..041 | L2 – *Adressierungs-Helfer* (umgesetzt als eigenständiges `CanKit.Pro.Addressing`: `CanIdRange`, `J1939Id`/`J1939Fields`; FR-RAW-041 als `CanIdFilter.Overlaps`/`ICanBusService.FindOverlappingFilterSubscriptions()` in `CanKit.Pro.RawCan`) | Unit-Test |
| FR-RAW-050..052 | L2 – *Fehler-/Timeout-Infrastruktur* (FR-RAW-050/051 umgesetzt als eigenständiges `CanKit.Pro.Reliability`: `IDeadlineScheduler`/`DeadlineScheduler`/`Deadline` als aktorgetriebene Deadline-Primitive, deren Ablauf über `IProtocolActor.Schedule` tatsächlich geprüft und gemeldet wird (FR-RAW-050); `BusStateMonitor`/`BusStateChangedEventArgs`/`BusStateExtensions` für gepushte `ICanBus.BusState`-Übergänge (FR-RAW-051), aufbauend auf `CanKit.Pro.Actor` und L1 `ICanBus.BusState`). FR-RAW-052 (reservierte/ungültige Protokollwerte) bleibt **zurückgestellt** und dem künftigen ISO-TP-Codec-Fix FR-TP-007 zugeordnet (Review §1.1 Punkt 6), da protokollspezifisch statt generische L2-Primitive. | Unit-Test, Integrationstest |
| FR-TP-001..020 | L3 – ISO-TP-Transport, `CanKit.Pro.IsoTp` (`IIsoTpChannel`, `IsoTpFrameCodec`, Actor-Runtime) | Unit-Test (Codec/Timing), Virtual-Loopback-Integrationstest, HIL-Stichprobe |
| FR-TP-030..035 | L3 – *J1939-Transport* (umgesetzt als eigenständiges `CanKit.Pro.J1939Tp`: `J1939Tp`/`J1939TpChannel`/`J1939TpOptions`/`J1939TpFrames`/`J1939TpAbortReason`, aufbauend auf `CanKit.Pro.Addressing` (`J1939Id`/`J1939Pgn`) sowie `CanKit.Pro.Actor`/`CanKit.Pro.RawCan`/`CanKit.Pro.Reliability`; MVP, `IsPackable=false`) | Virtual-Loopback-Integrationstest |
| FR-UDS-001..010 | L4 – **UDS-Client-MVP umgesetzt** (`CanKit.Pro.Uds`, `IUdsClient`/`UdsClient` auf `IIsoTpChannel`); FR-UDS-011 (Multi-DID, SHOULD) und FR-UDS-012 (Upload/Download 0x34/0x35/0x36/0x37, COULD) ebenfalls umgesetzt | Virtual-Loopback-Integrationstest (simulierte ECU, `tests/CanKit.Tests/TestCases/Uds/UdsClientTests.cs` + `UdsTransferTests.cs`), HIL-Stichprobe |
| FR-CO-001..012 | L4 – **CANopen-Stack-MVP umgesetzt** (`CanKit.Pro.CANopen`, `ICanOpenNode`/`CanOpen`/`ObjectDictionary`/`CanOpenNode` auf `CanKit.Pro.RawCan`/`CanKit.Pro.Actor`/`CanKit.Pro.Reliability`); FR-CO-004 (Block-Transfer) und FR-CO-009 (Node-Guarding) implementiert (`CanOpenNode.SdoBlock.cs`, `CanOpenNode.NodeGuarding.cs`) | Virtual-Loopback-Integrationstest (`tests/CanKit.Tests/TestCases/CANopen/`, inkl. Block-Roundtrip, Blockgrößen-Renegotiation und Node-Guarding-Toggle/Timeout), HIL-Stichprobe |
| FR-J1939-001..007 | L4 – *J1939-Applikationsschicht* (`src/protocols/CanKit.Pro.J1939`, MVP; FR-J1939-001..006 Must umgesetzt, FR-J1939-007 Should — jede periodische PGN läuft einheitlich über die `SendAsync`-Actor-Schleife (L2-Scheduling via `DeadlineScheduler`); native L1-`IPeriodicTx`-Optimierung zurückgestellt; der zugrundeliegende L1-Blocker (SoftwarePeriodicTx verschluckte `Transmit`-Ausnahmen) ist behoben (`IPeriodicTx.Faulted` = `EventHandler<Exception>`, außerhalb des Gates ausgelöst, Loop bleibt am Leben); `Transmit`-Fehler und Claim-Gate-Ausnahmen der aktuellen Actor-Schleife werden weiterhin über `BackgroundExceptionOccurred` gemeldet) | Virtual-Loopback-Integrationstest (`tests/CanKit.Tests/TestCases/J1939/J1939NodeTests.cs`), HIL-Stichprobe |
| FR-HAWE-001..005 | L4 – *HAWE-Erweiterungsrahmen* (`src/protocols/CanKit.Pro.Hawe`, SPI `IHaweCodecRegistry`/`HaweChannel`) | Architekturreview, Virtual-Loopback-Integrationstest (generischer `FakePatternCodec` in `tests/CanKit.Tests/TestCases/HaweFrameworkTests.cs`) |
| NFR-001..003 | L2/L3 Timing-Infrastruktur, L1 `SoftwarePeriodicTx`/`PreciseDelay` (`src/core/CanKit.Core/Utils/SoftwarePeriodicTx.cs`) | Performance-/Timing-Test; NFR-001-Synthetiktest `tests/CanKit.Tests/TestCases/PeriodicJitterTests.cs` gegen `CanKit.Adapter.Virtual` |
| NFR-004, CON-001 | Alle Ebenen – Multi-Targeting (`src/Directory.Build.props`) | CI-Testmatrix |
| NFR-005, CON-002, CON-003 | L0 – Vendor-Adapter (`src/adapters/*`) | Buildmatrix, Codereview |
| NFR-006 | L2/L3/L4 – Fehlerweiterleitung, aufbauend auf L1 `ICanBus.FaultOccurred`/`BackgroundExceptionOccurred` | Unit-Test je Fehlerpfad |
| NFR-007 | L2/L3 – Speicher-/Pooling-Strategie (aufbauend auf FR-RAW-001..005) | Benchmark |
| NFR-008 | L2 – Registry/Scheduler-Synchronisation, L1 `CanRegistry` (`src/core/CanKit.Core/Registry/CanRegistry.cs`) | Stresstest |
| NFR-009 | Alle Ebenen – Testinfrastruktur, L0 `CanKit.Adapter.Virtual`, Fake-Native-Muster | CI-Lauf |
| NFR-010 | L2/L3/L4 – SPI-Registrierungsmuster, L1 `CanRegistryEntryAttribute`/`ICanRegistryEntry` (`src/core/CanKit.Abstractions/Attributes/CanRegistryEntryAttribute.cs`) | Architekturreview |
| NFR-011, NFR-012 | Alle neuen Ebenen – API-Namenskonventionen | API-Review |
| CON-004..008 | Release-/CI-Prozess (`eng/`, `.github/workflows`) | Prozessreview |

---

## Anhang: Offene Punkte für die nächste Iteration

1. **Entschieden für NFR-001:** `SoftwarePeriodicTx` muss auf Windows- und Linux-Referenzhosts p99 des absoluten Inter-Sende-Jitters ≤ 1,0 ms einhalten, wenn die Periode ≥ 1 ms ist, gemessen über ≥ 500 Perioden bei idle Prozess und Virtual- oder Null-Bus-artigem Transmit. Hardware-BCM bzw. Vendor-periodic-TX wird separat per HIL validiert; CI sammelt Software-Timing-Statistik gegen Virtual und nutzt eine weiche mittelwertrelative p99 ≤ 25,0 ms Grenze statt eines absoluten Hardware-Freigabe-Gates (vermeidet Flakes durch systematische Timer-Verschiebung auf Shared Windows-Runnern).
2. J1939-TP ist als MVP-Paket `CanKit.Pro.J1939Tp` umgesetzt (siehe 4.2.2); CANopen ist als MVP-Paket `CanKit.Pro.CANopen` umgesetzt (siehe 4.3.2); die J1939-Applikationsschicht ist als MVP-Paket `CanKit.Pro.J1939` umgesetzt (siehe 4.3.3).
3. HAWE-Anforderungen (Abschnitt 4.3.4) sind bewusst als Rahmen gehalten und müssen bei Vorliegen der vertraulichen Spezifikation verfeinert werden (Annahme A-6). Der öffentliche generische Rahmen (`CanKit.Pro.Hawe`) ist umgesetzt; die konkrete HAWE-Codec-Implementierung erfolgt außerhalb dieses Repositorys (CON-006).
4. Die Traceability-Matrix referenziert `docs/architecture/arc42-CanKit.md`, das zum Zeitpunkt dieser SRS noch nicht vorliegt; Bausteinnamen sind als *geplant* markiert und beim Erscheinen des Architekturdokuments gegenzuprüfen. **Nachgezogen (2026-07-21):** das arc42-Dokument liegt vor; die Bausteinnamen der Matrix wurden gegengeprüft und stimmen mit den dortigen Bausteinen (L2-Demux in `CanKit.Pro.RawCan`, Aktor `CanKit.Pro.Actor`, TX-Confirm `CanKit.Pro.RawCan`, Adressierung `CanKit.Pro.Addressing`, Fehler-/Timeout-Infrastruktur `CanKit.Pro.Reliability`, L3/L4-Pakete) überein.
