# Grow-Domaene

## Produktfokus

Cultivate Control ist aktuell auf RDWC/DWC-Workflows ausgerichtet. Andere Medien wie Soil, Coco, NFT oder Aeroponic sollen erst dann in den normalen Flow, wenn die jeweiligen Datenmodelle und Workflows sauber ausgearbeitet sind.

Die App unterstuetzt den Grower, ersetzt aber kein Fachwissen und fuehrt keine gefaehrlichen automatischen Recovery-Aktionen ohne Nutzerkontrolle aus.

## Zelte

Zelte sind eigenstaendige Orte fuer Produktion, Mutterpflanzen, Quarantaene, Propagation oder Multi-Purpose-Nutzung. Sensoren sind flexibel ueber `TentSensors` gemappt, nicht ueber hartkodierte Entity-Felder.

Typische Sensor-Metriken:

- Lufttemperatur, Luftfeuchte, VPD, CO2, PPFD
- Lichtstatus
- Reservoir pH, EC, ORP, DO, Wassertemperatur, Wasserstand
- Pumpen, Chiller und UPS-Status

## Hydro-Setups

`GrowSystems` bilden die HydroSetup-Basis fuer echte DWC/RDWC-Systeme. Sie koennen mit Zelten verbunden sein und enthalten Layout-, Volumen-, Tank- und technische Basisdaten.

HydroSetups sind nicht dasselbe wie `Setups`: `Setups` beschreiben Plant-/Zeltkontexte wie Production, Mother, Quarantine oder Propagation.

## Grows

Grows dokumentieren konkrete Grow-Runs. Sie koennen mit Production-Setups, Zelten und HydroSetups verbunden sein und enthalten Status, Start-/Enddaten, Zielwerte, Snapshots und Verlauf.

Grow-Exports koennen Grows inklusive Messungen, Journal, Tasks, Addback, Changeouts und optional Foto-Metadaten transportieren. Importierte Grows sollen lokale Daten nicht ueberschreiben.

## Measurements

Measurements erfassen unter anderem:

- pH, EC, ORP, DO
- Reservoir-Temperatur und Wasserstand
- Lufttemperatur, Luftfeuchte, VPD
- PPFD und CO2
- PlantHeight, Stage und weitere Verlaufsdaten

Automatische Measurements koennen aus Home-Assistant-Sensordaten und LightTransitions entstehen. Harte Plausibilitaetsgrenzen sollen offensichtliche Sensorfehler blockieren.

## Addback und Changeout

Addback-Logs erfassen Korrekturen, Top-Offs und Addbacks inklusive Reservoirvolumen, EC/pH vorher und nachher, Zielwerten, Stock-Werten und zugegebenen Litern.

Changeout-Eintraege erfassen Teil- oder Komplettwechsel inklusive gewechselter Liter, Prozentanteil, EC/pH vorher und nachher sowie Notizen.

Bei RDWC/DWC ist die Volumen- und EC-Entwicklung ein zentraler fachlicher Zusammenhang.

## Home Assistant

Home Assistant ist die primaere Integrationsquelle fuer Sensor- und Statusdaten:

- Live Dashboard
- Tent-Detailansichten
- TentSensorReadings
- Tagesstatistiken
- AutoMeasurements
- LightTransitions
- Kamera-Snapshots

Der Grow-OS-Server muss Home Assistant erreichen koennen. Es reicht nicht, dass nur ein Handy oder Browser im selben Netz Zugriff hat.

## Licht und AutoMeasurements

LightSchedules dokumentieren Lichtplaene pro Zelt. LightTransitionEvents speichern erkannte LightOn-/LightOff-Ereignisse und dienen als Trigger- und Idempotenzgrundlage fuer AutoMeasurements.

`LightStatus` kann aus Home Assistant als `on/off`, `true/false`, `1/0` oder vergleichbaren States kommen, soweit der Code diese normalisiert.

## SOPs, Risiken und Hardware

SOPs koennen aus Knowledge-SOPs gestartet und als SopInstances mit materialisierten Steps abgearbeitet werden. RiskEvents koennen passende Emergency-SOPs vorschlagen und manuell starten.

HardwareItems bilden Inventar und Bezuege zu Zelt, Grow, HydroSetup, TentSensor oder Home Assistant Entity ab. MaintenanceEvents und CalibrationEvents dokumentieren Wartung und Kalibrierung. RiskEvents dokumentieren Ausfaelle, Risiken und manuelle Entscheidungen.

## Kamera, Fotos und Snapshots

Pro Zelt kann eine Kamera-Entity aus Home Assistant konfiguriert werden. Snapshots koennen lokal unter `App_Data/snapshots` landen. Uploads und Grow-Fotos sind private Daten und gehoeren nicht unbedacht ins Repository.

## Offene fachliche Punkte

- Feineres Rollen-/Rechtekonzept innerhalb der App (Auth/Remote laeuft ueber Home Assistant).
- Upload-/Foto-Speicher langfristig konsolidieren.
- SensorTrustScore und bessere Kalibrierungsintervalle.
- Home Assistant Auto-Detection fuer RiskEvents.
- Mehr Grow-Medien erst nach sauberer fachlicher Modellierung.
