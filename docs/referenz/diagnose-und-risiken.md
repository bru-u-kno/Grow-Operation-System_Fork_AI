# Diagnose, Abweichungen und Risiken

> Aus Messwerten und aus dem, was man sieht, wird ein benannter Befund mit einem
> nächsten Schritt — ohne KI, nur mit der Wissensbasis.

## Wo in der App

| Was | Wo |
|---|---|
| Menü „Pflanzen" → „Diagnose" | `/diagnose` |
| Dieselbe Ansicht innerhalb eines Grows | `/grows/<id>`, Abschnitt `diagnosis` |
| Offene Risiken zum Bestätigen/Erledigen | Menü „Jetzt" → „Aufgaben", `/aufgaben` |
| Karte „Beobachtungen · über Tage" | Live-Seite `/` (`TrendWatchPanel`) |
| Zustandsampel je Zelt (kritisch/beobachten/stabil) | Live-Seite `/`, je Zelt aus `/api/live/tents/{id}` |
| Schnittstellen | `GET /api/grows/{id}/deviations`, `/api/grows/{id}/treatment-recommendations`, `/api/observations`, `/api/trends/{growId}`, `/api/trends/{growId}/stability`, `/api/risk-events` |

## Was es tut

**Von der Zahl her.** `DeviationAnalyzerService.Analyze` sieht sich die zehn
jüngsten Messungen eines Grows an und prüft neun Größen: pH, pH-Drift­geschwindigkeit,
EC, ORP, Wassertemperatur, gelösten Sauerstoff, VPD, PPFD, CO₂. Läuft nur für
`ActiveHydro`-Grows mit Hydro-Profil. Die Zielbereiche kommen über die Kette Grow →
System → Anbaustil (`SetpointProfileResolver`) und werden mit eigenen Grenzwerten
überlagert (`UserTargets.Overlay`).

**Vom Befund zur Handlung.** `TreatmentRecommender` bildet jede Abweichung auf eine
Symptom-Kennung ab (`MapDeviationToSymptomId`) und holt daraus Behandlungen und
Abläufe samt Sicherheitshinweisen, Konflikten und Quellen. Fehlt die Zuordnung,
entsteht eine ehrliche Rückfallkarte statt eines erfundenen Rats.

**Vom Befund zur Aufgabe.** `DeviationRiskEventSyncService` macht aus jeder
Abweichung der Stufe *Warnung* oder *Kritisch* ein `RiskEvent` mit dem
Entdopplungs-Schlüssel `deviation:grow:<id>:<stableKey>`; verschwindet die
Abweichung, schließt sich das Ereignis von selbst. Der Abgleich läuft auf zwei
Wegen: wenn jemand `GET /api/risk-events` abruft (`RiskEventsApiController.List`),
und im Minutentakt des `AlertWatchWorker` (`RisikoAbgleichRunner`) — dort aber nur,
wenn es für einen Grow eine neue Messung gibt (Fingerabdruck der Messungen,
`GrowRepository.GetMeasurementFingerprint`). Offene Abweichungs-Karten tragen
außerdem den **Stand der Prüfung** (`MessTakt`): Zeit der neuesten Messung, üblicher
Abstand der automatischen Messungen, erwartete nächste Messung und „überfällig" ab
dem 1,5-Fachen dieses Abstands (Pille „Nicht mehr geprüft"). Der Abgleich rechnet mit demselben
Blattversatz des Zelts und demselben Sollwert-Profil wie die Diagnose
(`GrowRepository.LeafOffsetFor`, `SystemProfileIdFor`) — sonst nennt die Karte
einen anderen Wert als die Diagnose. `RiskActionCard` bietet *Bestätigen*,
*Erledigt* und *SOP vorschlagen*; die vorgeschlagenen Abläufe startet man
einzeln. Sie zeigt außerdem den Zustand („Besteht noch" / „Wieder okay"), den
Wert mit Zielband, „Seit …", „Zuletzt gesehen vor …" und, wenn dieselbe
Abweichung schon früher eine Karte hatte, „N× seit …" (`risiko-zeit.ts`). Ein vom
Abgleich geschlossenes Risiko (`resolvedAutomatically`) bleibt 24 Stunden als
„Wieder okay" sichtbar; von Hand erledigte verschwinden sofort.

**Von der Pflanze her.** `BeobachtungsWegweiser` fragt Blatt, Wurzel oder Lösung ab
und zeigt je Beobachtung mögliche Ursachen, was man selbst prüfen kann, und die
hinterlegten Behandlungen und Abläufe.

**Über Tage.** `TrendWatchService` findet, was keine einzelne Messung verrät:
durchgehende Drift, ausgebliebener Wasserwechsel, überfällige ORP-Nachdosierung,
eingebrochener oder explodierter Verbrauch. `TrendWatchRunner` reitet auf dem
Minutentakt des `AlertWatchWorker` mit und meldet flankengesteuert.

**Anlage statt Pflanze.** `AnlagenWatchService` beurteilt Kühler und USV,
`AnlagenRisikoService` legt daraus Ereignisse unter der Vorsilbe `anlage:` an —
getrennt von den Abweichungen, damit deren Aufräumroutine sie nicht mit abräumt.

**Das Muster statt des Werts.** `SolutionStabilityAnalyzer` liest die Tabelle aus
SOP-RDWC-CAN-N1 §2.1 über fünf Signale gemeinsam: derselbe fallende pH heißt je nach
EC, Sauerstoff und ORP „Pflanze frisst" oder „Biofilm".

## Die Zahlen und woher sie kommen

| Zahl | Wert | Woher |
|---|---|---|
| pH-Komfortband | 5,8–6,2 | `DeviationAnalyzerService.PhComfortMin/Max`, RDWC-Growplan |
| pH kritisch | < 5,5 · > 6,5 | `PhCriticalMin/Max` |
| pH-Drift kritisch | ≥ 0,5 Punkte in ≤ 24 h | `PhDriftCritical`, `PhDriftWindowHours`; SOP-RDWC-CAN-N1 §2.1 |
| pH-Drift leicht | ab 0,2 Punkten | `PhDriftLight`; SOP-N1 §2.1 |
| Sauerstoff: Handlungsschwelle | 6,5 mg/L | `DoActionThreshold`; SOP-RDWC-CAN-N1 Abschnitt 2.2 |
| Sauerstoff: Fäule bestätigt | 6,0 mg/L | `DoInfestationThreshold`; SOP-RDWC-CAN-S1 Abschnitt 2.2 |
| EC kritisch | > 3,0 mS/cm | `CheckEc` |
| EC-Sprung zwischen zwei Messungen | > 0,2 mS/cm | `GetEcTrendParticipants` |
| ORP-Zielband · kritisch | aus dem Profil je Phase (rdwc-default Blüte 400–450) · < 250 / > 650 | `CheckOrp` über `Zielband.FuerGrow` |
| Wassertemperatur Arbeitsbereich | 17–22 °C; ein eigener Grenzwert ersetzt die jeweilige Seite | `Wasserband.Grenzen`; SOP-RDWC-CAN-N1 |
| Wassertemperatur kritisch | > 24 °C · unten `min(14 °C, Untergrenze − 3)` | `Wasserband.KritischMaxC/KritischMinC`, gerechnet in `CheckWaterTemp` |
| PPFD-Deckel ohne CO₂ | 900 µmol/m²/s | `PpfdCeilingWithoutCo2`; Anreicherung zählt ab `Co2EnrichmentFrom` = 800 ppm |
| PPFD kritisch · Warnung | > 1500 · > Ziel × 1,2 | `CheckPpfd` |
| CO₂ Warnung · kritisch | > 1600 · > 2500 ppm | `CheckCo2` |
| VPD: Warnung statt Info | ab 0,4 kPa Abstand zum Band | `CheckVpd` |
| Keimung / Bewurzelung | Warnung ab 7, kritisch ab 14 Tagen | `DeviationAnalyzerService.CheckGerminationAndRooting` — **die Methode ruft niemand auf**, die Warnung erscheint nirgends |
| Rückblick der Diagnose · Toleranz für Zeitstempel voraus | 10 Messungen · 1 Stunde | `MaxConsecutiveLookback`, `DeviationAnalyzerService.Analyze` |
| Trend-Fenster · Mindestpunkte | 7 Tage · 4 Tageswerte | `TrendWatchService.WindowDays`, `MinimumPoints` |
| Drift-Schwellen über das Fenster | pH 0,25 · EC 0,30 · ORP 60 mV · Wasser 2,0 °C | `TrendWatchService.Evaluate` |
| Wasserwechsel fällig · überfällig | 7 · 10 Tage | `WaterChangeDueDays`, `WaterChangeOverdueDays`; Growplan: wöchentlich |
| ORP-Nachdosierung fällig · überfällig | 3 · 5 Tage | `OrpTopUpDueDays`, `OrpTopUpOverdueDays`; SOP-N1: alle 2–3 Tage per HOCl |
| Verbrauch: Einbruch · Sprung | ≤ 0,5× · ≥ 2,0× | `TrendWatchService.AddConsumption` |
| USV-Ladestand Warnung · kritisch | 70 % · 40 % | `AnlagenWatchService`; **Faustregel**, ausdrücklich keine Herstellerangabe |
| Beobachtungen im Wegweiser | 7 von 20 Symptomen (Blatt 3 · Wurzel 2 · Lösung 2) | `wwwroot/knowledge-defaults/symptoms/`; 10 tragen die Kategorie `Sensor` und damit keinen Bereich, 3 haben keine Ursachen (`BeobachtungsWegweiser.Gruppen`) |

## Was es bewusst NICHT tut

- **Keine KI.** Der Wegweiser sind drei Fragen und eine Liste; die Zuordnung
  Beobachtung → Ursache → Behandlung stand immer schon in den Symptom-Dateien und
  wurde nur nie angeboten (`BeobachtungsWegweiser`, `<remarks>`).
- **Den pH nicht jagen.** Innerhalb der Komfortzone wird nicht gewarnt: der
  Phasen-Sollwert ist ein Anmischziel, keine Schwelle. Ausnahme: selbst eingetragene
  Grenzen sind bindend, auch wenn sie enger sind (`CheckPh`, Parameter `phIsUserSet`).
- **Routinen sind keine Befunde.** Die drei Einträge ohne mögliche Ursachen
  („Präventive Routine-Maßnahme", „Steckling bereit für Hauptsystem",
  „CalMag-Routine-Baseline (Athena-Plan)") erscheinen nicht — gefiltert über das
  Fehlen von Ursachen, nicht über eine Ausnahmeliste, die beim nächsten Symptom
  veraltet.
- **Info wird nicht laut.** Info-Abweichungen werden nicht zu Risiko-Ereignissen
  (`IsActionable`), Info-Trends lösen keinen Push aus (`TrendWatchRunner`).
- **Kein „unbekannt heißt Gefahr".** Ohne gemappten Kühler und USV schweigt der
  Anlagen-Wächter, ohne dokumentierten Wasserwechsel wird keiner angemahnt, ohne je
  gemessenen ORP keine Nachdosierung.
- **Keine nackten Kennungen.** Was in der Wissensbasis nicht (mehr) existiert, wird
  weggelassen, statt als `hocl-orp-boost-emergency` auf dem Schirm zu stehen.
- **Keine Zahl ohne Fenster.** Der Stabilitäts-Analysator rechnet keine
  Geschwindigkeit aus zwei zu weit auseinanderliegenden Messungen
  (`MaxHoursBetweenPoints`) — und fragt nach Aussehen und Geruch des Wassers, statt
  die sensorlosen Zeilen der SOP-Tabelle stillschweigend fallen zu lassen.

## Im Code

| Aufgabe | Datei |
|---|---|
| Abweichungen aus Messwerten | `GrowDiary.Web/Services/DeviationAnalyzerService.cs` |
| Wassertemperatur-Band (eine Wahrheit) | `GrowDiary.Web/Services/Wasserband.cs` |
| Abweichung → Symptom → Behandlung/SOP | `GrowDiary.Web/Services/TreatmentRecommender.cs` |
| Abweichung → Risiko-Ereignis, Entdopplung | `GrowDiary.Web/Services/DeviationRiskEventSyncService.cs` |
| Risiko → Notfall-Ablauf | `GrowDiary.Web/Services/RiskEventSopRecommender.cs` |
| Diagnose von der Pflanze her | `GrowDiary.Web/Services/BeobachtungsWegweiser.cs`, `Api/Controllers/ObservationsApiController.cs` |
| Drift über Tage, Push | `GrowDiary.Web/Services/TrendWatchService.cs`, `TrendWatchRunner.cs`, `AlertWatchWorker.cs` |
| SOP-N1-Tabelle über fünf Signale | `GrowDiary.Web/Services/SolutionStabilityAnalyzer.cs` |
| Anlagenstörung als Risiko | `GrowDiary.Web/Services/AnlagenWatchService.cs`, `AnlagenRisikoService.cs` |
| Karten und Ampel der Live-Seite | `GrowDiary.Web/Services/GrowAlertService.cs`, `RecommendationEngine.cs` |
| Oberfläche Diagnose | `GrowDiary.React/src/features/grow-detail/GrowDetailDiagnosisSection.tsx`, `ObservationGuide.tsx` |
| Oberfläche Risiko und Trend | `GrowDiary.React/src/features/risks/RiskActionCard.tsx`, `features/risks/risiko-zeit.ts`, `Services/MessTakt.cs`, `Services/RisikoAbgleichRunner.cs`, `features/live/TrendWatchPanel.tsx` |

## Fallen

- **Die Diagnose urteilte aus der Zukunft.** Eine Testzeile mit dem Datum
  2099-01-01 war sechs Wochen lang die „jüngste" Messung. Jetzt fliegen Zeitstempel
  mehr als eine Stunde voraus raus (`DeviationAnalyzerService.Analyze`).
- **„Erledigt" tat nichts** (beta.33). Die Entdopplung kannte nur offene Ereignisse
  — der nächste Abgleich legte dasselbe Risiko aus derselben Messung wieder an.
  Jetzt bleibt erledigt erledigt, bis eine **neuere** Messung es erneut zeigt.
- **Die App meldete ihre eigene Regelung** (beta.52). Die Nachtabsenkung fährt auf
  den Finish-Nachtwert des Profils (Standard 16 °C), der Arbeitsbereich begann bei
  17 °C — ab Blütewoche 3 lag jede Nachtmessung „außerhalb". Behoben in
  `Wasserband.UntergrenzeC`.
- **Eigene Grenzwerte kamen nicht an** (beta.11). Der Analyse übergeben und nie
  zugewiesen: dieselbe Messung bekam auf Kachel und in der Diagnose zwei Urteile
  (EC 0,9–1,1 gegen 0,6–0,8).
- **Enum-Namen auf dem Schirm** (beta.47). Der Aufgabentitel kam aus
  `deviation.Metric`, also las man „Ec: Abweichung prüfen". Hält jetzt
  `DeviationRiskEventSyncService.ToMetricLabel`.
- **Gelöschte Grows behielten ihre Warnungen** (beta.48). `RiskEvents` hat keinen
  Fremdschlüssel auf `Grows` und `PRAGMA foreign_keys` ist aus; die Synchronisierung
  läuft nur über aktive Grows.
- **Schwere falsch eingefärbt.** „Kritisch" und „Info" trugen fest verdrahtete
  Farben, Kontrast 1,1 im hellen Thema (beta.22). Später war die Skala um eine Stufe
  verschoben — jede Meldung sah harmloser aus, als sie war (`severityBadge` in
  `GrowDetailDiagnosisSection.tsx`).
