# Grow OS Fork AI — Unterschiede zum Original

Dieses Repository ist ein Ableger von [Grow OS](https://github.com/Nerdstreak/Grow-Operation-System)
(Nerdstreak, MIT-Lizenz). Ziel: Weiterentwicklungen ausprobieren, die dem
Original-Projekt später als Pull Request angeboten werden können.

**Basis:** Grow OS 2.0.0-beta.65 · **Add-on:** „Grow OS Fork AI" (`grow_os_fork_ai`),
läuft parallel zum Original, eigene Datenbank, eigenes Image.

## Neue Funktionen gegenüber dem Original

| Seit | Funktion | Wo | Dateien |
|---|---|---|---|
| forkai.2 | Düngeprogramm **„SKX Canna Aqua (R/DWC Growplan Rev.01)"** mit 14-Spalten-Wochen-Feed-Chart (Root, Vega 1–4, Flores 1–8, Flush; ml/L, Ziel-EC, Ziel-pH) und den Plan-Regeln als Text | Wissen → Düngeprogramme | `GrowDiary.Web/wwwroot/knowledge-defaults/nutrient-programs/skx-canna-aqua.json` |
| forkai.3 | Bugfix: Addback-Seite zeigt das am Grow gespeicherte Programm (per Id) statt eines Teilstring-Treffers aus dem Freitext (betraf „SKX Canna Aqua“ vs. „Canna Aqua“) | Addback | `GrowDiary.React/src/pages/AddbackPage.tsx` |
| forkai.5 | Feed-Chart eines Düngeprogramms als Wochentabelle auf der Wissensseite (Komponenten × Wochen, Ziel-EC/pH); `feedChart` neu im Programm-DTO | Wissen → Programme | `KnowledgePage.tsx`, `types/knowledge.ts`, `KnowledgeDto.cs`, `KnowledgeMapping.cs`, `NutrientProgram.cs`, `CultivationKnowledgeService.cs` |
| forkai.6 | Seite **Kosten**: Strom aus HA-Zählerständen je Grow und Phase (Worker hält den kWh-Zähler bei Grow-Start/Phasenwechsel/täglich fest), Verbrauchsartikel mit Nachfüllungen, Laufzeit-Prognose, Kosten je Tag/Pflanze, Ernteprognose; eigene `Fork*`-Tabellen, API `/api/kosten` | Betrieb → Kosten | `Models/Kosten.cs`, `Infrastructure/KostenRepository.cs`, `Services/KostenSeiteService.cs`, `Services/ZaehlerstandWorker.cs`, `Api/Controllers/KostenApiController.cs`, `pages/KostenPage.tsx`, `features/kosten/*`, `docs/referenz/kosten.md` |
| forkai.7 | Kosten-Seite auf die Original-Bausteine umgestellt (`v1-kpi-grid`, Tokens statt eigener Werte, Tabelle am Telefon) | Betrieb → Kosten | `pages/KostenPage.tsx`, `features/kosten/kosten.css` |
| forkai.8 | Verbrauchsartikel mit Anzeigename, Hersteller, Produktbezeichnung, Preis je Gebinde (Vorbelegung der Erfassung), Bearbeiten-Formular; Spalten per einmaligem `ALTER TABLE` | Betrieb → Kosten | `Models/Kosten.cs`, `KostenRepository.cs`, `KostenApiController.cs`, `KostenSeiteService.cs`, `pages/KostenPage.tsx` |
| forkai.9 | Kosten-Seite mit Reitern (Strom/Verbrauch/Anschaffungen/Durchgänge), drei Erfassungs-Knöpfe oben; neuer Block **Anschaffungen** (Stück × Einzelpreis, Grow oder Lager, optional Hardware-Artikel + Journal); „Für Grow“-Auswahl bei Nachfüllungen; Einheiten-Dropdown, „Inhalt je Packung“ | Betrieb → Kosten | `Models/Kosten.cs`, `KostenRepository.cs`, `KostenApiController.cs`, `KostenSeiteService.cs`, `pages/KostenPage.tsx`, `features/kosten/*` |

## Geplant

- Sollwertprofil um **Wochenwerte** erweitern (VPD, Temp/RH, CO₂, PPFD, Wassertemp, Lichtzeit je Woche) — heute nur 6 Phasen.
- **Export der aktiven Wochen-Sollwerte nach Home Assistant** (Helfer), damit HA-Automationen danach regeln („Grow OS plant, HA regelt").

## Nur für den Fork geändert (nicht zur Übernahme gedacht)

- `grow-os/config.yaml`: Name, Slug, Image, Version `2.0.0-forkai.N`
- `repository.yaml`: Add-on-Repository des Forks

## Abgleich mit dem Original

Änderungen des Originals kommen nicht automatisch. Vorgehen: auf GitHub „Sync fork"
(holt `main` des Originals herein), Konflikte in `grow-os/config.yaml` und
`grow-os/CHANGELOG.md` von Hand lösen, Version `forkai.N+1` setzen, Changelog,
Build, Release. Eigene Funktionen liegen möglichst in eigenen Dateien, damit der
Abgleich konfliktarm bleibt.
