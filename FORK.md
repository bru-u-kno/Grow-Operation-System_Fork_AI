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
| forkai.10 | Kosten: Sprung zum Formular beim ersten Tipp (zwei Frames nach dem Reiterwechsel), `scroll-ziel` gegen die feste Kopfleiste | Betrieb → Kosten | `pages/KostenPage.tsx` |
| forkai.11 | Hersteller/Produkt-Vorschläge beim Tippen (`datalist`, Quelle: Artikel, Anschaffungen, Hardware) und Schreibweisen-Angleich beim Speichern (`Stammdaten.Angleichen`) | Betrieb → Kosten | `Models/Kosten.cs`, `KostenSeiteService.cs`, `KostenApiController.cs`, `pages/KostenPage.tsx` |
| forkai.12 | Vorschlagsliste unter dem Feld (`VorschlagsFeld`, eigene Liste statt `datalist`, Tastaturbedienung), Produkt bringt Hersteller mit | Betrieb → Kosten | `pages/KostenPage.tsx`, `features/kosten/kosten.css` |
| forkai.13 | **Mobile Navigation neu**: eigene Titelzeile (Name + aktuelle Seite, „+" für Erfassen, „⌂" zurück nach Home Assistant), Icon-Leiste mit anpassbarer Reihenfolge (API `/api/navbar`), Erfassen-Blatt für Messung/Addback/Wasserwechsel/Notiz/Kosten, „Leiste anpassen" unter Mehr; behebt die umbrechende Reiterzeile, die die Bewertungsscheibe halbierte | überall (Rahmen) | `AppShell.tsx`, `navigation.ts`, `useNavBar.ts`, `useHomeAssistantFrame.ts`, `components/ErfassenSheet.tsx`, `components/LeisteAnpassen.tsx`, `styles/forkai-shell.css`, `Api/Controllers/NavBarApiController.cs` |
| forkai.14 | **Alarmgrenzen dürfen dem Wochenplan folgen**: je Messgröße umschaltbar zwischen *Fest* (eingetragene Zahlen, unverändert) und *Plan* (Zielband der laufenden Phase/Woche ± Toleranz). Damit wandern pH- und EC-Grenzen mit der Blütewoche des Feed-Charts mit, statt ab Woche 4 dauerhaft zu melden. Die Lesart je Messgröße liegt jetzt gemeinsam in `Zielband.FuerMetrik`, damit Kachel und Alarm dieselbe Zahl nennen | Betrieb → Regeln & Automatik → Grenzwerte | `Services/Grenzwertquelle.cs`, `Services/Zielband.cs`, `Services/AlertEvaluationService.cs`, `Services/UserTargets.cs`, `Models/TentAlertRule.cs`, `Api/Contracts/AlertContracts.cs`, `Api/Controllers/AlertsApiController.cs`, `Infrastructure/AlertRuleRepository.cs`, `pages/AlertsPage.tsx`, `features/alerts/grenzwerte-modell.ts` |

## Geplant

- Sollwertprofil um **Wochenwerte** erweitern (VPD, Temp/RH, CO₂, PPFD, Wassertemp, Lichtzeit je Woche) — heute nur 6 Phasen.
- **Export der aktiven Wochen-Sollwerte nach Home Assistant** (Helfer), damit HA-Automationen danach regeln („Grow OS plant, HA regelt").

## Empfohlen: ohne die Kopfleiste von Home Assistant

Das Add-on läuft im Ingress von Home Assistant, also in einem iframe unterhalb
der HA-Kopfleiste mit dem Menü-Zeichen. **Ein Add-on kann diese Leiste nicht
entfernen** — sie gehört Home Assistant, nicht uns. Am Telefon kostet sie
zusammen mit der eigenen Titelzeile spürbar Platz.

Wer sie loswerden will, installiert die HACS-Integration
[**Ingress**](https://github.com/lovelylain/hass_ingress) (`lovelylain/hass_ingress`)
und trägt in `configuration.yaml` ein:

```yaml
ingress:
  growos:
    work_mode: hassio
    url: d48160c2_grow_os_fork_ai   # eigener Add-on-Slug, siehe Add-on-Seite in HA
    ui_mode: normal                 # 'normal' = ohne HA-Kopfleiste
    title: Grow OS
    icon: mdi:sprout
```

Danach Home Assistant neu starten. In der Seitenleiste erscheint ein neuer
Eintrag, der Grow OS bildschirmfüllend öffnet; der alte Add-on-Eintrag bleibt
daneben bestehen.

**Das ist freiwillig.** Ohne die Integration funktioniert alles genauso — die
Titelzeile erkennt selbst, ob die HA-Kopfleiste über ihr liegt, und lässt dann
den Namenszug weg, damit er nicht zweimal untereinander steht
(`useHomeAssistantFrame.ts`). Das „⌂"-Zeichen springt in beiden Fällen zurück;
wohin, steht unter Einstellungen → Darstellung (Voreinstellung `/lovelace/0`).

Nicht gangbar sind zwei naheliegende Wege, beide ausprobiert: eine
`webpage`-Karte auf die Ingress-Adresse verliert nach kurzer Zeit ihr
Sitzungs-Cookie, und `kiosk-mode` blendet nur Kopfzeilen von
Lovelace-Dashboards aus — ein Ingress-Panel ist keines.

## Nur für den Fork geändert (nicht zur Übernahme gedacht)

- `grow-os/config.yaml`: Name, Slug, Image, Version `2.0.0-forkai.N`
- `repository.yaml`: Add-on-Repository des Forks

## Abgleich mit dem Original

Änderungen des Originals kommen nicht automatisch. Vorgehen: auf GitHub „Sync fork"
(holt `main` des Originals herein), Konflikte in `grow-os/config.yaml` und
`grow-os/CHANGELOG.md` von Hand lösen, Version `forkai.N+1` setzen, Changelog,
Build, Release. Eigene Funktionen liegen möglichst in eigenen Dateien, damit der
Abgleich konfliktarm bleibt.
