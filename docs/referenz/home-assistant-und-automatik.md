# Home Assistant, Regeln und Hintergrunddienste

> Home Assistant liefert alle Live-Werte und schaltet alles Schaltbare; Cultivate Control
> ordnet zu, wertet aus, meldet und gibt Befehle.

## Wo in der App

| Was | Wo |
|---|---|
| Verbindung, Entität je Messgröße, Kameras | Einrichtung → Home Assistant · `/home-assistant` |
| Grenzwerte | Betrieb → Regeln & Automatik · `/regeln?tab=grenzwerte` (alt: `/alarme`) |
| Auto-Messungen | `/regeln?tab=automatik` (alt: `/automatik`) |
| Benachrichtigungen, Ruhezeit, Tagesüberblick | `/regeln?tab=push` (alt: `/benachrichtigungen`) |
| Kühler-Regler einstellen | Betrieb → Crop Steering · `/cropsteering` |
| Karte „Kühler · Crop Steering" (was der Regler gerade tut) | Live · `/` |
| Pumpen-, Kühler- und USV-Befunde als Risiko | Jetzt → Aufgaben · `/aufgaben` |
| Ziel der meisten Push-Nachrichten | `/aufgaben`, bei Kalibrierung/Wartung/Sensorausfall `/sensoren` |

## Was es tut

**Verbindung.** Als HA-Add-on ohne Eingabe: Token und Adresse kommen vom
Supervisor (`HomeAssistantAddon`, `SUPERVISOR_TOKEN`, `http://supervisor/core`).
Sonst URL und Long-Lived-Token von Hand.

**Zuordnung.** Je Zelt bekommt jede Messgröße eine Entität — 17 Felder in drei
Gruppen (Zelt, RDWC/DWC, Technik). Die Auswahlliste kommt über
`/api/home-assistant/entities`, dahinter HAs eigenes `GET /api/states`.

**Wichtigster Punkt für Code hier:** `HomeAssistantService.GetStatesAsync`
liefert ein Wörterbuch mit **Metrik-Schlüsseln** (`reservoir-temp`, `chiller`,
`light-status`), **nie** Entitäts-Kennungen; übersetzt wird in
`TentSensorMetricKeyMap.Resolve`. Wer dort `switch.kuehler` nachschlägt, findet
grundsätzlich nichts — dafür gibt es `GetEntityStateAsync`.

**Grenzwerte.** Je Zelt und Messgröße min/max plus Karenz.
`AlertEvaluationService.Decide` ist level-getriggert: erste Überschreitung
sofort, danach alle *Karenz* Minuten erneut, dazu eine Entwarnung. Ging die
Nachricht nicht raus, bleibt der alte Zustand stehen; der nächste Takt versucht
es wieder.

**Auto-Messungen.** Ein Auslöser (`Manual`, `LightOnDelay`, `LightOffDelay`)
legt zur Lichtflanke plus Verzögerung eine Messung an, optional mit
Kamera-Schnappschuss. Werte laufen durch `AutoMeasurementValueGuard` — engere
Bänder als von Hand, darüber die physikalische Tabelle aus
`MeasurementSanityService`.

**Fünf Hintergrunddienste** (`Program.cs`, `AddHostedService`):

| Dienst | Takt | Was |
|---|---|---|
| `HomeAssistantSnapshotWorker` | 5 min | Werte speichern, Lichtflanke, Nachtabsenkung, Sensorausfall, Kamera, Tageswerte, Aufräumen, Erinnerungen, Tagesüberblick |
| `AlertWatchWorker` | 1 min | Grenzwerte, Pumpen-Wächter, Watchdog, Trend-Wächter — holt Werte, speichert nichts |
| `AutoMeasurementWorker` | 5 min | fällige Auslöser, Schnappschüsse |
| `DosingWorker` | 1 min | Wirkung nachtragen, zweite Hälfte A+B, Automatik (Dünger vor pH) |
| `KuehlerWorker` | 1 min | Kühler-Steckdose schalten, ins Anlagen-Protokoll |

Alle starten versetzt, damit sie nicht im Gleichschritt auf HA einschlagen.

## Die Zahlen und woher sie kommen

| Zahl | Wert | Woher |
|---|---|---|
| Zeitlimit je HA-Abruf | 4 s | `HomeAssistantService.RequestTimeout` |
| Sperre nach Transportfehler | 20 s | `HomeAssistantService.BackoffWindow` |
| Startversatz der Dienste | 15 / 30 / 30 / 45 / 50 s | `ExecuteAsync` in Snapshot-, Alarm-, Auto-Mess-, Dosier-, Kühler-Worker |
| Rohablesungen werden gelöscht | älter als 7 Tage | `HomeAssistantSnapshotWorker.CleanupOldReadingsAsync` |
| Tages-Aggregation | ab 02:00, ab 3 Ablesungen je Messgröße | `AggregateYesterdayAsync` |
| Kalibrier-/Wartungs-Erinnerung | einmal täglich ab 08:00 | `HomeAssistantSnapshotWorker.ExecuteAsync` |
| Kamera-Schnappschuss | einmal täglich ab 12:00 | `TryCaptureCamera` |
| Watchdog „Überwachung steht" | 16 min | `WatchdogService.StalledMinutes` |
| Watchdog „Zelt ist dunkel" | 21 min | `WatchdogService.NoDataMinutes` |
| Karenz je Grenzwert-Regel | 30 min (Standard) | `TentAlertRule.CooldownMinutes`, `AlertsPage.tsx` |
| Grenzwert-Platzhalter (nur Feldvorschlag, keine Voreinstellung) | pH 5,5–6,5 · EC 1,2–2,4 · Wassertemp 18–22 °C · DO ab 6 | `AlertsPage.tsx`, `ALERT_METRICS` |
| Pumpen-Schonfrist, bis „aus" als Ausfall gilt | 15 min (Faustregel, 1–720 einstellbar) | `PumpWatchService.StandardSchonfristMinuten` |
| Fenster einer Auto-Messung | 20 min | `AutoMeasurementConfig.WindowMinutes` |
| Tagesüberblick | 06:00, standardmäßig aus | `NotificationSettings.DigestHour` |
| Kühler-Totband | ±0,4 °C | `KuehlerService.StandardHystereseC` |
| Kühler Mindestlauf / Mindestpause | je 5 min | `KuehlerService.StandardMindestlaufMinuten` / `StandardMindestpauseMinuten` (Druckangleich, Herstellerrichtwert) |
| Höchstalter des Messwerts zum Schalten | 10 min | `KuehlerService.StandardHoechstalterMinuten` |
| Harte Untergrenze Wassertemperatur | 12,0 °C | `NachtabsenkungService.AbsoluteUntergrenzeC` |

## Was es bewusst NICHT tut

- **Cultivate Control schaltet nichts selbst.** Es hat keine Anschlüsse; jeder Befehl geht
  über `HomeAssistantService.CallEntityServiceAsync`. Geregelt wird in HA.
- **Keine KI.** Weder hier noch sonst in der App.
- **Nachts kein Alarm für PPFD, CO₂ und VPD** — 0 µmol ist bei Licht aus der
  Sollzustand (`LightClock.DaytimeOnlyKeys`).
- **Während der Trocknung keine Reservoir-Alarme** — die Sonden liegen trocken
  (`DryingWindow`). Nur im Trocknungsfenster, nicht pauschal „ohne Grow".
- **Kein Sollwert heißt nicht „dann eben aus".** Ein Autoflower hat keine
  Blütewoche; der Kühler bleibt, wie er ist. Dasselbe bei unbekanntem
  Messwert-Alter und unbekanntem Steckdosenzustand (`KuehlerService.Entscheiden`).
- **Liefert HA gar keine Zustände, meldet niemand Sensorausfall** — sonst ein
  Alarm je Sensor für einen einzigen Ausfall (`EvaluateSensorOfflineAsync`).
- **Watchdog, Pumpen- und Trend-Wächter melden je Zustandswechsel**, nicht im
  Takt — mit Entwarnung, weil eine nie zurückgenommene Warnung ignoriert wird.
- **Der Kühler protokolliert „nichts zu tun" nicht.** Eine Zeile je Minute je
  Zelt liest niemand; der Grund steht auf der Live-Karte.
- **Die Ruhezeit gilt für alles außer dem Tagesüberblick**
  (`NotificationService.SendDigestAsync`).

## Schreiben mit Nachkontrolle

Home Assistant meldet auf einen Dienstaufruf **„gesendet", nicht
„angekommen"**. Bei Geräten hinter einer Hersteller-Wolke ist das ein echter
Unterschied: die AC-Infinity-Wolke verwirft parallele Aufträge und antwortet
mit `Unable to update device controls`. Wer drei Werte gleichzeitig schickt,
bekommt im besten Fall einen davon — und die Oberfläche sagt trotzdem
„gestellt".

`AcSchreiber` schreibt deshalb **nacheinander** und liest **jedes Mal nach**:

| Schritt | Verhalten |
|---|---|
| Steht der Wert schon? | Nichts senden — jeder Aufruf kann scheitern |
| Nach dem Senden | Alle 2 s nachfragen, längstens 20 s |
| Meldet die Entität nicht den Sollwert | Bis zu 3 Versuche, dann **Schwebezustand** (gelb) |
| Zwischen zwei Schritten | 2 s Pause |
| Ein Schritt scheitert | Die folgenden werden **nicht** versucht |

Die Zahlen (2 s Pause, 20 s Wartezeit, 3 Versuche) stammen aus der
Home-Assistant-Karte des Testers — **Faustregeln aus seinen Versuchen mit der
Wolke, keine Herstellerangabe.** `AcSchreiberTests` hält sie fest, damit eine
Änderung auffällt.

**Drei Ausgänge, nicht zwei.** Bestätigt → grün. Von Home Assistant gar nicht
angenommen → Fehler (502), denn dann wurde nichts geschaltet. Gesendet, aber
nicht zurückgemeldet → **gelb, kein Fehler**: die AC-Infinity-Integration
meldet neue Werte oft erst nach ihrer nächsten Wolken-Abfrage zurück — Minuten
später, während das Gerät längst geschaltet hat. Genau so hat es der Tester
erlebt („manchmal kommt 502, aber das Schalten funktioniert"); seither liest
die Seite den Stand nach einem gelben Ausgang von selbst nach.

**Warum die Reihenfolge zählt.** Beim Zeitplan kommt der Modus zuletzt: ein
Gerät, das auf „nach Zeitplan" steht, aber noch die alten Zeiten trägt,
schaltet nach dem alten Plan — schlimmer als gar nichts. Und ein Teilerfolg
wird als Teilerfolg gemeldet, nicht als „gespeichert".

Benutzt wird das heute vom Versuchsaufbau `/ac-test` (Stufe 0–10 und
Zeitplan). Der Zeitplan-Vorschlag kommt aus dem **Lichtplan des Zelts** —
derselben Quelle, aus der auch der Wächter gegen Lichteinbruch liest.

## Im Code

| Aufgabe | Datei |
|---|---|
| HA-Abruf, Kamera, Dienstaufruf, Push, Sicherung gegen tote Verbindung | `GrowDiary.Web/Services/HomeAssistantService.cs` |
| Messgröße → Metrik-Schlüssel | `GrowDiary.Web/Services/TentSensorMetricKeyMap.cs` |
| Grenzwert-Entscheidung (rein) und Auswertung | `GrowDiary.Web/Services/AlertEvaluationService.cs` |
| Minutentakt: Alarme, Pumpen, Watchdog, Trends | `GrowDiary.Web/Services/AlertWatchWorker.cs` |
| 5-Minuten-Takt, Speicherung, Tageswerte, Kamera | `GrowDiary.Web/Services/HomeAssistantSnapshotWorker.cs` |
| Totmannschalter | `GrowDiary.Web/Services/WatchdogService.cs` |
| Push-Torwächter (Kategorie, Ruhezeit, Ziel-Pfad) | `GrowDiary.Web/Services/NotificationService.cs` |
| Pumpen-, Kühler-, USV-Meldung | `GrowDiary.Web/Services/PumpWatchNotifier.cs` |
| Kühler-Regelung (rein) und Ausführung | `GrowDiary.Web/Services/KuehlerService.cs`, `KuehlerWorker.cs` |
| Schreiben mit Nachkontrolle (Wolke verwirft still) | `GrowDiary.Web/Services/AcSchreiber.cs` |
| Bau-Kennung der laufenden Assembly | `GrowDiary.Web/Services/Bauzeit.cs` |
| Testbetrieb: was geschaltet wurde, bleibt geschaltet | `GrowDiary.Web/Services/Demoschaltbrett.cs` |
| Tag/Nacht-Frage für Alarme und Kacheln | `GrowDiary.Web/Services/LightClock.cs` |
| Liste der Hintergrunddienste | `GrowDiary.Web/Program.cs` |
| Oberfläche (`GrowDiary.React/src/pages/`) | `HomeAssistantPage.tsx`, `AlertsPage.tsx`, `AutomationPage.tsx`, `NotificationsPage.tsx`, `collections.tsx` |
| Endpunkte (`GrowDiary.Web/Api/Controllers/`) | `HomeAssistantApiController.cs`, `AlertsApiController.cs`, `AutoMeasurementsApiController.cs`, `NotificationsApiController.cs`, `PumpWatchApiController.cs` |

## Fallen

- **Metrik-Schlüssel ≠ Entitäts-Kennung.** Der Kühler-Regler suchte
  `switch.kuehler` im Zustands-Wörterbuch und hätte in einer echten Anlage
  **nie** geschaltet. Es fiel nicht auf, weil die Demo-Daten genau diesen
  Schlüssel zusätzlich eintrugen — die Kulisse hat den Fehler verdeckt.
  `KuehlerLageTests` prüft jetzt das Gegenteil (beta.52).
- **`last_changed` statt `last_updated`.** Ersteres steht still, solange derselbe
  Wert gemeldet wird — also genau dann, wenn die Regelung trifft. Der Messwert
  galt als veraltet und blockierte sie (beta.52).
- **Zeitfenster statt Befehl.** Ein vom Regler abgeschalteter Kühler galt nach
  20 Minuten wieder als Ausfall; eine kühle Nacht ist genau dieser Fall. Jetzt
  entscheidet der zuletzt *gesendete* Befehl (`KuehlerService.IstAbsichtlichAus`).
- **„Gesendet" ist nicht „angekommen".** Ein Dienstaufruf, der zurückkehrt,
  belegt nur, dass Home Assistant ihn entgegengenommen hat. Alles hinter einer
  Hersteller-Wolke braucht eine Nachkontrolle (siehe oben).
- **Der Testbetrieb meldete Erfolg, ohne etwas zu ändern.** Damit war alles,
  was *nach* dem Schalten kommt, im Testbestand nicht prüfbar — die
  Nachkontrolle, der Wächter über der Steckdose, jede Anzeige eines
  geschalteten Geräts. Und die Kühler-Karte zeigte „steht" neben dem Satz
  „Kühler an". Seit dem 25.08.2026 hält `Demoschaltbrett` fest, was gestellt
  wurde.
- **Der laufende Stand war nicht der gebaute.** Ein Prozess aus einer früheren
  Sitzung hielt den Port; der neue Start meldete trotzdem Erfolg. Deshalb
  trägt `backend-health` jetzt `bauKennung` — vor jeder Messung gegen die eben
  gebaute `GrowDiary.Web.dll` halten.
- **Watchdog nach dem Neustart.** Der Herzschlag lebt im Speicher, also meldete
  er nach jedem Update „Überwachung steht". Eigener Zustand `Starting` (beta.8).
- **Ruhezeit verschluckte Alarme** (1.0.22), später auch Entwarnungen (beta.38).
  Heute bleibt der Zustand stehen und wird nachgeliefert.
- **Zu enges Zeitfenster.** Die Tages-Aggregation lief einmal nur 02:00–02:05.
  Ein zähes HA um 01:59 ließ den Vortag ausfallen — und nach sieben Tagen sind
  die Rohwerte weg. Jetzt „ab 02:00, einmal je Tag".
