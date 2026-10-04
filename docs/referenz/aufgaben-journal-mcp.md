# Aufgaben, Journal, Fotos und der MCP-Server

> Was heute zu tun ist, was gestern war — und die zwei Wege, das einer eigenen
> KI vorzulegen, ohne KI in die App zu bauen.

## Wo in der App

| Was | Wo |
|---|---|
| Menü „Jetzt" → „Aufgaben"; Badge `count`, Handy-Leiste, Ziel der meisten Push-Nachrichten | `/aufgaben` (alt: `/action`) → `MobileActionPage` |
| Menü „Pflanzen" → „Journal & Fotos"; derselbe Strom im Grow | `/journal` → `GrowScopedSectionPage`; `/grows/<id>`, Abschnitt `journal` |
| Begleitungsstufe (Voll · Wichtiges · Experte) | `/settings` (alt: `/einstellungen`) → `PUT /api/companion/settings` |
| Eigene Fotos zu einem Symptom | `/wissen` → `SymptomPhotos.tsx` |
| Menü „Einrichtung" → „KI-Assistent", Reiter „Mappe" (bis forkai.164 unter „Wissen") | `/ki?tab=mappe` (alt: `/berater`) → `MappeReiter`, siehe `ki-assistent.md` |
| Grow MCP: Einrichtungsseite mit Schlüssel und fertigem Befehl | eigenes Add-on, HA-Seitenleiste (Ingress 5078) |
| Grow MCP: die Schnittstelle selbst | `http://<heimnetz-adresse>:5080/mcp` (5079 belegt der Grow MCP des Originals) |

## Was es tut

**Aufgaben** ordnet nach Dringlichkeit, nicht nach Herkunft — in dieser
Reihenfolge: Pumpen-Befunde (`/api/pump-watch`, ganz oben), überfällige Routinen
(`/api/grows/{id}/due-sops`), offene Risiken, Termine (offene `GrowTask` + aktive
`SopInstance`), Aushärte-Gläser (`/api/curing/jars`, ohne Grow-Filter), Wartung
(`/api/maintenance-due`). Eine Hauptaktion je Zeile; Termine lassen sich abhaken
(`PATCH /api/tasks/{id}/status`) oder löschen. Aufgaben entstehen beim Starten
einer SOP oder eines Risiko-Ablaufs — je Schritt mit Fälligkeit eine
(`CreateReminderTasksForSteps`) — oder von Hand im Journal.

Und aus **geplanten Geräte-Vorgängen**: eine Kalibrierung oder Wartung mit
Status `Planned`, Fälligkeit und einem Gerät am Grow legt eine Erinnerung an
(`GrowTaskId`). Sie geht wieder mit, sobald der Vorgang gelöscht wird — auch
beim Löschen des ganzen Geräts, und dann für alle seine Vorgänge auf einmal.
**Abgehakte** Aufgaben bleiben stehen: was erledigt ist, gehört in die Historie.
(Bis beta.63 blieb jede dieser Erinnerungen zurück und war über die Oberfläche
nicht mehr erreichbar.)

**Fällige Routinen** liest `SopDueService` aus den `Schedule`-Triggern der
Wissens-Abläufe. „Zuletzt gemacht" kommt aus dem, was ohnehin anfällt:
Wasserwechsel aus der Lösungswechsel-Markierung einer Messung, Tagesroutine aus
der letzten Messung, sonst aus der letzten abgeschlossenen Instanz. Die
**Begleitungsstufe** dämpft global: `expert` liefert leer, `important` nur
Kritisches — auch in `WartungDueService`, nicht beim Pumpen-Wächter.

**Die Chronik eines Grows** (`GET /api/grows/{id}/chronik`) sammelt, was wann
geändert wurde: Grow angelegt, Messung gespeichert, Journal-Eintrag, Flip. Vier
Controller schreiben hinein. Bis beta.64 war sie **schreib-only** — die App
sammelte diese Zeilen seit Monaten, und niemand kam heran. Es gibt bewusst
keinen Knopf dafür: man liest eine Chronik nicht täglich, sondern wenn etwas
passiert ist.

**Journal & Fotos** ist ein Zeitstrom aus Einträgen, Messfotos und Ereignissen
(`buildJournalStream`), mit Filter „Nur Fotos". Fotos hängen an einer Messung;
ein Eintrag zu derselben Messung zieht sie an sich. Bei
`AutoMeasurementConfig.CaptureSnapshot` legt der Auto-Messlauf zusätzlich ein
Kamerabild ab (`SaveSnapshotAsync`: „Auto-Snapshot", Tag `Overview`, ohne
Messung).

**Einträge lassen sich bearbeiten und entfernen** (seit 05.10.2026 bzw.
25.08.2026). „Bearbeiten" öffnet in der Zeile dasselbe Formular wie
„+ Eintrag", vorbefüllt; „Änderungen speichern"/„Abbrechen", Escape bricht ab,
der Fokus kehrt auf den Knopf zurück. `PUT /api/journal/{id}` ändert Titel,
Text, Art und Zeitpunkt; **ein fehlendes Feld (`null`) bleibt, wie es ist**,
ein leerer Text leert es. Herkunft, Messungsbezug und Fotos bleiben unberührt,
`updatedAtUtc` hält fest, wann zuletzt geändert wurde, die Chronik bekommt
„Journal geändert". Meilensteine (Keimung, Flip …) bietet „+ Eintrag" nicht an;
beim Bearbeiten erscheint die eigene Art trotzdem in der Auswahl, damit
Speichern sie nicht still zur Beobachtung macht. Ihr Zeitpunkt ist dort gesperrt:
die Phase rechnet mit dem Datum am Grow (Flip, Finish), ein verschobenes
Journaldatum wäre eine zweite Wahrheit. Eine unbekannte Art (etwa `99`) lehnen
Anlegen und Ändern mit 400 ab. Anlass: ein Wasserwechsel mit
„pH- Menge nicht notiert" ließ sich nicht korrigieren.

**Grow MCP** gibt einem MCP-Klienten im Heimnetz 22 **lesende** Werkzeuge auf
Grow OS, darunter `foto_ansehen` (Bild plus Zusammenhangs-Satz); sein Zweck
gegenüber der Mappe sind Verlaufsfragen. **Die Mappe** (`/ki?tab=mappe`, alt `/berater`) ist ein ZIP
aus neun Markdown-Dateien — Anweisung, Lagebericht, fünf Wissensdateien,
Prüffragen — und hält den Stand von jetzt fest.

## Die Zahlen und woher sie kommen

| Zahl | Wert | Woher |
|---|---|---|
| Vorschau Wartung/Kalibrierung; sichtbare Termine | 3 Tage; 8 Zeilen | `MobileActionPage.tsx` (`dueBeforeUtc`, `termine.slice(0, 8)`) |
| Warnung / kritisch ohne eigene Angabe | Intervall + 1 / + 3 Tage | `SopDueService.FuerGrow` |
| Wasserwechsel | alle 7 Tage, Warnung ab 8, kritisch ab 10 | `knowledge-defaults/sops/weekly-water-change.json` |
| Tagesroutine / Tiefenreinigung | alle 1 bzw. 90 Tage | `daily-measurement-routine.json`, `system-cleaning-deep.json` |
| Sicherung gilt als alt | 30 Tage (dort als Faustregel bezeichnet) | `WartungDueService.SicherungAlterTage` |
| Vorwarnung vor Lebensdauer-Ende | 90 % | `WartungDueService.VorwarnAnteil` |
| Foto-Upload | 10 MB; `.jpg .jpeg .png .webp` | `PhotoStorageService` |
| Motiv-Tags / Eintragsarten | 9 Tags; 9 Arten im Formular (beim Bearbeiten + der eigene Meilenstein), 14 im Enum | `Models/Enums.cs`; `journal-bearbeiten.ts` |
| MCP: Ports | 5078 Ingress, 5079 Heimnetz | `GrowMcp/Tueren.cs` |
| MCP: Werkzeuge, alle lesend; Bild höchstens | 22; 6 MB | `GrowMcp/Tools/GrowTools.cs` |
| MCP: `messwert_verlauf` | 14 Tage voreingestellt (1–365); bis 2 Tage Einzelwerte, darüber Tageswerte | `GrowTools` |
| MCP: `dosierungen` | 30 Einträge, höchstens 200 | `GrowTools` |
| MCP: wo Grow OS liegt | Port 5076, Slug `grow_os`, mindestens 2.0.0-beta.24 | `GrowOsAccess/GrowOsLocator.cs` |
| Mappe | 9 Dateien, 4 Prüffragen | `AgentPackageBuilder.Build`, `AgentPruefung.Alle` |

## Was es bewusst NICHT tut

- **Ändern verschiebt nichts still.** `PUT /api/journal/{id}` lässt fehlende
  Felder stehen — wer nur die vergessene Menge nachträgt, schiebt den Eintrag
  nicht auf „jetzt" und macht keinen Wasserwechsel zur Notiz.
- **Der Experte bekommt keine ungefragten Erinnerungen** („hat sich die Stille
  ausdrücklich bestellt", `SopDueService`). Der Pumpen-Wächter ignoriert die
  Stufe: Gefahr, keine Erinnerung.
- **Aushärte-Gläser werden nicht nach Grow-Status gefiltert** — nach der Ernte
  gilt ein Grow als beendet, das Aushärten läuft weiter.
- **Keine fremden Symptombilder** (Urheberrecht) — nur eine Zuordnung eigener
  Aufnahmen, kein zweiter Bildbestand (`SymptomPhotosApiController`).
- **Kein MCP-Werkzeug schreibt.** Dosieren und Schalten bleiben in Grow OS.
- **Die Einrichtungsseite ist über Port 5079 nicht erreichbar** (`Tueren.Pruefen`
  → 404), sonst holte sich jeder im WLAN den Schlüssel.
- **Kein `hassio_role: manager`**, obwohl der Supervisor die Netzwerkadresse nur
  dann herausgibt: die Rolle dürfte jedes andere Add-on deinstallieren.
- **Grow OS verschickt nichts von selbst**; die Mappe verlässt den Rechner nur
  per „Herunterladen". Anweisung und Prüffragen stehen offen auf `/ki?tab=mappe`,
  nicht nur im ZIP — „ungelesene Grenzen sind keine".

## Im Code

| Aufgabe | Datei |
|---|---|
| Aufgabenseite, Reihenfolge, Abhaken/Löschen | `GrowDiary.React/src/pages/MobileActionPage.tsx` |
| Fällige Routinen + Begleitungsstufe (`FuerGrow`, `ZuletztGemacht`, `Stufe`) | `GrowDiary.Web/Services/SopDueService.cs` |
| Wartungs-, Prüf- und Sicherungstermine (`Offen`, `LetzteSicherung`) | `GrowDiary.Web/Services/WartungDueService.cs` |
| Stufe lesen/setzen, `due-sops`; Aufgaben; Journal (anlegen, ändern, entfernen) | `Api/Controllers/CompanionApiController.cs`, `TasksApiController.cs`, `JournalApiController.cs` |
| Journalstrom + Formulare; Bearbeiten | `src/features/grow-detail/journal-stream.ts`, `JournalStreamSection.tsx`, `journal-bearbeiten.ts` |
| Fotos ablegen, Schnappschuss, Prüfung; Bilder je Symptom | `Services/PhotoStorageService.cs`, `Api/Controllers/SymptomPhotosApiController.cs` |
| MCP-Werkzeuge (alle 22) | `GrowMcp/Tools/GrowTools.cs` |
| Welcher Port was darf; Schlüssel; Heimnetz-Prüfung | `GrowMcp/Tueren.cs`, `Program.cs`, `Services/TokenSpeicher.cs`, `Services/Heimnetzadresse.cs` |
| Grow OS finden | `GrowOsAccess/GrowOsLocator.cs` |
| Mappe bauen, Anweisung, Prüffragen | `GrowDiary.Web/Services/AgentPackageBuilder.cs`, `AgentPromptTexts.cs`, `AgentPruefung.cs` |

## Fallen

- **Aufgaben ließen sich anlegen, aber nie abhaken** — der Erledigt-Knopf saß auf
  einem Panel, das beim Umbau des Journals wegfiel (`taskErledigen`, beta.2).
  **Löschen** war samt Audit-Log fertig gebaut, nur bot es keine Oberfläche an
  (`taskLoeschen`, beta.38).
- **Die Liste zeigte Enum-Namen** — „Ec: Abweichung prüfen", gebaut aus
  `deviation.Metric` (beta.47). Und **„27 offen" stand über acht sichtbaren
  Zeilen**, die übrigen neunzehn waren weder zu sehen noch zu erreichen; der
  Zähler nennt jetzt beide Zahlen (Kommentar am Termine-Zähler in
  `MobileActionPage.tsx`, aus dem Desktop-Audit, ausgeliefert in beta.50).
- **Ein gelöschter Grow behielt seine Risiken** — `RiskEvents` hat keinen
  Fremdschlüssel auf `Grows`, `PRAGMA foreign_keys` ist aus; sie standen für
  immer auf der Aufgabenseite (beta.48).
- **`foto_ansehen` gab nie ein Bild zurück.** Der Pfad war doppelt
  (`uploads/uploads/4/x.jpg`) — und es fiel nicht auf, weil der SPA-Fallback
  jeden Pfad außerhalb `/api` mit `index.html` und **Status 200** beantwortet
  (Kommentar an `GrowTools.BildPfad`, grow-mcp 0.1.5).
- **Der MCP-Server fand Grow OS nicht**, weil der eigene Slug fest im Code stand
  und auf ein anderes Add-on zeigte (`GrowOsLocator.Kandidaten`, 0.1.1).
- **Die Einrichtungsseite schlug eine Adresse vor, unter der nie etwas lief:**
  sie hängt am Ingress und ist unter jeder HA-Adresse offen, auch einer aus dem
  Internet — die MCP-Tür aber nur im Heimnetz (`Heimnetzadresse.IstLokal`, 0.1.8).
- **`wissen_liste` gab ganze Einträge statt Kopfdaten** — rund 15.000 Tokens
  (0.1.2). Und **drei Werkzeuge beantworteten still eine andere Frage als die
  gestellte** (`alarme`, `technik`, `grows_auflisten`, 0.1.6).
