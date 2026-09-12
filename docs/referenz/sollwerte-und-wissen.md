# Sollwerte, Wissensbasis und Einkaufsliste

> Woher jeder Zielwert kommt, wie eigene Werte darüberliegen, und wie das
> mitgelieferte Fachwissen zu einer Bestandsinstallation findet.

## Wo in der App

| Was | Wo |
|---|---|
| Sollwert-Profile | Betrieb → Sollwert-Profile, `/sollwerte` |
| Wochenplan (Fork AI) | Betrieb → Wochenplan, `/wochenplan` |
| SOPs & Bibliothek | Wissen → SOPs & Bibliothek, `/wissen` |
| Einkaufsliste | Wissen → Einkaufsliste, `/einkaufsliste` |
| Profil als Vorgabe wählen | Hydro-System bearbeiten, `/hydro/new` bzw. `/hydro/:id/edit` |
| Profil am Grow abweichen lassen | `/grows/new` bzw. `/grows/:growId/setup` |
| Profilname auf der Live-Kachel | `/` — nur wenn ein **eigenes** Profil gilt |
| Einkaufsliste (zugeklappt) | am Fuß von `/wissen`, dieselbe Komponente |
| Symptombilder | in der Symptom-Detailansicht auf `/wissen` |
| Laufende SOP-Instanzen | `/sops` (Grow-Abschnitt), nicht die Bibliothek |

## Was es tut

**Die Kette.** `SetpointProfileResolver.Resolve(growProfilId, systemProfilId,
anbaustil)` entscheidet in dieser Reihenfolge: am Grow gewählt → vom
Hydro-System geerbt → aus dem Anbaustil. `ProfileOrigin`
(`Grow`/`System`/`Style`) trägt mit, *warum* es gilt. Darüber steht weiter der
Grenzwert vom Zelt — den legt `UserTargets.Overlay` zuletzt drauf.

**Eigene Profile speichern nur Abweichungen.** Ein `SetpointProfile` hält
`Overrides` als Phase → Feld → Zahl (Kennung `custom:<Id>`).
`TargetValueService.GetTargets` holt die Basis bei jedem Abruf frisch aus der
Wissensbasis und legt mit `SetpointProfileResolver.Apply` nur die angefassten
Felder darüber. Wer den pH in der Blüte anpasst, bekommt jede spätere
Verbesserung an EC, VPD und allem anderen weiter mit.

**Knowledge-Sync.** Die JSON-Dateien liegen unter
`GrowDiary.Web/wwwroot/knowledge-defaults/` und werden beim Start nach
`App_Data/knowledge/` kopiert (`AppPaths.KnowledgeDataPath`). Danach vergleicht
`KnowledgeBaseLoader.EnsureKnowledgeDirectory` bei jedem Start gegen das
Manifest `.shipped-defaults.json` — gehasht wird der *Inhalt* (JSON kanonisch
neu serialisiert, Einrückung zählt nicht). Noch unsere alte Fassung wird
aktualisiert, vom Nutzer Geändertes bleibt liegen.

**Einkaufsliste.** `EinkaufslisteService.Bauen` zieht die `requiredMaterials`
aller Abläufe zusammen: zerlegt an Kommas *außerhalb von Klammern* (nicht am
Dezimalkomma), zusammengefasst über einen Schlüssel ohne Klammerzusatz,
gruppiert, nach Häufigkeit sortiert. Jeder Posten nennt die Abläufe, die ihn
verlangen. Kein eigener Datenbestand — die Liste entsteht bei jedem Abruf neu.

## Die Zahlen und woher sie kommen

| Zahl | Wert | Woher |
|---|---|---|
| Rückfall-Profil | `rdwc-default` | `TargetValueService.FallbackProfileId` |
| Mitgelieferte Profile | 2 (`rdwc-default`, `dwc-default`) | `wwwroot/knowledge-defaults/setpoints/` |
| Felder je Phase | 14 | `SetpointProfile.Fields` |
| Phasen mit Sollwerten | 6 von 8 | `SetpointProfilesApiController.Stages`; `GrowStage` kennt zusätzlich `Dry` und `Cure` |
| DWC-EC-Aufschlag | 1,3 | `TargetValueService.DwcEcMultiplier`; fachlich `guidance/dwc-needs-higher-ec.json` (SKX-Growplan Rev.01, Punkt 1: 30–70 %, mit +30 % starten) |
| pH Veg / Blüte (RDWC) | 6,0–6,1 / 5,9–6,0 | `setpoints/rdwc-default.json`; der Profilname nennt die Quelle: Athena Blended / SKX-Growplan Rev.01 |
| EC Veg / Blüte (RDWC) | 0,6–0,8 / 1,0–1,2 | dieselbe Datei |
| ORP Blüte / Finish | 400–450 / 450–500 | dieselbe Datei |
| Wassertemp. Blüte Tag/Nacht | 20 / 18 °C | dieselbe Datei |
| EC Sämling (DWC) | 0,26–0,52 | `setpoints/dwc-default.json` — RDWC × 1,3, schon eingerechnet |
| Abläufe (SOPs) | 11 | `knowledge-defaults/sops/` |
| Materialangaben roh | 65 | Summe der `requiredMaterials` über die 11 Dateien; dieselbe Zahl in der XML-Doku von `EinkaufslisteService` |
| Gruppen der Einkaufsliste | 4 | `EinkaufslisteService.Reihenfolge`: Messen · Chemie & Dünger · Verbrauch · Werkzeug & Behälter |
| Wissensdateien | 39 Regeln · 30 Behandlungen · 20 Symptome · 12 Verschleiß · 11 Abläufe · 8 Erreger · 3 Programme · 2 Profile | Verzeichnisse unter `knowledge-defaults/` |

### Der Wochenplan (Fork AI)

Ein Sollwert-Profil gilt je **Phase** — sechs Stufen für einen ganzen Durchgang.
Das Düngeprogramm gliedert dagegen nach **Woche**, und seit forkai.46 trägt jede
Wochenspalte nicht nur EC und pH, sondern auch Wasser Tag/Nacht, VPD, CO₂, PPFD,
RH-Obergrenze und Lufttemperatur. Beim Auflösen legt `MitFeedchart` die Woche über
das Profil; was die Spalte nicht nennt, bleibt beim Profilwert.

Die Woche selbst wird nicht nach Kalender gezählt, sondern nach Ankern:
Blütewochen ab dem Flip, Vegi-Wochen ab dem Vegi-Start. Läuft eine Phase über die
letzte Spalte hinaus — der Normalfall, wenn die Vegi gestreckt wird —, bleibt der
Plan auf dieser Spalte stehen und sagt es (`gehalten seit Woche N`).

Die Seite `/wochenplan` zeigt das am Stück: die laufende Woche mit allen Werten,
die Anker darüber und den ganzen Verlauf darunter.

## Was es bewusst NICHT tut

- **Kein eigenes Profil ist eine Vollkopie.** Sie hätte den Nutzer beim ersten
  Speichern von allen künftigen Verbesserungen abgeschnitten, unbemerkt
  (`SetpointProfile`, `<remarks>`).
- **NFT, Aeroponik und „Sonstiges" bekommen RDWC.** Annahme, keine Messung —
  deshalb an genau einer Stelle: `TargetValueService.ProfileIdFor`.
- **Die Zielwerte rechnen den DWC-Aufschlag nicht an.** `dwc-default` hat ihn je
  Phase schon drin; beides zusammen ergäbe das 1,69-fache.
- **`Dry` und `Cure` haben keine Sollwerte.** `GetTargets` gibt `null` zurück,
  statt etwas zu erfinden.
- **Beim Ansehen eines mitgelieferten Profils gibt es keine Eingabefelder** — ein
  Feld, das nichts speichert, ist schlimmer als keins.
- **Der Profilname steht nur bei Abweichung auf der Kachel**
  (`GrowDashboardComposer.ApplyProfileNote`).
- **Die Einkaufsliste macht keine Warenkunde.** Gruppen entstehen aus dem Text
  des Postens; die Füllwörter-Liste ist bewusst kurz — lieber eine Zeile zu viel
  als zwei verschiedene Dinge zusammengeworfen.
- **Der Knowledge-Sync löscht nichts.** Eigene Dateien bleiben, geänderte werden
  nicht überschrieben; nicht als unverändert nachweisbare landen vorher als
  `*.user-backup` daneben.
- **Keine KI.** Die Bibliothek ist zum Nachschlagen da; `/berater` packt das
  Wissen zum Mitnehmen nach außen — Export, keine KI in der App.

## Im Code

| Aufgabe | Datei |
|---|---|
| Kette Grow → System → Anbaustil, `Apply` der Abweichungen | `GrowDiary.Web/Services/SetpointProfileResolver.cs` |
| Profile laden, Basis + Abweichung zusammenrechnen | `GrowDiary.Web/Services/TargetValueService.cs` |
| Modell eines eigenen Profils, `custom:`-Kennung, Feldliste | `GrowDiary.Web/Models/SetpointProfile.cs` |
| Speichern (Tabelle `SetpointProfiles`) | `GrowDiary.Web/Infrastructure/SetpointProfileRepository.cs` |
| `GET/POST/PUT/DELETE /api/setpoint-profiles` | `GrowDiary.Web/Api/Controllers/SetpointProfilesApiController.cs` |
| Wissensdateien laden, Sync gegen `.shipped-defaults.json` | `GrowDiary.Web/Services/Knowledge/KnowledgeBaseLoader.cs` |
| `GET /api/knowledge/…` (SOPs, Symptome, Setpoints, …) | `GrowDiary.Web/Api/Controllers/KnowledgeApiController.cs` |
| Einkaufsliste zusammenführen, gruppieren, sortieren | `GrowDiary.Web/Services/EinkaufslisteService.cs` |
| Zelt-Grenzwert über das Profil legen | `GrowDiary.Web/Services/UserTargets.cs` |
| Seite `/sollwerte` | `GrowDiary.React/src/pages/SetpointProfilesPage.tsx` |
| `GET /api/wochenplan`, Seite `/wochenplan` | `GrowDiary.Web/Api/Controllers/WochenplanApiController.cs`, `GrowDiary.React/src/pages/WochenplanPage.tsx` |
| Klima je Woche über das Phasenprofil legen | `GrowDiary.Web/Services/MischplanService.cs` (`MitFeedchart`) |
| Profil-Auswahl an Grow und Hydro-System | `GrowDiary.React/src/features/setpoints/ProfileSelect.tsx` |
| Seiten `/wissen` und `/einkaufsliste` | `GrowDiary.React/src/pages/KnowledgePage.tsx`, `…/ShoppingListPage.tsx` |

## Fallen

- **Die Abkürzung, die das Profil umging.** `GetTargets(grow.HydroStyle, stage)`
  landet immer beim Standardprofil. Die Diagnose zeigte EC 0,6–0,8, die Kachel
  0,9–1,1 — derselbe Grow, dieselbe Minute; beim Aufräumen fanden sich zwei
  weitere Stellen. Hält jetzt
  `GrowDiary.Web.Tests/SollwertKetteVollstaendigTests.cs`.
- **Wissens-Updates erreichten Bestandsinstallationen nie** (bis 1.6.1): einmal
  beim ersten Start kopiert, danach nie wieder angefasst.
- **In die Laufzeitkopie geschrieben statt in die Vorlage** (beta.29): eine
  Änderung landete in `App_Data/knowledge/` statt in
  `wwwroot/knowledge-defaults/`. Das Changelog versprach sie, ausgeliefert wurde
  sie nie. **Änderungen am Wissen gehören immer in `wwwroot/knowledge-defaults/`.**
- **Mitgelieferte Profile ließen sich nicht ansehen** (beta.14): es gab nur
  „Kopieren" — man musste etwas verändern, um etwas nachlesen zu können.
- **Die Einkaufsliste war unauffindbar** (bis beta.42): seit beta.36 vorhanden,
  aber zugeklappt am Fuß der Wissensseite, ohne Menüpunkt und ohne Suchwörter.
  „Einkaufsliste" im Suchfeld ergab „Nichts gefunden".
- **Wörterbuch-Schlüssel werden in JSON kleingeschrieben.** Aus `Veg` wurde
  `veg`, und die Oberfläche suchte einen Schlüssel, den es nicht gab — deshalb
  ist `SetpointProfileDto.Stages` eine Liste, kein Wörterbuch.
