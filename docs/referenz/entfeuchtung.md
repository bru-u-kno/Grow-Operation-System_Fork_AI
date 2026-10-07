# Entfeuchtung — beide Entfeuchter, gleich aufgebaut

> Fork AI (A-014, 07.10.2026). Der Entfeuchter und der Zusatz-Entfeuchter haben dieselben vier Reiter, farbige
> Zonen und eine Erklärung, wie sie zusammenarbeiten. Jede Kachel lässt sich ein- und ausklappen.

## Wo in der App

`/steuerung/entfeuchter` und `/steuerung/entfeuchter-zusatz` — Menü **Betrieb → Entfeuchtung**. Oben wechseln die Chips
zwischen den beiden Seiten (und den anderen Steuerungen). Beide Seiten haben dieselben Reiter:

| Reiter | Inhalt |
|---|---|
| **Überblick** | Lagemeldung (im Ziel, knapp daneben, deutlich daneben), Messwerte mit Farbzonen, „Warum ist er gerade AN/AUS?", „So arbeiten die beiden Entfeuchter zusammen" |
| **Regel** | Was schaltet: Regelart (nach VPD oder feste Schwellen), Abstand EIN → AUS (Toleranz), beim Zusatz die Hilfsstärke und der VPD-Abstand |
| **Schutz** | Höchsttemperatur Tag/Nacht (gilt für beide), Abstände „früher aus / später wieder an", Mindestlaufzeit und -pause, Meldung „zieht nichts" |
| **Betrieb** | Automatik an/aus, Wartezeiten (Einschaltverzögerung, Außenluft zuerst, beim Zusatz „Zuschalten erst nach"), Ablauf des Kondenswassers, Energie heute |

## Was es tut

Zeigt, wie es um Luftfeuchte und Temperatur steht, erklärt in einfachen Sätzen, wann welcher Entfeuchter schaltet, und lässt
die Werte dafür einstellen. Gespeichert wird über die bekannten Wege der Steuerung (`/api/steuerung/entfeuchter` und
`/api/steuerung/entfeuchter-zusatz`); die Regelung selbst läuft in Home Assistant.

## Die drei Zonen

Der Messpunkt auf der Skala, die große Zahl und die Lagemeldung zeigen die Zone — immer auch als Wort, nie nur als Farbe:

- **im Ziel** (grün): Luftfeuchte bis zur Plan-Obergrenze; Temperatur bis eine Stufe unter der Höchsttemperatur
  (beim Zusatz bis zu seinem früheren Abschalten).
- **knapp daneben** (orange): Luftfeuchte bis 4 Prozentpunkte über dem Ziel; Temperatur bis zur Höchsttemperatur.
- **deutlich daneben** (rot): darüber. Über der Höchsttemperatur sind beide Entfeuchter aus.

## „Nach VPD regeln" und die festen Schwellen

Ist **Nach VPD regeln** an, wandern die Schwellen selbst mit Temperatur und VPD-Band aus dem Plan. Die vier **festen
Schwellen** (Tag/Nacht, EIN ab / AUS unter) haben dann keine Wirkung: Sie stehen blass und gesperrt da und gelten nur als
Rückfallebene. Den **Abstand EIN → AUS** („Wie ruhig schaltet er?") gibt es in beiden Regelarten.

## Die Zahlen und woher sie kommen

- **Ziel der Luftfeuchte:** die Plan-Obergrenze (Haupt) bzw. die Plan-Schwelle EIN (Zusatz); „knapp daneben" endet 4 Prozentpunkte darüber.
- **Höchsttemperatur:** Plan-Luft + Abstand oder fester Wert (Reiter Schutz) — dieselbe Zahl für beide Entfeuchter.
- **EIN/AUS-Schwellen, VPD-Ziel, Plan-Werte:** aus dem Plan und den Helfern in Home Assistant; die Seite zeigt sie nur.

## Was es bewusst NICHT tut

- Es schaltet nichts selbst — geschaltet wird in Home Assistant.
- Es gibt noch keine Vorrang-Einstellung „Feuchte vor Temperatur" (Aufgabe A-013).
- Der Verlauf der letzten Stunden steht im Überblick noch nicht.
- Die festen Schwellen wirken nicht, solange „Nach VPD regeln" an ist.

## Im Code

`GrowDiary.React/src/features/steuerung/`: `EntfeuchterDetail.tsx`, `EntfeuchterZusatzDetail.tsx`, `EntfeuchterUeberblick.tsx`
(Lage, Skala, „Warum", Zusammenspiel), `Klappkachel.tsx`, `entfeuchter-zonen.ts` (Zonen-Rechnung).
