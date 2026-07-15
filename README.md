# CanKit.Pro

**Erweiterte CAN-Protokoll-Bibliothek für .NET**  
Höhere Schichten auf Basis von [CanKit](https://github.com/pkuyo/CanKit)

---

## Über CanKit.Pro

**CanKit.Pro** ist ein Fork von **CanKit** und erweitert die bewährte, hochperformante und herstellerübergreifende CAN-Basis um **höhere Protokolle** wie:

- **CANopen** (PDO, SDO, NMT, Heartbeat, LSS, etc.)
- **J1939** (PGN, SPN, Transport Protocol, BAM, CM etc.)
- **UDS** (ISO 14229 – Unified Diagnostic Services)
- Weitere Protokolle (in Planung): ISO-TP, XCP, DeviceNet, Safety (z. B. CANopen Safety)

Ziel ist es, eine moderne, einheitliche und typsichere .NET-Bibliothek für industrielle, automotive und embedded CAN-Anwendungen bereitzustellen.

---

## Features

### Kern (von CanKit übernommen)
- Herstellerübergreifende Adapter-Unterstützung (PCAN, Kvaser, Vector, SocketCAN, ZLG, …)
- Hohe Performance & Async-First API
- CAN + CAN FD
- Einheitliche Endpunkt-Konfiguration

### Neue Protokoll-Features in CanKit.Pro

- **CANopen**
  - Vollständiger Object Dictionary Support
  - PDO Mapping (statisch + dynamisch)
  - SDO Client & Server
  - NMT Master/Slave
  - Heartbeat Producer/Consumer

- **J1939**
  - PGN-basiertes Messaging
  - Multi-Packet Transport (TP.BAM & TP.CM)
  - Address Claiming
  - DM1/DM2 Diagnose-Nachrichten

- **UDS**
  - ISO-14229 Diagnoseprotokoll
  - Session Management
  - Standard Services (0x10, 0x22, 0x2E, 0x31, 0x3E, …)
  - Security Access

- Moderne C# APIs (Records, Source Generators, stark typisierte Nachrichten)

---

## Installation

```bash
# Core + Adapter (wie bei CanKit)
dotnet add package CanKit.Core
dotnet add package CanKit.Adapter.PCAN     # oder Kvaser, Vector, SocketCAN...

# Protokoll-Erweiterungen
dotnet add package CanKit.Pro.CanOpen
dotnet add package CanKit.Pro.J1939
dotnet add package CanKit.Pro.UDS
```

---

## Schnellstart

### CANopen Beispiel

```csharp
using CanKit.Pro.CanOpen;

// Bus öffnen (wie gewohnt)
using var bus = CanBus.Open("pcan://PCAN_USBBUS1", cfg => cfg.Baud(500_000));

var node = new CanOpenNode(bus, nodeId: 0x10);

// SDO Read
var value = await node.ReadAsync<byte>(0x1000, 0x00); // Device Type

// PDO Mapping
node.ConfigurePdo(...);
```

### J1939 Beispiel

```csharp
var j1939 = new J1939Controller(bus);

j1939.MessageReceived += (sender, msg) =>
{
    if (msg.PGN == Pgns.EngineTemperature1)
        Console.WriteLine($`Öltemperatur: {msg.GetSpn(Spns.EngineOilTemperature1)}`);
};
```

---

## Projektstruktur

```
CanKit.Pro/
├── src/
│   ├── CanKit.Pro.Core/          # Gemeinsame Typen & Hilfen
│   ├── CanKit.Pro.CanOpen/
│   ├── CanKit.Pro.J1939/
│   ├── CanKit.Pro.UDS/
│   └── CanKit.Pro.ISO-TP/
├── samples/
├── tests/
└── docs/
```

---

## Roadmap

- [ ] Vollständige CANopen Implementierung
- [ ] J1939 Transport Protocol & Address Claim
- [ ] UDS Diagnose-Stack
- [ ] Source Generatoren für Object Dictionaries / PGNs
- [ ] DBC & EDS Import/Export
- [ ] GUI-Tool (CanKit.Pro.Toolkit)

---

## Mitwirken

Beiträge sind herzlich willkommen!  
Siehe [CONTRIBUTING.md](CONTRIBUTING.md) für Details.

---

## Lizenz

Dieses Projekt basiert auf [CanKit](https://github.com/pkuyo/CanKit) und steht unter der **Apache License 2.0**.

---

**Hinweis**: Dies ist ein aktiver Fork / Community-Projekt und nicht mit dem Original-CanKit verbunden.

---

**Made with ❤️ für die CAN-Community**