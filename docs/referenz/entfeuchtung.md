# Entfeuchtung — alle Entfeuchter auf einer Seite

> Fork AI (A-014, A-015, 08.10.2026). Hauptentfeuchter und Zusatz-Entfeuchter haben EINEN Eintrag „Entfeuchtung"
> mit den Reitern Überblick · Regel · Schutz · Einrichtung, Farbzonen und einklappbaren Kacheln.

## Wo in der App

`/steuerung/entfeuchtung` — Menü **Betrieb → Entfeuchtung**. Ganz oben steht die Auswahl der Steuerungen
(CO₂, Entfeuchtung, Chiller …), darunter die Überschrift. Die alten Adressen `/steuerung/entfeuchter` und
`/steuerung/entfeuchter-zusatz` führen hierher.

| Reiter | Inhalt |
|---|---|
| **Überblick** | Nur zum Lesen: Lagemeldung (im Ziel, knapp daneben, deutlich daneben), Messwerte und Zonen einmal fürs Zelt, die Geräte mit ihrem Status (läuft · aus · wartet · offline), „Warum läuft welches Gerät?", die Erklärung der Zusammenarbeit |
| **Regel** | Nach Regeln geordnet: „Wie ruhig schaltet er?", „Einschalten erst nach", „Auch tagsüber entfeuchten", „Automatik" — je Regel dieselben Felder für jedes Gerät. Davor steht „Wonach wird geregelt?" (Luftfeuchte oder VPD, EINE Einstellung für alle) und danach die festen Schwellen (für alle). Dann „Nur für …": beim Hauptentfeuchter Außenluft und Plan-Werte; beim Zusatz Hilfsstärke, Schaltgröße, Nachts durchlaufen |
| **Schutz** | Höchsttemperatur Tag/Nacht einmal für alle, Mindestlaufzeit je Gerät, beim Zusatz früher aus/später wieder an, Mindestpause, Meldung „zieht nichts" |
| **Einrichtung** | Wie viele Entfeuchter du hast (Zusatz an- oder abwählen, sofort gespeichert), wie sie arbeiten, Zuordnung der Geräte |

## Was es tut

Zeigt, wie es um Luftfeuchte und Temperatur steht, erklärt in einfachen Sätzen, wann welcher Entfeuchter schaltet, und lässt
die Werte dafür einstellen. Gespeichert wird über die bekannten Wege (`/api/steuerung/entfeuchter` und
`/api/steuerung/entfeuchter-zusatz`); ein Knopf „Speichern" ruft beide nacheinander auf. Die Regelung selbst läuft in
Home Assistant. Wie viele Entfeuchter es gibt, sagst du im Reiter **Einrichtung** (Zusatz-Entfeuchter an- oder abwählen; „Automatisch" schließt es aus der Zuordnung in „Geräte & Entitäten"). Ohne Zusatz steht nur der Hauptentfeuchter da, ohne Hilfsstärke und ohne zweite Zeile; in der Übersicht der Steuerungen ist es weiter EIN Eintrag „Entfeuchtung". Abwählen hält die Regelung des Zusatzes in Home Assistant nicht an.

## Die drei Zonen

Der Messpunkt auf der Skala, die große Zahl und die Lagemeldung zeigen die Zone — immer auch als Wort, nie nur als Farbe:

- **im Ziel** (grün): Luftfeuchte bis zur Plan-Obergrenze; Temperatur bis eine Stufe unter der Höchsttemperatur
  (mit Zusatz: bis zu dessen früherem Abschalten).
- **knapp daneben** (orange): Luftfeuchte bis 4 Prozentpunkte über dem Ziel; Temperatur bis zur Höchsttemperatur.
- **deutlich daneben** (rot): darüber. Über der Höchsttemperatur sind alle Entfeuchter aus.

## Regelgröße: Luftfeuchte oder VPD — für alle Entfeuchter gleich

Im Reiter Regel steht oben **Wonach wird geregelt?** mit zwei Wahlen. Sie gilt für Haupt- und Zusatz-Entfeuchter zugleich
(derselbe Schalter `input_boolean.trotec_vpd_regelung` in Home Assistant; ab Fassung 2 der Zusatz-Regelung folgt auch der Zusatz ihm).

- **Luftfeuchte** (Standard): Ziel ist die Plan-Obergrenze der Luftfeuchte. Es gibt EINEN Abstand EIN → AUS in Prozent für alle
  Geräte; kPa-Felder erscheinen nicht. Die vier **festen Schwellen** (Tag/Nacht) sind bedienbar; die Plan-Feuchte deckelt EIN.
- **VPD**: Die Schwellen wandern mit Temperatur und VPD-Band aus dem Plan. Dann gibt es den Abstand in % (Haupt) und in kPa (Zusatz);
  die festen Schwellen stehen blass und gesperrt als Rückfallebene.

## Die Zahlen und woher sie kommen

- **Ziel der Luftfeuchte:** die Plan-Obergrenze der Luftfeuchte (ohne Plan die Schwelle EIN); „knapp daneben" endet 4 Prozentpunkte darüber.
- **Höchsttemperatur:** Plan-Luft + Abstand oder fester Wert (Reiter Schutz) — dieselbe Zahl für alle Entfeuchter.
- **EIN/AUS-Schwellen, VPD-Ziel, Plan-Werte:** aus dem Plan und den Helfern in Home Assistant; die Seite zeigt sie nur.
- **Abstand „Wie ruhig":** bei Luftfeuchte ein Wert in Prozent für alle Geräte; nur bei VPD zusätzlich beim Zusatz in kPa (Stufen knapp, normal, ruhig).

## Was es bewusst NICHT tut

- Es schaltet nichts selbst — geschaltet wird in Home Assistant.
- „Getrennt" (jeder Entfeuchter regelt sich selbst) und mehr als zwei Entfeuchter gibt es noch nicht; sie brauchen eine zweite Regelvariante in Home Assistant (Aufgabe A-015, Stufen 2 und 3).
- Es gibt noch keine Vorrang-Einstellung „Feuchte vor Temperatur" (Aufgabe A-013).
- Der Verlauf der letzten Stunden steht im Überblick noch nicht.
- Die festen Schwellen wirken nicht, solange die Regelgröße VPD gewählt ist.

## Im Code

`GrowDiary.React/src/features/steuerung/`: `EntfeuchtungSeite.tsx` (Rahmen, Speichern), `EntfeuchtungUeberblick.tsx`,
`EntfeuchtungRegel.tsx`, `EntfeuchtungSchutz.tsx`, `EntfeuchtungEinrichtung.tsx`, `useEntfeuchterHaupt.ts` und
`useEntfeuchterZusatz.ts` (Zustand je Gerät), `entfeuchtung-modell.ts` (Zusammenfassen, Status), `entfeuchter-zonen.ts` (Zonen),
`Klappkachel.tsx`, `SteuerungWechsel.tsx` (Auswahl der Steuerungen).
