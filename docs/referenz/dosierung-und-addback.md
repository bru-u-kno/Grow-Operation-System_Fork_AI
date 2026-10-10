# Dosierung, Addback und Wasserwechsel

> Wie Grow OS Nährlösung korrigiert: Pumpen schalten, Mengen rechnen, und alles
> protokollieren, was ins Becken gegangen ist.

## Wo in der App

| Was | Wo |
|---|---|
| Pumpenliste, kalibrieren, von Hand dosieren, „Was wäre jetzt nötig?" | Betrieb → Dosierung, `/dosierung` |
| Pumpe anlegen bzw. einstellen | `/dosierung/neu`, `/dosierung/:pumpId` |
| Nachfüllen (Addback) als ein Ablauf: vorher → nachfüllen → nachher → speichern, darunter die bisherigen Einträge | Jetzt → Addback, `/addback?growId=…` |
| Alte Adresse des Addback-Assistenten — leitet mit allen Suchparametern auf `/addback?growId=:growId` | `/grows/:growId/addback` |
| Nachfüllen vorbelegt öffnen (Grow-Tagebuch, „Nachfüllen eintragen") | `/addback?growId=…&zeitpunkt=…&ecVorher=…&ecNachher=…&liter=…` — siehe unten |
| Wasserwechsel: Stand, eintragen, nachtragen, Verlauf | Jetzt → **Wasserwechsel**, `/wasserwechsel` |
| Stand des Wasserwechsels (ohne Formular, mit Weg dorthin) | Abschnitt „Wasserwechsel" auf `/addback` |
| Befunde des Pumpen-Wächters | Jetzt → Aufgaben, `/aufgaben` — ganz oben, vor allem anderen |
| Schonfrist des Pumpen-Wächters | `/settings` |
| Düngerkosten aus dem Dosier-Protokoll | Ernte & Archiv, `/archiv` |

## Was es tut

**Pumpen.** Grow OS hat keine Anschlüsse; es schaltet eine Home-Assistant-Entität
(`DosingPump.HaEntityId`, meist ein `switch`). Aus Millilitern wird Laufzeit über
die kalibrierte Fördermenge — ohne sie verweigert der Dienst die Dosis. Kalibriert
wird per Lauf in den Messbecher: abgelesene Menge durch Laufzeit ergibt ml/min
(`DosingCalculator.MlPerMinuteFrom`).

**Drei Stufen.** Von Hand (`POST /api/dosing/pumps/{id}/dose`), Vorschlag
(`GET …/suggestion`, rechnet nur), Automatik je Pumpe (`AutomationEnabled`,
`DosingWorker`). Der Vorschlag läuft durch dieselben Anschläge wie eine echte
Dosis — sonst stünde dort eine Menge, die beim Druck abgelehnt würde.

**Die Menge wird gelernt, nicht gerechnet.** Wie stark eine Lösung gegenhält,
hängt an Wasserhärte und Dünger. `LearnedChangePerMl` zählt die Änderung je
Milliliter aus dem eigenen Protokoll — nur echte Dosen mit Wert davor *und*
danach, geschnitten am letzten Wasserwechsel. Den Wert danach trägt der
`DosingWorker` nach, `DosingFollowUp` bestimmt das Fenster dafür.

**A und B.** Calcium aus A fällt mit Sulfat/Phosphat aus B als Gips aus, wenn
beide konzentriert zusammenkommen. Also: A läuft, Trennzeit, dann B — als
`PendingDose` eingeplant, vom Worker abgeholt.

**Addback.** `AddbackCalculator.Calculate` löst
`Liter = V · (Ziel − Ist) / (Stock − Ziel)`. Liegt der Ist-EC schon auf Ziel, ist
nichts zu tun. Gemischt wird von Hand nach SOP `nutrient-addback`; Grow OS rechnet
und protokolliert.

**Nachfüllen als ein Vorgang (A-006, Etappe 3).** Wie der Wasserwechsel: ein
Speichern legt Addback-Eintrag, Messung vorher und nachher, Verbrauch (Zugaben
und Wasser) und eine Tagebuchzeile (Art „Fütterung") in **einer** Transaktion an
(`POST /api/grows/{id}/addback/vorgaenge`, Tabelle `ForkAddbackVorgaenge`,
Buchungen über `ForkVerbraeuche.AddbackVorgangId`). Löschen am Vorgang
(`DELETE …/addback/vorgaenge/{id}`) oder am Eintrag (`DELETE …/addback/logs/{id}`)
nimmt alles mit. Die Zugaben schlägt der Mischplan auf die **nachgefüllten** Liter
vor (`GET …/mixing-plan/vorschlag`, Wasser-EC und CalMag nach Wasserart — dieselbe
Rechnung wie beim Wechsel). Im Schritt „Nachfüllen" stehen zwei Rechnungen mit
Etikett: „Tank danach" (Mischrechnung nach Volumen: Rest × EC vorher + Liter × EC
der Lösung) und der Addback-Rechner oben — wie viel der eigenen Lösung den Tank
auf das EC-Ziel brächte. Nachfüllen ist kein Lösungswechsel: die
Wechsel-Erinnerung zählt weiter. Einträge des früheren Assistenten (ohne Vorgang)
bleiben lesbar und zählen weiter.

**Vorbelegung per Link.** `/addback?growId=1&zeitpunkt=2026-10-03T14:55:00Z&ecVorher=1.75&ecNachher=1.61&liter=20`
öffnet den Ablauf mit diesen Werten. `zeitpunkt` als ISO mit Zone oder Ortszeit
`yyyy-MM-ddTHH:mm`; Zahlen in Maschinenform (`1.75`) für `liter`, `ecVorher`,
`phVorher`, `wtVorher`, `ecNachher`, `phNachher`, `wtNachher`; dazu `wasser`
(`Tap`/`RO`/`Mixed`), `notiz` und `quelle` (`sensor`, Standard, oder `hand`). Mit
`quelle=sensor` gelten die Werte vorher als Sensorwerte, solange „Wann" nicht
verstellt wird. Gelesen von `vorbelegungAusLink` in
`src/features/vorgang/ablauf-rechnung.ts`.

**Wasserwechsel.** Teil- oder Komplettwechsel mit EC/pH vor und nach und der
benutzten Wasserquelle — und er schneidet das Lernfenster der Pumpen
(`DosingContextBuilder.LastSolutionChangeUtc`).

**Pumpen-Wächter.** `PumpWatchService` beurteilt Luft- und Umwälzpumpe aus zwei
Signalen: Zustand aus Home Assistant und Leistungsaufnahme. Eine Pumpe mit
gerissener Membran meldet „an" und fördert nichts — das sieht nur, wer auf die
Watt schaut.

## Die Zahlen und woher sie kommen

| Zahl | Wert | Woher |
|---|---|---|
| Größte Einzeldosis | 5 ml | `DosingPump.MaxSingleDoseMl` (Vorgabe, je Pumpe änderbar) |
| Mischpause | 18 min | `DosingPump.MinIntervalMinutes` |
| Dosen je Tag | 6 | `DosingPump.MaxDosesPerDay` |
| Menge je Tag | 25 ml | `DosingPump.MaxMlPerDay` |
| Höchstalter des Messwerts (nur Automatik) | 10 min | `DosingPump.MaxReadingAgeMinutes` |
| Harte Laufzeitgrenze je Dosis | 60 s | `DosingGuard.AbsoluteMaxSeconds` |
| Kalibrierlauf höchstens | 300 s | `DosingGuard.MaxCalibrationSeconds`, gespiegelt in `features/dosing/calibration.ts` |
| Kalibrier-Zielmengen | 100 / 50 / 25 ml | `calibration.ts` (`ZIELE`); 1 ml Ablesefehler wiegt bei 100 ml 1 %, bei 23 ml 4 % (CHANGELOG 2.0.0-beta.11) |
| Kalibrierung / Schlauchwechsel fällig | 30 / 40 Tage | `DosingPump.CalibrationIntervalDays`, `TubeIntervalDays` |
| Trennzeit A → B, Verhältnis A : B | 5 min (mind. 1), 1,0 | `DosingPump.PartnerDelayMinutes` / `PartnerRatio`, `PartnerDosing.MinDelayMinutes` |
| Lernen ab | 3 brauchbaren Dosen | `DosingCalculator.LearnedChangePerMl` |
| Anteil der Strecke je Dosis | 0,5 | `DosingCalculator.MlToReach` — nach unten ist ein pH schnell, zurück kaum |
| Untergrenze Volumenfaktor | 0,3 | `DosingCalculator.VolumeFactor` (`Math.Clamp(…, 0.3, 1.0)`) |
| Füllstand gilt als frisch | 2 h | `DosingContextBuilder.VolumeFactorFor` |
| Fenster fürs Nachtragen der Wirkung | 1× bis 2× Mischpause | `DosingFollowUp.WindowFactor = 2.0` |
| Takt der Automatik | 1 min, Start um 45 s versetzt | `DosingWorker.Interval` |
| Schonfrist Pumpen-Wächter | 15 min (1–720 einstellbar) | `PumpWatchService.StandardSchonfristMinuten`, Grenzen in `PumpWatchNotifier.SchonfristMinuten` — **Faustregel** gegen Flattern |
| Leerlauf-Schwelle | 1,0 W | `PumpWatchService.LeerlaufWatt` — **Faustregel**: Messsteckdosen zeigen im Leerlauf typisch unter 1 W |
| Takt des Wächters | 1 min | `AlertWatchWorker.Interval` |
| Addback-EC (Stock) Vorgabe | 3,0 | `GrowWorkflowApiController.AddbackDefaults` |
| Ziel-EC Vorgabe | Mitte des Profilbands | `AddbackDefaults` über `SetpointProfileResolver` + `TargetValueService` |
| Volumen für den **Addback** | Topfzahl × Topfgröße + Tank | `GrowWorkflowApiController.CalculateHydroSetupTotalVolumeLiters` |
| Volumen für den **Volumenfaktor** | nur `GrowSystem.ReservoirLiters` | `DosingContextBuilder.VolumeFactorFor` — nicht dieselbe Zahl wie oben |
| Addback-Behälter | 18,9 L, zu 90 % füllen (klein: 50 %) | `knowledge-defaults/guidance/addback-mixing-procedure.json`; Quelle „RDWC Recirculating Deep Water Culture Procedure (Metric)" |
| Höchstmenge je Komponente | 500 ml je Behälter | `knowledge-defaults/guidance/addback-part-limit.json`; gleiche Quelle |
| pH des Anmischwassers | 6,0 | `knowledge-defaults/sops/nutrient-addback.json`, Schritt `a5` |
| Komplettwechsel | fest 100 % | `ChangeoutsPanel.tsx` (Feld gesperrt) |

## Was es bewusst NICHT tut

- **Keine Dosis aus der Konzentration.** `ConcentrationPercent` wird gespeichert
  und wieder ausgegeben, geht aber in keine Rechnung ein. Ohne Erfahrung sagt der
  Vorschlag „Noch keine Erfahrung" statt zu raten.
- **Nie ganz bis zum Ziel** (halbe Strecke) und **nie nach oben skaliert** — ein
  übervolles Becken macht eine Dosis nur schwächer, das ist die sichere Richtung.
- **Nie gleichzeitig A und B.** Solange eine zweite Hälfte aussteht, dosiert im
  ganzen Zelt niemand — auch die pH-Pumpe nicht.
- **In stehendes Wasser dosiert niemand.** Von Hand blockt nur *bestätigt*
  stehende Umwälzung; die Automatik verlangt *bestätigt laufende*. Unbekannt
  reicht ihr nicht.
- **Automatik nur mit Abschaltung in Home Assistant** (`HasHomeAssistantAutoOff`)
  und nur mit kalibrierter, nicht überfälliger Sonde.
- **Die zweite Hälfte fragt die üblichen Anschläge nicht.** Die Mischpause hat
  gerade A gesehen und würde B genau deshalb ablehnen — B ist keine neue
  Entscheidung, sondern die Vollendung einer getroffenen.
- **Testdosen lernen nicht** (`Simulated`) — sonst stünde später eine Zahl da,
  hinter der nie ein Tropfen war.
- **Der Wächter hängt nicht an der Begleitungsstufe**, und „nichts gemappt"
  heißt „nichts sagen" statt Alarm.
- **Keine KI.** Gelernt wird eine einzige Zahl aus dem eigenen Protokoll.

## Im Code

| Aufgabe | Datei |
|---|---|
| Rechnen (Laufzeit, Fördermenge, Lernen, Volumenfaktor) und alle Anschläge | `GrowDiary.Web/Services/DosingService.cs` (`DosingCalculator`, `DosingGuard`, `DosingService`) |
| Pumpe, Protokollzeile, Zwecke, Auslöser | `GrowDiary.Web/Models/DosingPump.cs` |
| Takt: Wirkung nachtragen, Automatik, zweite Hälfte geben | `GrowDiary.Web/Services/DosingWorker.cs` |
| Lage vor einer Dosis (Messwert, Ziel, Sonde, Umwälzung, Füllstand) | `GrowDiary.Web/Services/DosingContextBuilder.cs`, `DosingSituation.cs` (`DosingSituationRules`) |
| Wann die Wirkung eingetragen werden darf | `GrowDiary.Web/Services/DosingFollowUp.cs` |
| A + B: Verhältnis, Trennzeit, Prüfung des Paares | `GrowDiary.Web/Services/PartnerDosing.cs` |
| Endpunkte: Pumpen, Kalibrierung, Dosis, Stopp, Vorschlag, Protokoll | `GrowDiary.Web/Api/Controllers/DosingApiController.cs` |
| Addback: Rechnung, Endpunkte, Einträge | `GrowDiary.Web/Services/AddbackCalculator.cs`, `Api/Controllers/GrowWorkflowApiController.cs`, `Models/AddbackLogEntry.cs`, `Models/ChangeoutEntry.cs`, `Infrastructure/AddbackRepository.cs` |
| Vorgang (Wechsel und Nachfüllen): anlegen, löschen, gemeinsame Bausteine | `Api/Controllers/AddbackVorgangApiController.cs`, `WasserwechselApiController.cs`, `VorgangEingabe.cs`, `Infrastructure/AddbackVorgangRepository.cs`, `WasserwechselVorgangRepository.cs`, `VorgangBausteine.cs` |
| Pumpen-Wächter: Urteil / Meldung | `GrowDiary.Web/Services/PumpWatchService.cs`, `PumpWatchNotifier.cs` |
| Wann zuletzt gewechselt wurde — **die einzige Antwort** | `GrowDiary.Web/Services/Wasserwechsel.cs` |
| Stand (Tage seit, fällig/überfällig, Plan) | `GrowDiary.Web/Services/WasserwechselStandService.cs`, Endpunkt `GET /api/grows/{id}/changeouts/stand` |
| Oberfläche | `GrowDiary.React/src/pages/DosingPage.tsx`, `DosingPumpSetupPage.tsx`, `AddbackPage.tsx`, `WasserwechselPage.tsx`, `src/features/vorgang/` (`VorgangAblauf.tsx`, `ablauf-rechnung.ts`), `src/features/addback/NachfuellListe.tsx`, `src/features/changeouts/` (`ChangeoutsPanel.tsx`, `WasserwechselStand.tsx`, `routine-weg.ts`), `src/features/dosing/calibration.ts` |
| Fachwissen mit Quellen | `GrowDiary.Web/wwwroot/knowledge-defaults/sops/nutrient-addback.json`, `guidance/addback-mixing-procedure.json`, `guidance/addback-part-limit.json` |

## Fallen

- **Ein eingetragener Wasserwechsel zählte nicht.** Bis zum 31.08.2026 gab es
  zwei Wahrheiten darüber, wann zuletzt gewechselt wurde: das Häkchen
  `SolutionChange` an einer Messung und die Tabelle `Changeouts` (das Formular).
  Drei der vier Rechnungen lasen nur die Messung — wer den Wechsel im Formular
  eintrug, räumte damit **keine einzige** Mahnung weg; „Wöchentlicher
  Wasserwechsel: zuletzt vor 20 Tagen" blieb stehen. Jetzt liest jeder über
  `Wasserwechsel.ZuletztOrtszeit(…)`, und `WasserwechselEineWahrheitTests`
  zählt über alle `.cs`-Dateien, dass niemand wieder selbst rechnet.
- **Das Formular war nicht zu finden.** Es lag als dritter Abschnitt auf
  `/addback`; „Wasserwechsel" stand im ganzen Menü nur als *Schlagwort bei den
  Aufgaben* — wer das Wort tippte, landete auf der falschen Seite. Vier Stellen
  mahnten ihn an, keine führte zum Eintragen. Seit beta.60 hat er eine eigene
  Seite, und die Mahnungen verlinken sie (`routine-weg.ts`).
- **Keine Pumpe hatte je etwas gelernt.** `ValueAfter` blieb immer null, weil den
  Wert nach der Dosis niemand nachtrug — und die Lernrechnung überspringt genau
  solche Zeilen. Behoben mit `DosingFollowUp` + `RecordEffects` (beta.13).
- **Die Tagesgrenze lief in UTC.** `nowUtc.Date` ist hier 02:00 Ortszeit — eine
  Pumpe konnte von 23:00 bis 01:59 die volle Tagesmenge fahren und ab 02:00
  gleich noch einmal (beta.38).
- **Kalibrierläufe zählten wie Dosen** — danach war 18 Minuten lang keine Dosis
  möglich, obwohl nichts ins Becken ging. `DosingGuard.Evaluate` filtert
  `DoseTrigger.Calibration` heraus.
- **Die Mischpause gehörte der Pumpe statt dem Becken.** Eine Minute nach der
  B-Hälfte hätte die pH-Pumpe in die Schliere gemessen. Jetzt zählt die jüngste
  Dosis *irgendeiner* Pumpe des Zelts (`LastTentDoseUtc`).
- **Die zweite Hälfte prüfte die Umwälzung nicht** — A verlangte bestätigte
  Umwälzung, B lief fünf Minuten später ungeprüft (beta.18). Ihr
  `PendingDose`-Eintrag wird jetzt *vor* dem Schalten gelöscht: fehlt B einmal,
  ist das harmloser, als B doppelt zu geben.
- **Zwei Wahrheiten für dasselbe EC-Ziel.** Die Abkürzung
  `GetTargets(HydroStyle, stage)` landet immer beim Standardprofil; die Diagnose
  meldete 0,6–0,8, während die Live-Kachel für denselben Grow 0,9–1,1 sagte. Die
  Addback-Vorgaben gehen jetzt über `SetpointProfileResolver`.
- **Ein Riegel, der nicht greifen kann:** `DosingContext.WaterLevelOk` wird von
  `DosingContextBuilder` immer als `null` übergeben. „Wasserstand unter Minimum"
  in `DosingGuard.Evaluate` existiert, löst heute aber nie aus — der Füllstand
  wirkt nur über `VolumeFactor`.
