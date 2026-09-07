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
