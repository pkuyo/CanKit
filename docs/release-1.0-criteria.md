# CanKit.Pro 1.0-Releasekriterien

**Stand:** 2026-07-21 · Bezug: Gap-Review `docs/reviews/2026-07-21-implementation-gap-review.md`,
Release-Prozess `docs/release-process.md`.

Dieses Dokument definiert, wann ein `CanKit.Pro.*`-Paket von `IsPackable=false` auf
`IsPackable=true` gedreht und damit veröffentlicht werden darf — und welche
Änderungspolitik ab dann gilt.

## 1. Pack-Gates (pro Paket)

Ein Pro-Paket darf nur veröffentlicht werden, wenn **alle** Kriterien erfüllt sind:

| # | Kriterium | Nachweis |
|---|---|---|
| G1 | Alle **Must**-Anforderungen der SRS für das Paket sind implementiert **und durch Tests verifiziert** (Unit oder Virtual-Loopback, laufend in CI). | SRS-Traceability + CI-Workflow grün |
| G2 | Alle **Must-NFRs**, die das Paket betreffen, sind verifiziert (z. B. Timing/Threading/Fehlerpfade). | SRS §5, Tests |
| G3 | **README.md** des Pakets beschreibt Umfang, Status und bekannte Grenzen ehrlich; keine irreführenden Statusangaben. | Paket-README |
| G4 | **API-Snapshot** existiert und ist aktuell (`tests/CanKit.Tests/ApiApprovals/<PackageId>.approved.txt`, Test `PublicApiSurfaceTests`). | § 3 dieses Dokuments |
| G5 | Paket ist in **`eng/package-smoke`** abgedeckt (Referenz + Typ-Smoke). | `eng/package-smoke` |
| G6 | Release-Notes unter `eng/release-notes/<PackageId>/<Version>.md` und CHANGELOG-Eintrag vorhanden. | `eng/`, `CHANGELOG.md` |
| G7 | Keine bekannten kritischen Defekte offen; bekannte Grenzen sind im README unter „Open items" dokumentiert. | Review-Stand |

### Stand je Paket (2026-07-21)

| Paket | G1 | G2 | G3 | G4 | G5 | G6 | G7 | Status |
|---|---|---|---|---|---|---|---|---|
| CanKit.Pro.Actor | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | **packbar (0.1.0)** |
| CanKit.Pro.Addressing | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | **packbar (0.1.0)** |
| CanKit.Pro.RawCan | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | **packbar (0.1.0)** |
| CanKit.Pro.Reliability | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | **packbar (0.1.0)** |
| CanKit.Pro.IsoTp | ✅ | ✅ | ✅ | ⬜ Snapshot fehlt | ⬜ | ⬜ | ✅ | `IsPackable=false` |
| CanKit.Pro.J1939Tp | ✅ | ✅ | ✅ | ⬜ | ⬜ | ⬜ | ✅ | `IsPackable=false` |
| CanKit.Pro.Uds | ✅ | ✅ | ✅ | ⬜ | ⬜ | ⬜ | ✅ | `IsPackable=false` |
| CanKit.Pro.CANopen | ✅ | ✅ | ✅ | ⬜ | ⬜ | ⬜ | ✅ (pst>0 dokumentiert) | `IsPackable=false` |
| CanKit.Pro.J1939 | ✅ | ✅ | ✅ | ⬜ | ⬜ | ⬜ | ✅ | `IsPackable=false` |
| CanKit.Pro.Hawe | ✅ (Rahmen) | ✅ | ✅ | ⬜ | ⬜ | ⬜ | ⏸ wartet auf externe Spezifikation (A-6/CON-006) | `IsPackable=false` |

L3/L4-Pakete drehen erst dann auf `IsPackable=true`, wenn ihre Oberfläche als stabil genug
für § 2 bewertet wird (empfohlen: nach HIL-Stichprobe gemäß `docs/hil-test-strategy.md`).

## 2. Änderungspolitik (Breaking Changes)

- **Vor 1.0 (0.x):** öffentliche API kann sich in Minor-Releases ändern; jede Änderung der
  öffentlichen Oberfläche muss aber im API-Snapshot (§ 3) und im CHANGELOG als solche
  erkennbar sein. Binär-brechende Änderungen werden vermieden, wo vertretbar.
- **Ab 1.0:** striktes SemVer. Breaking Changes nur mit Major-Bump, Migration-Hinweis in den
  Release-Notes und vorheriger Obsoletion (`[Obsolete]` mit Hinweistext) über mindestens
  einen Minor-Zyklus, sofern technisch möglich.
- **Interne Oberflächen:** `internal` (ggf. per `InternalsVisibleTo` nur für Tests
  freigegeben) ist kein API-Versprechen und kann sich jederzeit ändern.

## 3. API-Tracking (verbindlich)

- Jedes **veröffentlichte** Paket (`IsPackable=true`) hat eine approved API-Datei unter
  `tests/CanKit.Tests/ApiApprovals/<PackageId>.approved.txt`, geprüft durch
  `tests/CanKit.Tests/TestCases/PublicApiSurfaceTests.cs` (reflexionsbasiert, ohne externe
  Abhängigkeit).
- Schlägt der Test fehl, legt er `<PackageId>.received.txt` daneben. Vorgehen:
  1. Diff `received` vs. `approved` prüfen.
  2. Ist die Änderung beabsichtigt: `received` → `approved` umbenennen **und im selben PR**
     committen (mit CHANGELOG-Hinweis bei sichtbarer Änderung).
  3. Ist sie nicht beabsichtigt: Code korrigieren, Approval unverändert lassen.
- Beim Pack-Gate eines weiteren Pakets (§ 1, G4) wird es in `PublicApiSurfaceTests.Tracked`
  aufgenommen und die initiale Approval erzeugt.
