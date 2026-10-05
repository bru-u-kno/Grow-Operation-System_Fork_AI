# Grow OS — UI Design-System & Redesign-Briefing

> Stand: 2026-06-04. Bestandsaufnahme der aktuellen React-UI als Grundlage für einen
> Redesign mit dem `frontend-design`-Skill. Beschreibt **Ist-Zustand**, nicht Soll.

## 1. Was die App ist
Local-first Operations-System für RDWC-/Hydro-Cannabis-Grows, ausgeliefert als Home-Assistant-Add-on (native HA-Integration). Single-Page-React-App (React 19, Vite 8, react-router 7), die vom ASP.NET-Backend aus `wwwroot` ausgeliefert wird. Dünner API-Client (`src/api.ts` → `apiFetch`), Aufrufe inline in Seiten/Hooks.

## 2. App-Shell & Navigation (`src/App.tsx`)
Drei Navigations-Oberflächen, ein gemeinsamer Frame (`v1-app-shell`):
- **Desktop-Sidebar** (`v1-desktop-nav`, 228px): Brand + Gruppen „Core" / „Mehr".
- **Mobile-Topbar** (`v1-mobile-topbar`) + ausklappbares „Mehr"-Panel (gruppiert: Setup / Integration / Wissen & System).
- **Mobile-Bottom-Nav** (`v1-bottom-nav`): 4 Primär-Ziele (Live, Addback, Messung, Grows).

Primärnavigation (Core): **Live · Addback · Messung · Grows**. Sekundär: Aufgaben, Zelte, Hydro, Home Assistant, Sensoren, Wissen, Vergleich, Archiv, Einstellungen.

## 3. Routen & Seiten (22 Seiten)
Priorisiert nach Redesign-Aufwand.

### Hoher Aufwand / Kernflüsse
| Seite | Route | Zweck | Größe |
|---|---|---|---|
| HydroPage | `/hydro`, `/hydro/new` | Liste + Wizard + RDWC-Visualpreview, Dependency-Blocking | 448 |
| GrowDetailPage | `/grows/:id` | Multi-Tab Grow (Overview/Diagnose/SOPs/Messungen/Journal/Automation) | 366 |
| GrowSetupPage | `/grows/new`, `/grows/:id/setup` | 6-Schritt-Wizard Grow anlegen/bearbeiten | 168 |

### Mittlerer Aufwand
| Seite | Route | Zweck | Größe |
|---|---|---|---|
| TentsPage | `/zelte` | Zelt-Liste + Inline-Formular, Live-Daten, Löschsperre | 427 |
| ManualMeasurementPage | `/messung` | Messwert-Erfassung (Klima/Hydro/Foto) | 425 |
| HardwarePage | `/hardware` | Multi-Tab Sensor-Status/Inventar/Wartung | 347 |
| KnowledgePage | `/wissen` | Wissens-Wiki + Suche | 288 |
| HomeAssistantPage | `/home-assistant` | HA-Verbindung + Sensor-Mapping + Kamera-Test | 261 |
| GrowsPage | `/grows` | Grow-Grid + CRUD | 264 |
| ReleasePage | `/release` | Grow Export/Import | 241 |
| MobileActionPage | `/aufgaben` | Aktions-Hub (Risiken/Tasks/Wartung) + Risk-Aktionen | 240 |
| AddbackPage | `/addback` | Nachfüllen als Vorgang (VorgangAblauf) + bisherige Einträge | 160 |
| SettingsPage | `/settings` | Backup + System-/HA-/Index-Export | 230 |
| TentDetailPage | `/zelte/:id` | Zelt-Live + Räume/Hydro/Grows/Pflanzen | 167 |
| LiveDashboardPage | `/` | Live-Dashboard pro Zelt (Desktop/Mobile getrennt) | 126 |

### Geringer Aufwand
HarvestPage (`/grows/:id/harvest`, 204) · MeasurementEditPage (412, Foto-Galerie) · DeviceConnectPage (`/connect`, 196, QR) · AnalysisPage (`/analyse`, 146) · ArchivePage (`/archiv`, 98) · HydroDetailPage (`/hydro/:id`, 79).

> **Nicht auf das Komponenten-System migriert** (eigenes Topbar/Scroll-Markup statt `V1Page`): HarvestPage, MeasurementEditPage, AnalysisPage, ArchivePage.

## 4. Komponenten-Bibliothek
### Primitive — `src/components/v1.tsx`
`V1Page` (Hero+Titel-Wrapper) · `V1Section` (Block mit Kopf) · `V1Card` (Container, `tone`) · `V1Button` / `V1LinkButton` (Varianten: primary/secondary/ghost/danger) · `V1Badge` · `V1Stat` (KPI) · `V1Empty` · `V1Alert` · `V1Tabs` · `V1Field` (Formularfeld) · `V1Switch` · `V1Wizard` (Schritt-Anzeige).
Gemeinsamer Typ `Tone = 'neutral' | 'ok' | 'warn' | 'critical' | 'accent'`.

Nutzung: `V1Page` (~18 Seiten), `V1Card` (fast überall), `V1Section` (21), `V1Stat`/`V1Field`/`V1Badge` (15–17), `V1Tabs` (5), `V1Wizard` (3).

### Feature-Komponenten
- `features/live/`: DesktopLiveDashboard, MobileLiveDashboard, CameraTile, LiveMetric, RiskSummaryCard, `live-model`, `useIsPhoneViewport`.
- `features/grow-detail/`: 6 Section-Komponenten (OverviewHero, Diagnosis, Sop, Measurements, Journal, Automation) + 4 Hooks (useGrowDetailBundle/Resources/Mutations/Automation) + `grow-detail-model`.
- `features/risks/`: `RiskActionCard` (Risiko bestätigen/erledigen/SOP starten).
- `components/`: `RdwcPreview` (RDWC-Visualisierung in HydroPage).

## 5. Design-Tokens ⚠️ ZWEI parallele Systeme
**System A — Basis** (`00-base.css :root`, **oklch**, mit Light-Theme `html[data-theme="light"]`):
`--bg --surface --surface2/3 --border --border2 --text --muted --faint --green --amber --red --blue (+ *-bg) --radius:10px --radius-sm:7px --sidebar:228px --font-sans(Inter) --mono(JetBrains Mono) --shadow`. Chart-Farben `--chart-1..5` in `10-…`.

**System B — V1** (`40-v1-core.css :root`, **Hex/rgba**, **kein** Light-Theme):
`--v1-bg:#000603 --v1-panel --v1-panel-2 --v1-line(rgba grün .14) --v1-line-strong(.28) --v1-text:#effff2 --v1-muted --v1-soft --v1-green:#43d45a --v1-green-dark --v1-warn:#f5c84b --v1-red:#ff5c5c --v1-cyan --v1-radius:22px --v1-radius-sm:14px`.

**Konflikt:** unterschiedliches Grün (oklch ~`#7fff7f` vs `#43d45a`), unterschiedlicher Radius (10/7 vs 22/14), V1 hat **keinen** Light-Mode. Seiten wirken je nach genutztem System optisch entkoppelt. → **Token-Unifizierung ist Redesign-Aufgabe #1.**

## 6. CSS-Architektur (11 Dateien, ~9.800 Zeilen)
Geladen via `src/index.css` (`@import 00…90`), **plus** `src/rc2-overrides.css` separat in `App.tsx`.

| Datei | Z. | Inhalt |
|---|---|---|
| 00-base | 911 | Reset, Tokens (A), App-Shell, Basis-Karten/Buttons/Felder |
| 10-grow-wizard-legacy | 389 | Alter Grow-Wizard, Chart-Overrides (Übergangsschicht) |
| 20-settings | 50 | Settings-Grid |
| 30-live-home | 830 | „Frontend-Rebuild-2": Live-Dashboard, Ops-Karten, HA, Addback-Hub, Wiki |
| 40-v1-core | 260 | V1-Kit: Tokens (B), Shell, Karten/Badges/Buttons/Tabs/Wizard |
| 50-v1-polish | 346 | V1 Mobile-Density-Iteration |
| 60-v1-rc2 | 257 | V1 RC2: Live-Metrik-Paare, Addback-Command-View |
| 70-addback-assistant | 430 | Geführter Addback-Assistent (Sticky-Rail) |
| 80-grow-wizard-final | 320 | Grow-Wizard finale Iteration |
| 90-operations | 571 | Ops-Hub: Kalibrierung/Wartung, Score-Karten, Risk-Aktionen |
| **rc2-overrides** | **2740** | **Patch-Schicht (38%): Text-Overflow, Safe-Area, Grids, 105×`!important`** |

## 7. Hauptprobleme / Redesign-Chancen
1. **Zwei Token-Systeme vereinheitlichen** (A+B → ein Set mit semantischen Namen `--accent/--warn/--danger`, Light+Dark).
2. **Light-Theme aktivieren** — CSS existiert, aber kein `data-theme`-Toggle im React. Theme-Switch nötig.
3. **`rc2-overrides.css` auflösen** — 2740 Z. Patches + 105 `!important` zurück in die Cascade/Feature-Dateien führen.
4. **Doppelte „final/polish/legacy"-Layer** entfernen (Grow-Wizard legacy↔final; v1-polish↔rc2).
5. **Radius/Spacing/Breakpoints standardisieren** — Magic-Numbers (`720px`/`860px` in vielen Dateien) als Tokens.
6. **4 Alt-Seiten auf `V1Page` migrieren** (Harvest/MeasurementEdit/Analysis/Archive).

## 8. Empfohlene Redesign-Reihenfolge
1. **Token-/Theme-Foundation** vereinheitlichen (ein Set, Light+Dark, `data-theme`-Toggle).
2. **Primitive (`v1.tsx`) auf neue Tokens** heben — propagiert automatisch in fast alle Seiten.
3. **Flaggschiff-Seiten** neu gestalten: Live-Dashboard → GrowDetail → Addback.
4. **rc2-overrides** schrittweise abbauen, je Seite die Patches in saubere Regeln überführen.
5. Mittel-/Kleinseiten nachziehen, Alt-Seiten migrieren.
