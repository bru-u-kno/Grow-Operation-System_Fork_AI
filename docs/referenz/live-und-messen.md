# Live, Messen und das Messprotokoll

> Der tägliche Weg: sehen, was gerade ist — eintragen, was das Messgerät sagt — später nachlesen, ob es im Ziel lag.

## Wo in der App

| Was | Wo |
|---|---|
| Live | `/` — Menü „Jetzt → Live" (`navigation.ts`) |
| Messen (Formular) | `/messung` — Menü „Jetzt → Messen" |
| Grow-Tagebuch (A-006) | `/tagebuch` — Menü „Pflanzen → Tagebuch" |
| Messungen (Protokoll) | `/messungen` — seit A-006 nicht mehr im Menü; Link „Messungen als Tabelle" oben im Tagebuch, Suche |
| Dasselbe Protokoll im Grow | Abschnitt `measurements` in `/grows/:growId` |
| Eine Messung bearbeiten | `/grows/measurements/:measurementId/edit` |
| Kamera | Bühne auf der Live-Seite; Snapshot-Knopf im Messformular |
| Kühler-Karte | Live-Seite, Knopf führt auf `/cropsteering` |
| Alte Adressen | `/live` → `/`, `/messungen/new` → `/messung` (`App.tsx`) |

## Was es tut

**Live.** Zwei feste Bänder, „Klima" und „Hydroponik · Nährlösung". Jede Kachel zeigt Zahl, Skala und Zielband; die Ziele kommen aus der Kette Grow → Hydro-System → Anbaustil (`SetpointProfileResolver`), überschrieben vom Feedchart, zuletzt vom eigenen Grenzwert. Klick auf eine Kachel klappt die 24-h-Kurve **unter** der Zeile auf — nur, wenn es mehr als einen Punkt gibt.

Fehlt ein Sensor, fällt die Kachel auf die letzte Messung zurück, **je Kennzahl einzeln**. Darunter steht „Hand · vor 2 Std" oder „Automatik · vor 2 Std"; ab 36 h wird daraus „nachmessen?" bzw. „Automatik prüfen?". „▦ Anpassen" öffnet eigene Bereiche und Kacheln (Messwert, HA-Entität, Verlauf), je Zelt gespeichert.

**Messen.** 21 Zahlenfelder mit Lesbarkeitsprüfung, dazu Phase, Herkunft, Notiz, Fotos und Kamera-Snapshot. „Aus Home Assistant übernehmen" füllt aus den gemappten Sensoren vor (`pullLive` über `/api/live/tents/{id}`); `LiveCheckPanel` prüft beim Tippen gegen dieselben Zielbereiche wie die Kacheln.

**Protokoll.** Jede Zeile wird gegen die Sollwerte **ihrer eigenen Phase** beurteilt — fünf `AssessmentVerdict`-Fälle, die als „im Ziel", „über dem Ziel", „unter dem Ziel", der blanke Grund (kein Ziel) und „den Wert kann es nicht geben" erscheinen; darüber die Bilanz. Ab 1000 px Tabelle, darunter Zeitachse.

**Tagebuch.** Alles eines Grows nach Tagen, neueste oben (`GET /api/grows/{id}/tagebuch?bis=&tage=7`, `TagebuchService`): Messungen als Zellen mit Herkunft „Sensor"/„von Hand" (Handmessung gegen den Sensor ±10 min abgeglichen), Wasserwechsel als Vorgang (Changeout + Messung mit Lösungswechsel-Haken + Journal „Wasserwechsel" innerhalb einer Stunde = eine Zeile mit vorher | nachher | Änderung), Nachfüllen/Addback, Dosierungen, Journal mit Bearbeiten/Entfernen, Fotos, gebuchter Verbrauch. Tag und Uhrzeit rechnet der Server in Ortszeit. Je Tag „▸ Sensorkurven" (beim Öffnen immer zu, nicht gespeichert; `GET …/tagebuch/kurven?tag=`): EC, pH, Wasser, Luft, Feuchte, VPD mit Min–Max, Licht aus den Schaltflanken (sonst Lichtplan), Ereignis-Striche. Älter als die Rohwerte (7 Tage) gibt es nur Tageswerte — die Seite sagt das.

**Auffällig.** Sprünge im Becken (EC, pH, Wasserstand) ohne passenden Eintrag (`Sprungerkennung`): zwei ruhige Abschnitte von je 45 min, dazwischen höchstens 60 min Übergang. Gemerkt in `ForkTagebuchAuffaelligkeiten` beim Öffnen und jede Nacht vor dem Aufräumen der Rohwerte. „Nachfüllen eintragen", „Notiz dazu", „War nichts" (`PUT /api/tagebuch/auffaelligkeiten/{id}`).

## Die Zahlen und woher sie kommen

| Zahl | Wert | Woher |
|---|---|---|
| Live-Seite lädt neu | 30 s | `LiveDashboardPage.tsx` |
| Werte fremder HA-Entitäten | 30 s | `useTentDashboard.ts` |
| 24-h-Kurven je Messwert | alle 5 min, `days=1&resolution=raw` | `useTentSparklines.ts` |
| Kamerabild | 1 s nach Abschluss des vorigen Abrufs | `CameraPanel.tsx` |
| Handwert gilt als veraltet | 36 h | `live-model.ts` (`handVeraltetAbMinuten`) — Messroutine läuft auf 24 h, ein halber Tag Luft |
| Score-Abzug knapp daneben / weiter als eine Zielbreite | 10 / 20 | `live-model.ts` `buildScore` |
| Score-Abzug je fehlendem Wert unter sechs | 8 | ebenda |
| Score-Schwellen | < 55 kritisch, < 82 beobachten | ebenda |
| pH-Komfortzone im Protokoll | 5,8–6,2 | `DeviationAnalyzerService.PhComfortMin/Max` |
| Wassertemperatur Arbeitsbereich | 17–22 °C, kritisch < 14 / > 24; die Kachel zeigt seit beta.61 denselben Bereich wie Protokoll und Diagnose, nicht mehr das Tag/Nacht-Paar des Profils | `Wasserband.Grenzen`, SOP-RDWC-CAN-N1 (`wwwroot/knowledge-defaults/guidance/water-temperature-band.json`) |
| Gelöster Sauerstoff | 6,5 mg/l | `DeviationAnalyzerService.DoActionThreshold`, SOP-RDWC-CAN-N1 §2.2 |
| Trocknung: Temperatur / Feuchte | 18–20 °C / 55–60 % | `MoldGuard.cs` |
| CO₂ ohne Anreicherung | Hinweis „~400–500 ppm normal", Umgebungsluft ~420 ppm | `GrowDashboardComposer.cs`, `MeasurementAssessmentService.cs` |
| Physikalische Grenzen | pH 0–14 · EC 0–10 · Wasser −5…60 °C · Luft −20…60 °C · rF 0–100 % · DO 0–20 · ORP ±1000 · CO₂ 0–30000 · PPFD 0–3000 · VPD 0–20 · Luftstrom 0–300 | `MeasurementSanityService.PhysikalischeGrenzen` — die Wahrheit, sperrt beim Speichern |
| Dieselben Grenzen beim Tippen | nur 8 der 11 Größen (ohne PPFD, VPD, Luftstrom) | `live-check-model.ts` (`PHYSIK`) — **abgetippt, kein Test hält die beiden Tabellen gleich** |
| „Außerhalb des Laufs" | vor Start −1 Tag oder nach jetzt +1 h | `MeasurementAssessmentService.Assess` |

### Tagebuch
| Zahl | Wert | Herkunft |
|---|---|---|
| EC-Sprung | ab 5 % des Werts davor, mind. 0,05 | Faustregel: so viel verdünnt 1/20 des Beckens Wasser (`Sprungerkennung.EcAnteil`) |
| pH-Sprung | ab 0,2 | halbe Breite des Komfortbands 5,8–6,2 (`DeviationAnalyzerService.PhComfort*`) |
| Wasserstand-Sprung | ab 5 % | dieselbe 1/20-Regel |
| Erklärendes Fenster | 1 h davor bis 3 h danach | Faustregel: eingetragen wird nach der Arbeit (Brus Wechsel 04.10.: Sensor 16:44, Eintrag 17:30) |
| Abgleich Hand/Sensor | ±0,1 pH, ±0,1 EC, ±1 °C | Herstellerangabe Bluelab Guardian Monitor |

## Was es bewusst NICHT tut

- **Keine Note je Messung, keine für den Grow.** Es gibt keine Quelle dafür, wie pH gegen EC gegen VPD zu gewichten wäre (`MeasurementAssessmentService`).
- **Kein Trend, keine Vorhersage im Protokoll.** Regeln über die Veränderung — pH-Drift, ORP-Zerfall, EC-Sprung — wohnen im `SolutionStabilityAnalyzer` und erscheinen in der Diagnose, nicht hier.
- **pH nicht gegen den Phasenwert.** Der ist Anmischziel, keine Schwelle; im RDWC darf der pH in der Komfortzone wandern. Eine selbst gesetzte Grenze gilt dagegen allein.
- **Luft und Feuchte ohne eigenes Zielband im Protokoll.** Live werden sie aus dem VPD-Ziel zurückgerechnet — rückwirkend sinnlos, das Urteil trägt das VPD.
- **Zurückgerechnete Bänder zählen nicht in den Score.** Sonst würde ein Klimaproblem dreimal abgezogen; auf der Kachel steht die Bewertung trotzdem.
- **Nachts kein Ziel** für PPFD, CO₂ und VPD (`LightClock.DaytimeOnlyKeys`). Unbekannter Lichtzustand verhält sich wie Tag.
- **„Unmöglich" ist kein Urteil über den Anbau,** sondern über die Messung: eigener Zähler, fließt nicht in die Bilanz. Ein Zeitstempel aus der Zukunft gilt nicht als frisch (`AlterInMinuten` gibt `null`).
- **Keine Kamera-Kachel** im Anpassen-Modus — sie hat ihre eigene Bühne; der Server filtert sie beim Speichern heraus. Ein leeres Layout liest er als „nicht angepasst" und liefert den Standard zurück (`DashboardLayoutRepository.GetSaved`), deshalb warnt das Speichern vorher.
- **Kein Zahlenfeld für den Wasserfluss:** die Quelle sagt „moderat, nicht stark" und nennt keinen Durchsatz (`MeasurementDraft.waterFlow`).
- **Keine KI.** Die Beurteilung ist Regelwerk gegen Profil und Wissensbasis.

## Im Code

Alle Frontend-Pfade unter `GrowDiary.React/src/`, alle Backend-Pfade unter `GrowDiary.Web/`.

| Aufgabe | Datei |
|---|---|
| Daten holen, Anpassen-Entwurf, 30-s-Takt | `pages/LiveDashboardPage.tsx` |
| Bildschirm zeichnen (Kopf, Bänder, Kühler, „Heute fällig", Zeitachse) | `features/live/LiveScreen.tsx` |
| Score, Herkunft/Alter, Banddefinition | `features/live/live-model.ts` |
| Kachel-Geometrie, Zielband, Nachkommastellen | `features/live/metric-tile-model.ts` |
| Anpassen-Modus, Kamerabühne | `features/live/dashboard-layout.ts`, `DashboardEditor.tsx`, `CameraPanel.tsx` |
| Messformular + Prüfung beim Tippen | `pages/ManualMeasurementPage.tsx`, `features/measurement/live-check-model.ts` |
| Protokoll-Tabelle und Urteils-Anzeige | `features/grow-detail/GrowDetailMeasurementsSection.tsx`, `mess-urteil.ts` |
| Kacheln bauen, Ziele auflösen, Nacht/Trocknung | `Services/GrowDashboardComposer.cs` |
| Messungen beurteilen | `Services/MeasurementAssessmentService.cs` |
| Physik-Grenzen, Speicher-Sperre, Wasserband | `Services/MeasurementSanityService.cs`, `Services/Wasserband.cs` |
| Live-Endpunkt, Kamera, Kühlerlage | `Controllers/TentsController.cs` (`Live`) |
| Messungen + Beurteilung als API, eigene Anordnung | `Api/Controllers/MeasurementsApiController.cs`, `Api/Controllers/DashboardApiController.cs` |

## Fallen

- **Der Kachel-Klick war in der Standardansicht tot.** Er kam in beta.38 und war nur in `DashboardBands` verdrahtet — also in der Ansicht mit eigener Anordnung (CHANGELOG beta.43). Gehalten von `features/live/schutzregeln.test.ts`, das beide Dateien auf `onOpen=` prüft.
- **Der Speichern-Knopf tat nichts.** `V1Button` steht auf `type="button"` und schickt kein Formular ab. Nur ein echter Klick zeigt das; `e2e/formular-rundweg.spec.ts` zählt seitdem über alle `<form onSubmit>`.
- **21 Felder verloren Werte lautlos:** „6,2x" speicherte eine Messung ohne pH, mit Erfolgsmeldung — `parseNullableNumber` gibt für „leer" und „unlesbar" dasselbe zurück.
- **„Messungen" trug ein zweites, verkrüppeltes Messformular** — neun Felder gegen 31 auf `/messung`, ohne Live-Prüfung, Foto und Addback (CHANGELOG beta.50). Die 31 sind alle Felder, nicht nur die 21 Zahlenfelder.
- **−500 ppm CO₂ standen wochenlang auf einer Kachel.** Die Kachel-Schlüssel heißen anders als die Physik-Tabelle (`temperature` gegen `air-temp`), und unbekannte Größen gelten dort absichtlich als plausibel — ohne `PhysikSchluessel` prüfte die Sperre bei fünf von zehn Größen nichts und war grün.
- **Eine Zeile mit Datum 2099 galt als „gerade eben"** und verdrängte die echte letzte Messung. Und „Hand" stand über Werten der Automatik, samt „nachmessen?" (bis beta.49).
- **Der Score zeigte 100, während vier Werte daneben lagen:** er zählte ein `tone`-Feld, das der Server für Messwerte nie setzt. Später stand „0 /100" im Ring neben „Nicht bewertet".
- **Die Grid-Falle:** `min-width: auto` am Grid-Item ließ die Phasen-Zeitachse die Live-Seite aufziehen, VPD lag außerhalb des Schirms. Gehalten von `features/live/schutzregeln.test.ts` — als Quelltext-Test, weil die E2E-Mappe ohne Backend auf `/` nur einen Ladezustand sieht.
