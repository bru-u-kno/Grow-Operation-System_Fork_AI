# Entfeuchtung — alle Entfeuchter auf einer Seite

> Fork AI (A-014, A-015, A-021). Hauptentfeuchter und Zusatz-Entfeuchter haben EINEN Eintrag „Entfeuchtung"
> mit den Reitern Überblick · Einstellungen · Einrichtung. Seit A-021 (13.10.2026) gibt es „Regel" und „Schutz"
> nicht mehr: ihre Werte stehen als wenige Antworten unter „Einstellungen", die Einzelwerte unter „Erweitert".

## Wo in der App

`/steuerung/entfeuchtung` — Menü **Betrieb → Entfeuchtung**. Ganz oben steht die Auswahl der Steuerungen
(CO₂, Entfeuchtung, Chiller …), darunter die Überschrift. Die alten Adressen `/steuerung/entfeuchter` und
`/steuerung/entfeuchter-zusatz` führen hierher.

| Reiter | Inhalt |
|---|---|
| **Überblick** | Nur zum Lesen: Lagemeldung, Plaketten für Licht und Woche, je Messwert die große Zahl mit dem Urteil in Worten, die ZIEL-Zeile, das Band mit zwei Seiten und „Woher kommt das?"; die Geräte mit ihrem Status; „Warum läuft welches Gerät?"; die Erklärung der Zusammenarbeit |
| **Einstellungen** | Betrieb · Ziel · Höchsttemperatur · „Wie reagieren die Entfeuchter?" · Zusatz-Hilfsstärke · Meldungen aufs Handy · Erweitert |
| **Einrichtung** | Wie viele Entfeuchter du hast (Zusatz an- oder abwählen, sofort gespeichert), wie sie arbeiten, Zuordnung der Geräte |

## Was es tut

Zeigt, wie es um Luftfeuchte und Temperatur steht, erklärt in einfachen Sätzen, wann welcher Entfeuchter schaltet, und lässt
die Werte dafür einstellen. Gespeichert wird über die bekannten Wege (`/api/steuerung/entfeuchter` und
`/api/steuerung/entfeuchter-zusatz`); ein Knopf „Speichern" ruft beide nacheinander auf (der Hauptentfeuchter schreibt alle
seine Helfer, der Zusatz nur, was sich geändert hat). Die Regelung selbst läuft in Home Assistant. Ohne Zusatz-Entfeuchter
steht nur der Hauptentfeuchter da; in der Übersicht der Steuerungen ist es weiter EIN Eintrag „Entfeuchtung".

## Überblick

- **Plaketten:** „☾ Nacht"/„☀ Tag" und „Woche …" einmal oben in der Karte — sie gelten für Luftfeuchte und Temperatur zugleich.
- **Urteil in Worten** neben der großen Zahl: „im Ziel", „knapp über dem Ziel", „deutlich unter dem Ziel", bei der Temperatur
  „über dem Plan" und über der Höchsttemperatur „zu warm — alle aus". Die Zone steht nie nur als Farbe da.
- **ZIEL-Zeile und Band** wie bei den Live-Kacheln: das grüne Ziel in der Mitte, gelbe Striche für die Grenzen, die Nadel für den
  Messwert; unter dem Band stehen nur die Grenzen. **Zu trocken und zu kühl zählen genauso als Abweichung** wie zu feucht und zu warm
  (Bru, 13.10.2026) — das Band hat zwei Seiten.
- **Woher kommt das?** Eine Zeile zum Aufklappen unter jedem Band; aufgeklappt eine Zeile je Quelle (Wert links, Herkunft rechts) und
  die Wege zum Ändern (Plan, Grenzwerte, „Ein-/Ausschalten einstellen", „Höchsttemperatur einstellen" — die beiden letzten springen in
  „Einstellungen" zur Kachel).
- Die Seite liest dafür zusätzlich `GET /api/zielwerte`. Antwortet der Server nicht, bleibt der Überblick ohne Plan- und
  Grenzwert-Angaben stehen und zeigt das Ziel aus den Entfeuchter-Daten (Plan-Obergrenze).

### Woher die Grenzen kommen

| Größe | Untergrenze | Obergrenze |
|---|---|---|
| **Luftfeuchte** | der Grenzwert für die Meldung aufs Handy (Seite Grenzwerte) — der Plan kennt keine Untergrenze | die Plan-Obergrenze („RH max"); ohne Plan die Einschalt-Schwelle |
| **Temperatur** | die Grenzen des Grenzwerts „Lufttemperatur" (folgt dem Plan: Plan-Luft ± Toleranz, Tag und Nacht getrennt) | dieselben; nie über der Höchsttemperatur |

Fehlt der Grenzwert, bleibt die Untergrenze leer, und das Band ist so einseitig wie vor A-021.

## Die drei Zonen

- **im Ziel** (grün): zwischen Untergrenze und Obergrenze.
- **knapp daneben** (orange): Luftfeuchte bis 4 Prozentpunkte außerhalb des Bandes (oben wie unten); Temperatur oben bis zur Höchsttemperatur,
  unten bis 2 Kelvin unter der Untergrenze.
- **deutlich daneben** (rot): weiter weg. Über der Höchsttemperatur sind alle Entfeuchter aus.

## Einstellungen

| Kachel | Was sie ändert |
|---|---|
| **Betrieb** | **Automatik** für beide Geräte mit einem Schalter; **Wann darf entfeuchtet werden?** („Tag und Nacht" / „Nur nachts") für beide. Stehen die zwei Geräte verschieden, steht „Gerade ist nur ein Gerät an" bzw. „unterschiedlich" — eine Wahl setzt beide. |
| **Ziel · Luftfeuchte** | Plan-Obergrenze (nur Ansicht), **Einschalten über** (ein Wert für Tag und Nacht; getrennt per Schalter unter „Erweitert"), **Ausschalten unter** = Einschalten minus Abstand (nur Ansicht), dazu ein kleines Band. Bei VPD-Regelung gelten die festen Werte nur als Rückfallebene. |
| **Höchsttemperatur** | Tag und Nacht getrennt, je „Plan +" oder „Fest". Darüber gehen alle Entfeuchter aus. |
| **Wie reagieren die Entfeuchter?** | Ruhig · Normal · Schnell — setzt Abstand EIN→AUS, „Einschalten nach" und die Mindestlaufzeit auf einmal; die Tabelle zeigt, was jede Stufe setzt. „Eigene Werte", wenn die Einzelwerte zu keiner Stufe passen. |
| **Zusatz-Entfeuchter · Hilfsstärke** | aus · sparsam · normal · kräftig — setzt auch „Zuschalten nach", Mindestpause, „früher aus" und „wieder an" des Zusatzes. |
| **Meldungen aufs Handy** | Nur zum Lesen: die Grenzwerte mit ihrer Herkunft und der Schalter „zieht nichts"; Links zu Grenzwerte und Handy. |
| **Erweitert** | Die Einzelwerte (Abstand, Einschalten nach, Mindestlaufzeit; beim Zusatz Zuschalten nach, Mindestpause, früher aus, wieder an) und die Schalter: Einschalt-Schwelle für Tag und Nacht getrennt, Zusatz läuft nachts durch, Nach VPD regeln, Meldung „zieht nichts". |

### Die Stufen

| Wert | Ruhig | Normal | Schnell |
|---|---|---|---|
| Abstand EIN → AUS | 6 % | 4 % | 2 % |
| Einschalten nach (Haupt) | 10 min | 2 min | 1 min |
| Mindestlaufzeit (Haupt / Zusatz) | 30 / 25 min | 20 / 15 min | 10 / 10 min |

**Normal** sind die Werte, mit denen die Regelung bei Bru am 12.10.2026 lief; **Ruhig** und **Schnell** sind ein Vorschlag und nicht gegen
den Betrieb geprüft (der Abstand folgt den früheren Stufen knapp 2 · normal 4 · ruhig 6). Die Stufe verschiebt die Ausschalt-Schwellen mit
(AUS = EIN − Abstand, nie unter 30 %) und hebt die Wartezeit bei trockener Außenluft an, wenn sie sonst kürzer als „Einschalten nach" wäre
(das verlangt das Backend). Sie rührt „Zuschalten nach" und „Mindestpause" des Zusatzes nicht an — die gehören seiner Hilfsstärke.

## Regelgröße: Luftfeuchte oder VPD — für alle Entfeuchter gleich

Unter „Erweitert" steht der Schalter **Nach VPD regeln statt nach Luftfeuchte** (derselbe Schalter `input_boolean.trotec_vpd_regelung` in
Home Assistant; der Zusatz folgt ihm). **Luftfeuchte** ist der Standard: Ziel ist die Plan-Obergrenze. Bei **VPD** wandern die Schwellen
mit Temperatur und VPD-Band aus dem Plan. Ob VPD sinnvoll ist, hängt vom Plan ab: ein VPD-Ziel von 1,6 kPa ist bei 21 °C erst bei rund
30 % Luftfeuchte erreicht — die Entfeuchter würden dann dauernd laufen (nachgerechnet am 13.10.2026). Eine eigene Wahl mit Erreichbarkeits-
prüfung ist noch nicht gebaut.

## Die Zahlen und woher sie kommen

- **Ziel der Luftfeuchte:** die Plan-Obergrenze (ohne Plan die Einschalt-Schwelle); die Untergrenze aus dem Grenzwert.
- **Höchsttemperatur:** Plan-Luft + Abstand oder fester Wert (Einstellungen) — dieselbe Zahl für alle Entfeuchter.
- **EIN/AUS-Schwellen, VPD-Ziel, Plan-Werte:** aus dem Plan und den Helfern in Home Assistant; der Fork zeigt sie und schreibt die festen Werte.
- **Wartezeit bei trockener Außenluft:** nicht mehr sichtbar (A-021), bleibt aber gespeichert und wächst mit „Einschalten nach".

## Was es bewusst NICHT tut

- Es schaltet nichts selbst — geschaltet wird in Home Assistant.
- „Getrennt" (jeder Entfeuchter regelt sich selbst) und mehr als zwei Entfeuchter gibt es noch nicht (A-015, Stufen 2 und 3).
- Es gibt noch keine Vorrang-Einstellung „Feuchte vor Temperatur" (A-013) und keinen Verlauf der letzten Stunden im Überblick.
- Der Arbeitsbereich folgt noch nicht dem Plan („Einschalten = Plan-Obergrenze − Vorlauf"): geplant für die nächste Fassung.
- Die Mindestpause gibt es weiter nur beim Zusatz; für den Hauptentfeuchter bräuchte sie eine Änderung an der Regelung in Home Assistant.

## Im Code

`GrowDiary.React/src/features/steuerung/`: `EntfeuchtungSeite.tsx` (Rahmen, Speichern, Sprung in „Einstellungen"),
`EntfeuchtungUeberblick.tsx` + `EntfeuchterUeberblick.tsx` (Bausteine: Band, ZIEL-Zeile, „Woher kommt das?"),
`EntfeuchtungEinstellungen.tsx`, `EntfeuchtungEinrichtung.tsx`, `useEntfeuchterHaupt.ts` und `useEntfeuchterZusatz.ts` (Zustand je Gerät),
`useZielwerteKurz.ts` (Plan und Grenzwerte), `entfeuchtung-modell.ts` (Zusammenfassen, Status, Reiter), `entfeuchtung-stufen.ts`
(Ruhig · Normal · Schnell), `entfeuchtung-schwellen.ts` (Einschalt-Schwelle und Abstand), `entfeuchtung-ziel.ts` (Zielband mit zwei Seiten,
Zonen, Urteil in Worten), `entfeuchter-zonen.ts`, `Klappkachel.tsx`, `SteuerungWechsel.tsx`.
Backend: `MeldungenApiController` führt die Meldung „Zusatz-Entfeuchter zieht nichts" jetzt in der Liste der Absender.
