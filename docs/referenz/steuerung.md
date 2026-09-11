# Steuerung — der Leitstand der Regelungen

> Fork AI (forkai.20). Die Sollwerte der Regelungen liegen im Fork, geregelt
> wird in Home Assistant. Erste Steuerung: die CO₂-Begasung.

## Wo in der App

`/steuerung` — Menü **Betrieb → Steuerung**, in der Leisten-Vorgabe als
sechstes Ziel. Zwei Ebenen:

- **Übersicht** (`/steuerung`): je Regelung eine Zeile mit Status-Punkt
  (grün an, gelb gesperrt, grau aus), Kurzbeschreibung und aktuellem Wert.
- **Detail** (`/steuerung/co2`): oben eine Chip-Leiste zum Wechseln zwischen
  den Steuerungen, darunter die Statuskarte und fünf Reiter — **Ziel ·
  Dosierung · Klima · Zeiten · Heute**.

## Was es tut

Sie hält die Sollwerte der Regelungen und zeigt, was daraus wurde: das Livebild
aus Home Assistant, die Tageswerte der CO₂-Begasung und den Abschluss jedes
Tages in Chronik und Kosten. Verstellen und Speichern schreibt die Werte sofort
in die HA-Helfer.

## Die Arbeitsteilung

Geregelt wird in Home Assistant: `automation.co2_dosierung_rdwc_port_5` samt
Wächter (`automation.co2_wachter_rdwc_port_5`), Licht-aus-Sicherung und
Abluft-Drosselung. Ein Ventil an einer Gasflasche darf nicht davon abhängen,
ob ein Web-Add-on gerade neu startet — fällt der Fork aus, dosiert HA mit den
zuletzt geschriebenen Werten weiter.

Der Fork besitzt die **Sollwerte** und schreibt sie in die `co2_*`-Helfer.
Zurück liest er das Livebild und die Tageswerte. Speichern schreibt sofort;
zusätzlich schiebt der `Co2SyncWorker` sie einmal stündlich nach, weil bei
Ziel-Quelle *Plan* mit der Phase ein neues Ziel kommt, das niemand von Hand
einträgt.

## Ziel: fest oder aus dem Plan

Bei **fest** stehen drei ppm-Werte direkt in der Seite, gestaffelt nach
Canopy-Temperatur (unter 25 °C, 25–27 °C, ab 27 °C).

Bei **Plan** kommt ein Wert aus `Zielband` — der Untergrenze des CO₂-Bands der
laufenden Phase, dieselbe Quelle, aus der Kachel und Alarm ihr Band nehmen.
Die drei Stufen sind dann **Prozentanteile** davon (Vorgabe 55 / 70 / 80 %).

> **Das ist ein Phasenwert, kein Wochenwert.** Das Feedchart hat keine
> CO₂-Spalte je Woche; der Wert kommt aus dem Sollwertprofil und wechselt mit
> der Phase. Die Seite schreibt das auch so hin — sonst erwartet man eine
> wöchentliche Änderung, die es nicht gibt.

## Klima hat Vorrang

Dosiert wird nur, wenn `binary_sensor.co2_klima_ok` an ist: Feuchte unter der
Obergrenze (der Plan gibt sie je Blütewoche vor) und Canopy unter ihrer Grenze,
beides mit Hysterese gegen Flattern. Ist die Abluft-Drosselung an, muss der T6
außerdem wirklich auf Drosselstufe stehen — sonst bläst er das CO₂ hinaus,
während dosiert wird. Die tiefe Stufe greift nur bei kühler **und** trockener
Luft, weil sich sonst Feuchte staut, schneller als der Entfeuchter sie abführt.

## Zeiten

**Start nach Licht an** und **Ende vor Licht aus** gehen als Helfer
(`input_number.co2_start_nach_licht_an`, `…_ende_vor_licht_aus`) nach Home
Assistant; die Automation rechnet ihr Fenster daraus gegen die geplante
Aus-Zeit des Licht-Controllers. Vorher standen 15 Minuten und 16:30 Uhr fest in
der Automation — die Felder wären Attrappen gewesen.

Der Startversatz hat einen Grund: die Photosynthese erreicht erst nach etwa 15
bis 30 Minuten Licht ihre volle Rate. Früher zu dosieren pumpt Gas in Pflanzen,
die es noch nicht verwerten.

## Heute: Tageswerte, Chronik, Kosten

Der `Co2SyncWorker` legt je Lichttag einen Datensatz an, hält fest, wann das
Ziel erstmals erreicht wurde, und schließt ihn nach Licht-aus ab — auch, wenn
das Add-on den Abend verschlafen hat. Beim Abschluss entstehen wahlweise ein
Chronik-Eintrag und eine Verbrauchsbuchung auf einen Kosten-Artikel.

**Der Verbrauch kommt aus dem Fall von `co2_flasche_rest`.** Springt der Helfer
nach oben, wird das als Flaschenwechsel gelesen: das bis dahin Gezählte bleibt
stehen, ab dem neuen Stand läuft die Zählung weiter. Vorher fiel der ganze Tag
auf null, weil die Differenz negativ wurde.

Die **Ventilzeit** in der Tagesliste rechnet aus den Gramm zurück. Sie ist
dieselbe Zahl in anderer Einheit, keine zweite Messung — sie steht da, weil
Minuten anschaulicher sind als Gramm.

## Die Zahlen und woher sie kommen

| Zahl | Herkunft |
|---|---|
| CO₂ jetzt, Canopy, RH, VPD, T6-Stufe | Entitäten aus Home Assistant, ungerechnet |
| Ziel jetzt | `sensor.co2_ziel_effektiv` — HA wählt die Stufe nach Canopy |
| Planziel | `Zielband` → Untergrenze des CO₂-Bands der **Phase** |
| Die drei Stufen bei *Plan* | Planziel × Anteil, auf 10 ppm gerundet |
| Durchfluss g/s | kalibriert aus dem ppm-Anstieg je Impuls — **netto** nach Abluftverlust |
| Gramm je Tag | Fall von `input_number.co2_flasche_rest`, abschnittsweise über Flaschenwechsel hinweg |
| Ventilzeit | Gramm ÷ g/s — dieselbe Zahl in anderer Einheit, keine zweite Messung |
| Impulse heute | `counter.co2_impulse_heute`, zurückgesetzt beim Einschalten der LED |

## Was es bewusst NICHT tut

- **Nicht regeln.** Kein Impuls, kein Ventilbefehl, keine Wächterlogik im Fork.
  Das bleibt in Home Assistant und läuft weiter, wenn das Add-on neu startet.
- **Den Verbrauch nicht wiegen.** Die Gramm sind gerechnet. Eine Waage an der
  Flasche wäre die einzige zweite Quelle; ohne sie bleibt die Zahl eine
  Untergrenze und steht als solche da.
- **Das Ziel nicht selbst erfinden.** Bei *Plan* kommt es aus derselben Stelle
  wie Kachel und Alarm. Eine eigene Rechnung hier wäre die nächste abweichende
  Auskunft.
- **Keine Steuerung anlegen, die es nicht gibt.** Die Chip-Leiste zeigt, was
  `/api/steuerung` meldet; ein unbekannter Pfad führt nicht ersatzweise auf CO₂.

## Im Code

- Modelle: `GrowDiary.Web/Models/Steuerung.cs` — `Co2Einstellungen`, `Co2Tag`.
- Tabellen: `GrowDiary.Web/Infrastructure/SteuerungRepository.cs` legt
  `ForkSteuerungEinstellungen` (Modul + JSON, damit eine neue Steuerung keine
  Wanderung braucht) und `ForkCo2Tage` selbst an — **nicht** im Kern-Schema,
  damit der Abgleich mit dem Original konfliktfrei bleibt.
- Rechnung und Sollwert-Schreiben:
  `GrowDiary.Web/Services/Co2SteuerungService.cs`; die HA-Entitäten stehen als
  Konstanten in `Co2SteuerungService.Entitaeten`, damit ein Umbenennen einmal
  reicht. Geprüft in `GrowDiary.Web.Tests/Services/Co2SteuerungTests.cs`.
- Worker: `GrowDiary.Web/Services/Co2SyncWorker.cs` — Tageslauf alle 2 min,
  Sollwerte stündlich. Ohne einmal gespeicherte Einstellungen schreibt er
  nichts.
- API: `GrowDiary.Web/Api/Controllers/SteuerungApiController.cs` —
  `GET /api/steuerung`, `GET /api/steuerung/co2`, `PUT /api/steuerung/co2`.
- Oberfläche: `GrowDiary.React/src/pages/SteuerungPage.tsx`, Typen und Stil in
  `GrowDiary.React/src/features/steuerung/`.
- In Home Assistant: `automation.co2_dosierung_rdwc_port_5`,
  `automation.co2_wachter_rdwc_port_5`,
  `automation.co2_abluft_drosselung_t6_rdwc_port_1`,
  `automation.co2_dosierung_licht_aus_sicherung_rdwc_port_5`.

## Grenzen

- Der Durchfluss (`input_number.co2_gramm_pro_sekunde`) ist kalibriert, nicht
  gewogen: er stammt aus dem ppm-Anstieg je Impuls und ist damit ein
  **Netto**-Wert nach Abluftverlust. Für die Impulslänge ist das richtig, für
  den Flaschenverbrauch ist es eine Untergrenze.
- Eine neue Steuerung braucht eine Detailansicht; Übersicht und Chip-Leiste
  speisen sich aus der Liste, die `/api/steuerung` liefert.
