# Code-Analyse — Grow Operation System

Tiefenanalyse des Codestands, als Grundlage für spätere Diskussionen.
Ergänzt `architecture.md` (Überblick) um die Frage: **was tut der Code konkret, und wo
widerspricht er sich?**

Stand: 2026-07-25, Commit `6b4b34b`.

## Methode — was gelesen wurde und was nicht

Der Bestand sind **~76.500 Zeilen in 673 Dateien**. Eine wörtliche Zeile-für-Zeile-Lektüre
wäre eine Behauptung, keine Arbeit. Tatsächlich gemacht:

| Durchgang | Umfang | Methode |
|---|---|---|
| 1 — Inventur | **alle 673 Dateien** | mechanisch: Typen, Member, Routen, Doc-Kommentare extrahiert |
| 2 — Verhalten | Services, Repositories, Controller, React-Features | gelesen |
| 3 — Querschnitt | gesamter Bestand | gezielte Analysen (Dubletten, tote Pfade, Schwellenwerte, Smells) |

Nicht vollständig gelesen: Tests (13.700 Zeilen), DTO-/Mapping-Dateien, CSS. Diese sind
inventarisiert und wurden gezielt geprüft, aber nicht Zeile für Zeile.

## Größenverhältnisse

```
C#           45.134 Zeilen   366 Dateien
TypeScript   14.287 Zeilen    79 Dateien
CSS           8.042 Zeilen
```

Nach Bereich (C#, ohne bin/obj):

```
11.346  Api/            113 Dateien   Controller + DTOs + Mapping
 9.293  Infrastructure/  45 Dateien   Repositories, Schema, Pfade
 7.498  Services/        64 Dateien   Domänenlogik
 1.842  Models/          49 Dateien   Entities, Enums
   621  Controllers/      7 Dateien   Kamera-Proxy, Legacy, Export
13.769  Tests            79 Dateien
```

**186 API-Routen**, davon ruft das Frontend 172 auf.
**34 SQLite-Tabellen.**

Größte Einzeldateien: `RecommendationEngine.cs` (745), `GrowCoreRepository.cs` (744),
`DatabaseInitializer.CoreSchemaSql.cs` (687), `SopRepository.cs` (605).
Im Frontend: `KnowledgePage.tsx` (704), `ManualMeasurementPage.tsx` (658), `AddbackPage.tsx` (631).

---

# Teil 1 — Was der Code tut

## 1.1 Schichten

**Kein ORM.** Datenzugriff ist handgeschriebenes ADO.NET über `RepositoryBase`, das
`OpenConnection()` liefert. Jedes Repository öffnet seine eigene Verbindung pro Aufruf.

**Migrationen sind idempotent statt versioniert.** `DatabaseInitializer` legt Tabellen mit
`CREATE TABLE IF NOT EXISTS` an und fügt Spalten über `EnsureColumn` nach. Es gibt eine
Tabelle `AppliedSchemaMigrations`, aber der Hauptweg ist „prüfen und nachziehen". Praktisch
heißt das: **jede Version kann jede ältere Datenbank öffnen**, ohne Migrationskette.

Die Fork-Tabellen (Kosten, Geräte, Steuerung, Grow-Plan, Wochenwerte) liegen außerhalb
davon; jedes dieser Repositories legt sie beim ersten Zugriff an. Ob das je Datei schon
geschehen ist, hält **ein** Merker: `SchemaWaechter` (forkai.157). Die Wiederherstellung
einer Sicherung lässt danach `DatabaseInitializer.Initialize()` laufen und setzt den Merker
zurück — vorher fehlten nach dem Einspielen einer älteren Sicherung Tabellen und Spalten
bis zum Neustart (die Schema-Version im Kern bleibt über viele Ausgaben gleich und hält
eine solche Sicherung nicht auf). `ForkTabellenTests` zählt, dass jede Datei mit einer
Fork-Tabelle den Wächter benutzt und nicht an ihm vorbei anlegt.

**Offen:** Die Wiederherstellung ist nicht gegen gleichzeitige Zugriffe gesperrt. Öffnet
ein Hintergrund-Worker zwischen dem Wegräumen der alten und dem Kopieren der neuen Datei
eine Verbindung, entsteht dort eine leere Datenbank (nachgestellt: einige von 100
Durchgängen). Die Sicherheitssicherung bleibt erhalten.

**Einstellungen liegen als Key-Value** in `AppSettings` (`notify:*`, `ai:*`,
`trendwatch:seen:*`). Jede Einstellungsgruppe hat ihr eigenes Repository, das die Präfixe
kennt. Seit heute gibt es zusätzlich `AppSettingsRepository` für generisches Key-Value.

**Lebensdauern**: Repositories sind überwiegend `Singleton` (zustandslos, öffnen Verbindungen
selbst), Dienste mit Anfragebezug `Scoped`.

## 1.2 Die vier Hintergrunddienste

| Dienst | Takt | Aufgabe |
|---|---|---|
| `HomeAssistantSnapshotWorker` | 5 min | Sensorwerte von HA holen, in `TentSensorSnapshots` / `TentSensorReadings` / `TentSensorDailyStats` schreiben |
| `AlertWatchWorker` | **1 min** | Grenzwert-Alarme auswerten, Watchdog, seit heute Trend-Wächter |
| `AutoMeasurementWorker` | 5 min | automatische Messungen ausführen (u. a. 30 min nach Licht an/aus) |
| — | Start +15/30 s | alle Dienste warten beim Start, damit die DB initialisiert ist |

Der Minutentakt des `AlertWatchWorker` ist bewusst: Grenzwert-Alarme waren vorher an den
5-Minuten-Takt gekoppelt, wodurch minutengenaue Intervalle unmöglich waren.

## 1.3 Wie eine Messung durch das System läuft

1. **Erfassung** — `ManualMeasurementPage.tsx` oder `AutoMeasurementExecutionService`
2. **Plausibilität** — `MeasurementSanityService` prüft auf offensichtlichen Unsinn
   (Einheitenverwechslung, unmögliche Werte) und erzeugt Hinweiskarten
3. **Speichern** — `MeasurementRepository`
4. **Abweichungen** — `DeviationAnalyzerService.Analyze(grow, measurements)` liefert
   `GrowDeviation`-Objekte mit `StableKey` (z. B. `hydro.ph`)
5. **Risiko-Sync** — `DeviationRiskEventSyncService` überführt Abweichungen in `RiskEvents`
6. **Behandlung** — `TreatmentRecommender` schlägt aus der Wissensbasis passende
   Behandlungen zu, `RiskEventSopRecommender` passende SOPs
7. **Karten** — `RecommendationEngine.BuildCardsFromDiagnostics` macht daraus Anzeigetexte
8. **Anzeige** — `GrowDashboardComposer` setzt das Live-Bild zusammen

## 1.4 Wo die Domänenregeln wohnen

Das ist die wichtigste Karte in diesem Dokument, weil hier die Widersprüche sitzen.

| Regel | Quelle | Ort im Code |
|---|---|---|
| pH-Sollband je Phase | Wissensbasis | `setpoints/rdwc-default.json` → `TargetValueService` |
| pH-Handlungsband 5,8–6,2 | Growplan Pkt. 6 | `DeviationAnalyzerService.PhComfort*` (Konstanten) |
| pH kritisch 5,5 / 6,5 | Growplan Pkt. 6 | dieselben Konstanten |
| EC-Ziel je Phase | Wissensbasis | `setpoints/rdwc-default.json` |
| **EC-Ziel je Phase (zweite Quelle)** | hart kodiert | `RecommendationEngine.GetAthenaRdwcTarget` |
| DWC-EC-Faktor | Growplan Pkt. 1 | `TargetValueService.DwcEcMultiplier = 1.3` |
| **DWC-EC-Faktor (zweiter Wert)** | hart kodiert | `RecommendationEngine`, Faktor `1.35` |
| ORP-Arbeitsbereich 300–500 | Messprotokoll | `DeviationAnalyzerService.CheckOrp` (fest) |
| ORP-Sollwert je Phase | Wissensbasis | `setpoints/rdwc-default.json` |
| PPFD ohne CO₂ ≤ 900 | Growplan Pkt. 11 | `DeviationAnalyzerService.PpfdCeilingWithoutCo2` |
| VPD aus Blatttemperatur | Ben-Green-Rechner | `VpdCalculator` + `Tent.LeafTempOffsetC` |
| Drift über Tage | neu | `TrendWatchService` |
| Growplan-Regeln als Text | Growplan | `knowledge-defaults/guidance/*.json` (10 Einträge) |

**Muster:** Zahlen kommen aus der Wissensbasis, Verhaltensregeln stehen als Konstanten im
Code — außer bei `RecommendationEngine`, das eine komplett eigene, ältere Welt mitbringt.

## 1.5 Frontend

- **React 19 + Vite**, keine Zustandsbibliothek. Daten kommen über `apiFetch` in
  `useEffect`-Hooks, Zustand lebt lokal in den Seiten.
- **`resolveUrl`** (in `base.ts`) setzt den Ingress-Basispfad davor. Wichtig: Der Backend
  injiziert `<base href="/">` in `index.html`; `npm run dev` tut das **nicht**, weshalb
  Deep-Links im reinen Vite-Dev-Server brechen. Für visuelle Prüfungen deshalb immer
  Backend + `e2e/preview-server.mjs` verwenden.
- **Zwei Komponentenwelten nebeneinander**: das ältere `.card`-Chrome und das neuere
  `V1*`-System (`components/v1.tsx`), dazu das `ix`-Cockpit-Chrome nur für das
  Live-Dashboard (`features/live/live-instrument.css`).
- **Navigation**: `navGroups` in `App.tsx` ist die einzige Quelle für Seitenleiste und
  mobiles „Mehr"-Panel. Klappzustand in `localStorage` unter `growos.navGroups.v2`.
- **Suche**: `AppSearch` matcht Seiten lokal (mit Synonymliste `SEARCH_KEYWORDS`) und holt
  Grows/Zelte/Sorten/SOPs/Wissen von `/api/search`.

## 1.6 Wissensbasis und ihr Abgleich

`wwwroot/knowledge-defaults/` wird beim Start nach `App_Data/knowledge/` gespiegelt.
`KnowledgeBaseLoader.EnsureKnowledgeDirectory()` vergleicht dabei gegen ein Manifest
(`.shipped-defaults.json`):

- Datei fehlt → anlegen
- Inhalt entspricht dem zuletzt Ausgelieferten → aktualisieren
- Inhalt weicht ab und Manifest kennt ihn → **unangetastet lassen** (Nutzeränderung)
- kein Manifest-Eintrag → vorher `*.user-backup` danebenlegen, dann aktualisieren

Der Vergleich ist **inhaltsbasiert** (JSON kanonisch neu serialisiert), Formatierung und
Zeilenenden lösen nichts aus.

Kategorien: `setpoints`, `sops`, `treatments`, `pathogens`, `symptoms`, `nutrient-programs`,
`wear`, `guidance` (neu).

---

# Teil 2 — Befunde

Sortiert nach Auswirkung auf den Nutzer.

## B1 — Zwei widersprüchliche EC-Zielquellen ⚠️ inhaltlich falsch

`RecommendationEngine.GetAthenaRdwcTarget` hat **eigene, hart kodierte EC-Ziele**, die nicht
aus der Wissensbasis kommen und ihr widersprechen:

| Phase | Wissensbasis (`TargetValueService`) | `RecommendationEngine` |
|---|---|---|
| Veg (spät) | 0,6–0,8 | 0,8–1,2 |
| Transition | 0,8–1,0 | 1,2–1,4 |
| Flower | 1,0–1,2 | 1,4–1,6 |
| **Finish** | **1,1–1,6** | **0,4–0,6** |
| DWC-Faktor | **1,3** | **1,35** |

Der Nutzer bekommt je nach Bildschirm **unterschiedliche Zielwerte für denselben Grow**.

**Bei Finish ist der Widerspruch gegenläufig** und trifft eine echte Situation: Der Growplan
endet auf EC 0,4 (Flush). `DeviationAnalyzerService.CheckEc` nutzt die Wissensbasis, also
1,1–1,6. Das ist derselbe Fehlertyp wie die pH-Mahnung, die heute korrigiert wurde:
**die App mahnt das an, was der Plan vorschreibt.**

Am laufenden System nachgestellt — Grow in Finish, EC 0,4 erfasst:

```
GET /api/grows/1/deviations
  Warning | Reservoir-EC ist um -1,60 mS/cm gefallen. | Ziel 1.1 - 1.6
```

Auffällig ist auch, dass **Finish (1,1–1,6) höher liegt als Flower (1,0–1,2)** — der Wert
beschreibt sichtlich die späte Blüte, nicht das Ausklingen.

*Nicht selbst entschieden, weil es eine fachliche Festlegung ist: welche Zahlen gelten für
Finish?* Meine Einschätzung: Die Wissensbasis beschreibt bei „Finish" eher die späte Blüte,
der Flush braucht eine eigene Phase oder ein eigenes Band.

## B2 — pH-Schwellen an drei Stellen 🔁 Dublette

`5.8 / 6.2 / 5.5 / 6.5` stehen in:
1. `DeviationAnalyzerService` (Konstanten, seit heute `public`)
2. `TrendWatchService` (nutzt seit heute die Konstanten aus 1)
3. **`RecommendationEngine.EvaluateHydro` Zeile 151/157 — eigene Literale**

Die Werte stimmen aktuell überein, die Texte sind sogar growplan-konform („Beobachte den
Trend statt hektisch nachzuregeln"). Aber es ist eine Kopie, die beim nächsten Fix übersehen
wird — genau so ist der pH-Fehler heute im Trend-Wächter wieder aufgetaucht.

## B3 — Toter Code: `TemplateRepository` 🗑️

`TemplateRepository` (56 Zeilen) ist in `Program.cs` registriert, wird aber **nirgends
injiziert**. Die zugehörige Tabelle `GrowTemplates` und das Model `GrowTemplate` haben
weder API noch Frontend. Vollständig unerreichbar.

## B4 — Gebaut, aber nicht verdrahtet 🔌

Backend-Routen ohne Frontend-Aufruf (ohne die legitimen Fälle wie `/api/error`):

| Route | Was fehlt |
|---|---|
| `GET /api/light-transitions` | Licht-Schaltverlauf hat keine Anzeige |
| `GET /api/alerts/notify-services` | Auswahl des Push-Dienstes auf der Alarm-Seite |
| `POST /api/alerts/test` | Test-Alarm auslösen |
| `GET /api/auto-measurements/grows/{id}/status` | Status der Auto-Messung je Grow |
| `GET /api/journal/{entryId}` | Einzelner Journaleintrag |
| `POST /api/ai/ask` | bewusst offen (kein Chat gebaut) |

`GET /api/camera/tents/{id}` und `/status` waren Aliase auf dieselbe Aktion wie
`/api/live/tents/{id}/camera`. Beide sind am 02.09.2026 **gelöscht**: die Zählung
`JedeRouteHatEinenAufruferTests` fand für keinen einen Aufrufer, und drei Wege zu einem
Bild sind drei Stellen, an denen der Kamera-Zwischenspeicher auseinanderlaufen kann.

Im Frontend: Route `/messungen` (`GrowScopedSectionPage`) existiert, steht aber in keinem
Menü — Rest des IA-Umbaus, bei dem „Messungen" aus „Verlauf & Daten" entfernt wurde.

## B5 — Kulturabhängige Formatierung war ein Absturzrisiko ✅ heute behoben

`CultureInfo.GetCultureInfo("de-DE")` in einem `static readonly`-Feld wirft ohne ICU eine
`TypeInitializationException` — der ganze Dienst stirbt beim ersten Zugriff, statt nur
einen Punkt statt Komma zu drucken. Heute durch `AppCulture.German` mit Rückfall ersetzt.
Das Laufzeit-Image ist Debian-basiert und hat ICU, war also nie akut.

**Verbleibendes Risiko derselben Familie:** Tests dürfen nicht auf Dezimaltrennzeichen
prüfen. Lokal absichern mit
`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test`.

## B6 — `RecommendationEngine` ist die größte Altlast 🏗️

745 Zeilen, zwei Verantwortlichkeiten:
- `Evaluate(...)` — eigene, hart kodierte Regelwelt (B1, B2)
- `BuildCardsFromDiagnostics(...)` — reine Darstellung der `GrowDeviation`-Objekte

Der zweite Teil ist der neuere Weg und stützt sich auf die Wissensbasis. Der erste Teil ist
eine parallele Meinung. Solange beide leben, gibt es zwei Wahrheiten.

*Refactoring-Kandidat Nr. 1* — aber nicht ohne fachliche Entscheidung zu B1.

## B7 — Kleinere Beobachtungen

- **93 Dateien über 60 Zeilen ohne XML-Doc.** Keine Katastrophe, aber die Services mit
  Domänenlogik sollten erklärt sein, gerade weil die Regeln fachlich sind.
- **Zwei TODOs**, beide „Sprint B": `CultivationKnowledgeService` (MediumPlaybook nach JSON)
  und `GrowDashboardComposer` (LightCycle aus Setup laden).
- **Ein leerer catch** in `GrowExportsApiController.Import.cs:146` — Aufräumen nach
  fehlgeschlagenem Import, vertretbar.
- **Keine Async-Deadlock-Muster**, kein `.Result`/`.Wait()` auf Tasks.
- **Kein toter TypeScript-Code** — jedes Modul außer `main.tsx` wird importiert.
- **Drei Komponentenwelten** (alt `.card`, `V1*`, `ix`-Cockpit). Der IA-Umbau hat die
  Seitenrahmen auf `V1*` gezogen, die Section-Inhalte teilweise nicht.

---

# Teil 3 — Was ich empfehle

**Zuerst fachlich klären (nur du kannst das):**
1. **B1 Finish-EC** — welche Zahlen gelten beim Flush? Danach eine einzige Quelle.

**Dann technisch, in dieser Reihenfolge:**
2. `RecommendationEngine.GetHydroEcTarget` & Co. auf `TargetValueService` umstellen
   → B1 und B2 verschwinden zusammen
3. pH-Literale in `RecommendationEngine` durch die Konstanten ersetzen
4. `TemplateRepository`, `GrowTemplate`, Tabelle `GrowTemplates` entfernen
5. Die vier unverdrahteten Endpunkte entweder anbinden oder löschen — besonders
   `alerts/test` und `notify-services`, die zur Alarm-Seite gehören
6. Route `/messungen` entweder ins Menü oder raus

**Bewusst nicht empfohlen:** ein ORM einführen, die drei Komponentenwelten in einem Zug
vereinheitlichen, oder die Repositories umbauen. Das läuft und trägt.
