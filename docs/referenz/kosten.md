# Kosten — Strom vom Zähler, Verbrauchsartikel von Hand

> Fork AI (forkai.6). Was der laufende Grow bis heute gekostet hat, woraus
> sich das zusammensetzt und wie lange eine CO₂-Flasche voraussichtlich hält.

## Wo in der App

`/kosten` — Menü **Betrieb → Kosten**. Aufbau (forkai.9):

- Kopf mit drei Knöpfen **Artikel anlegen · Nachfüllung erfassen · Anschaffung
  erfassen** — ein Tipp wechselt auf den passenden Reiter und öffnet dort das
  Formular; mehrere dürfen offen sein, ▴ schließt.
- Kachel Durchgang: Gesamtkosten, Aufteilung Strom/Verbrauch/Anschaffungen,
  Ø je Tag, Prognose bis zur Ernte; darunter die KPI-Leiste (Strom,
  Verbrauchsartikel, Anschaffungen, je Pflanze).
- Reiter (`?tab=strom|verbrauch|anschaffungen|durchgaenge`):
  - **Strom**: Leistung jetzt, kWh und Euro seit Grow-Start, je Phase eine
    Zeile; „Strom-Quelle einstellen“.
  - **Verbrauch**: Artikel-Karten (laufende Füllung, Prognose, Bearbeiten) und
    die Nachfüll-Historie.
  - **Anschaffungen**: Werkzeug/Technik/Zubehör mit Stück, Einzelpreis, Grow.
  - **Durchgänge**: dieselbe Rechnung für alle Grows, anklickbar.

Der Strompreis je kWh steht **nicht** hier, sondern in den Einstellungen
(`/einstellungen`, Kosten) — derselbe Wert, den das Archiv benutzt.

## Was es tut

**Strom.** Sobald eine Zähler-Entität gewählt ist, hält der
`ZaehlerstandWorker` den kWh-Stand fest: sofort beim Speichern der Quelle,
dann einmal am Tag, und sofort, wenn ein anderer Grow der laufende wird oder
der laufende Grow seine Phase wechselt (Klick **oder** Kalender — der Worker
vergleicht mit `GrowStageResolver`, nicht mit dem Speichern-Knopf). Die Seite
rechnet nur noch Differenzen zwischen Ständen.

**Verbrauchsartikel.** Ein Artikel ist etwas, das leer wird: Anzeigename,
Hersteller, Produktbezeichnung, Einheit (Auswahl: kg, g, L, ml, Stück),
Inhalt je Packung und Preis je Packung. Inhalt und Preis belegen die Erfassung
vor — jede Füllung darf davon abweichen. Eine Nachfüllung wird einem Grow
zugeordnet (Auswahl „Für Grow“: alle laufenden Grows oder **Lager**); nur
zugeordnete zählen in den Durchgang.

**Hersteller und Produkt (forkai.11).** Beide Felder schlagen beim Tippen
vor, was Artikel, Anschaffungen und die Hardware-Liste schon kennen; beim
Speichern gewinnt die vorhandene Schreibweise (Groß/Klein und Leerzeichen
werden ignoriert). Wer einen neuen Hersteller anlegt, legt damit auch seine
Schreibweise fest.

**Anschaffungen (forkai.9).** Was gekauft wird und bleibt: Name, Hersteller,
Produkt, Datum, Stück, Einzelpreis, Grow oder Lager, Notiz. Zählt einmal, hat
keine Laufzeit. Optional legt das Erfassen einen Hardware-Artikel (Kategorie
„Zubehör“) unter Sensoren & Wartung an und einen Journal-Eintrag im Grow. Eine **Nachfüllung** ist Datum, Menge, Kosten, Notiz, Grow. Beim
Erfassen ist „Vorherige Füllung damit als leer markieren“ vorbelegt — die neue
Flasche hängt ja dran, die alte nicht mehr. Aus dem Leer-Zeitpunkt entsteht
die **Laufzeit** der alten Füllung; daraus die Prognose für die neue. Auf
Wunsch (vorbelegt) entsteht ein Journal-Eintrag im Grow.

## Die Zahlen und woher sie kommen

| Zahl | Rechnung | Quelle |
|---|---|---|
| kWh seit Start | letzter Stand − erster Stand des Grows; ein Sprung nach unten gilt als Zähler-Reset und zählt ab null | `KostenSeiteService.KwhZwischen` |
| Strom in Euro | kWh × Preis (Einstellungen, Cent/kWh ÷ 100) | `GrowCostService.StrompreisCentProKwh` |
| kWh je Tag | kWh ÷ Tage zwischen erstem und letztem Stand; erst ab 12 h Abstand | `StromBerechnen` |
| Strom je Phase | Stände werden an jedem Phasenwechsel-Stand geschnitten; der Wechsel-Stand schließt die alte Phase ab und öffnet die neue | `PhasenBerechnen` |
| Laufzeit einer Füllung | Leer-Zeitpunkt − Füll-Zeitpunkt in Tagen | `Laufzeit` |
| Prognose „leer ≈“ | Mittel der letzten drei Laufzeiten **je Mengeneinheit** × Menge der laufenden Füllung — eine halbe Flasche hält halb so lang; Laufzeiten unter einem Tag zählen nicht (Fehlgriff) | `ArtikelBerechnen` |
| Füllstand % | 1 − vergangene Tage ÷ Prognose-Tage, zeitbasiert | `ArtikelBerechnen` |
| Euro je Tag (Artikel) | Kosten der Füllung ÷ Prognose-Tage (laufend) bzw. ÷ Laufzeit (abgeschlossen) | `ArtikelBerechnen` |
| Anschaffungen | Summe Stück × Einzelpreis der Positionen mit `GrowId` = Grow; Lager zählt nirgends | `Berechnen` |
| Gesamt | Strom + Füllungen + Anschaffungen des Grows | `Berechnen` |
| Je Tag / je Pflanze | Gesamt ÷ Tag im Grow bzw. ÷ `PlantCount` | `Berechnen` |
| Prognose Ernte | Gesamt + Resttage × Ø je Tag; Ernte = Flip + Züchter-Blütewochen (Mitte von min/max) | `Ernteprognose` |

Wo ein Wert fehlt, steht ein Strich und ein Satz, warum — kein Null-Euro.
Der Hinweis unter den Strom-Kacheln sagt, ob gemessen wurde („Gemessen am
kWh-Zähler“), ob der Zähler erst nach Grow-Start eingerichtet wurde (dann
fehlt der Strom davor) oder ob der Preis fehlt (dann nur kWh).

## Was es bewusst NICHT tut

- **Es wiegt nichts.** Der CO₂-Sensor misst Konzentration, nicht Verbrauch.
  „Noch 96 %“ heißt: nach den letzten Laufzeiten müsste die Flasche noch so
  lange halten — das steht so am Balken.
- **Es schätzt keinen Strom aus Watt.** Ohne Zählerstände gibt es keine
  Stromzahl; das Archiv rechnet weiter seine Untergrenze aus Lampen-Watt, die
  beiden Zahlen sind verschieden und heißen auch verschieden.
- **Es liest keinen HA-Helfer „Verbrauch seit Start“.** Ein Helfer kann von
  jedem zurückgesetzt werden; ein Gesamtzähler nicht. Deshalb eigene Stände.
- **Es dosiert und bestellt nichts.** Die Einkaufsliste (`/einkaufsliste`)
  bleibt, was sie ist; ein Artikel hier ist kein Posten dort.
- **Kein Auswahlmenü über alle HA-Entitäten.** Die Kennung wird getippt und
  mit „Prüfen“ gegen den Live-Wert gehalten — bei über tausend Entitäten
  ist das schneller als jede Liste.

## Im Code

- Modelle: `GrowDiary.Web/Models/Kosten.cs` — `Verbrauchsartikel`,
  `Nachfuellung`, `Zaehlerstand` (+ `ZaehlerAnlass`), `StromQuelle`.
- Tabellen: `GrowDiary.Web/Infrastructure/KostenRepository.cs` legt
  `ForkVerbrauchsartikel`, `ForkNachfuellungen`, `ForkZaehlerstaende`,
  `ForkAnschaffungen` selbst an
  (`CREATE TABLE IF NOT EXISTS`) — **nicht** im Kern-Schema, damit der
  Abgleich mit dem Original konfliktfrei bleibt.
- Rechnung: `GrowDiary.Web/Services/KostenSeiteService.cs` — statisch,
  ohne Datenbank, geprüft in `GrowDiary.Web.Tests/Services/KostenSeiteTests.cs`.
- Worker: `GrowDiary.Web/Services/ZaehlerstandWorker.cs` (Takt 10 min;
  `Entscheiden` ist statisch und getestet).
- API: `GrowDiary.Web/Api/Controllers/KostenApiController.cs` — `GET /api/kosten`,
  `PUT /api/kosten/strom-quelle`, `GET /api/kosten/entitaet`,
  `POST /api/kosten/zaehlerstand`, `…/artikel`, `…/nachfuellungen`,
  `POST …/nachfuellungen/{id}/leer`, `…/anschaffungen` (POST/PUT/DELETE).
- Oberfläche: `GrowDiary.React/src/pages/KostenPage.tsx`,
  `src/features/kosten/kosten-typen.ts`, `src/features/kosten/kosten.css`.
- Strom-Quelle liegt in `AppSettings` unter `fork-kosten-strom-quelle`.

## Fallen

- **Zähler erst nach Grow-Start eingerichtet:** der Strom davor fehlt und
  wird nicht geschätzt. Der Hinweis nennt das Datum des ersten Stands.
- **Zwei laufende Grows:** die Stände hängen am **ältesten** laufenden Grow.
  Wer zwei Zelte an einem Zähler hat, bekommt eine Summe, keine Aufteilung.
- **Phase aus dem Kalender:** der Worker sieht einen Phasenwechsel erst beim
  nächsten Takt (≤ 10 min). Die Phasenzeile beginnt mit diesem Stand, nicht
  mit Mitternacht.
- **„Als leer markieren“ ohne neue Füllung:** die Prognose des Artikels
  nutzt auch diese Laufzeit; ein zu früher Klick verkürzt künftige Prognosen.
  Über die Tabelle lässt sich die Füllung löschen und neu erfassen.
- **Datum ohne Zeitzone:** das Formular schickt ISO mit Offset; wer die API
  direkt füttert und eine nackte Ortszeit schickt, bekommt die Ortszeit des
  Add-ons unterstellt (`ZuUtc`).
