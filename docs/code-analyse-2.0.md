# Code-Analyse vor 2.0.0

Durchgang über die 21 Commits des UI-Umbaus. Gesucht wurde nicht, was funktioniert,
sondern was daran nicht stimmt. Neun Befunde, nach Wirkung sortiert.

---

## 1. Drei Weichen, die nichts trennen — und eine davon steuert jetzt die Anzeige

**Wirkung: toter Verzweigungscode · sichtbare Folge harmlos, aber ungetestet**

`Tent.ActiveGrows` wurde nirgends befüllt; ich habe das in `TentsController` und
`HomeController` behoben. Damit ist zum ersten Mal auch

```csharp
var hasActiveHydro = tent.ActiveGrows.Any(g => g.IrrigationType == IrrigationType.ActiveHydro);
```

wahr, und daran hängen fünf Kacheln: pH, EC, ORP, DO, Wassertemperatur erscheinen
jetzt bei jedem Zelt mit laufendem Grow — auch ohne gemappten Sensor und ohne
Messung, dann mit „–".

**Erste Einschätzung war falsch.** Ich hatte notiert, Erd-Grower sähen nun fünf
leere Kacheln, weil `ParseEnum(..., IrrigationType.ActiveHydro)` auf Aktiv-Hydro
zurückfällt. Es gibt keine Erd-Grows:

```csharp
public enum IrrigationType { ActiveHydro }        // ein einziger Wert
public enum MediumType     { Hydro }              // ein einziger Wert
public bool IsHydro => true;                      // fest verdrahtet
```

Die App ist RDWC/DWC-only, so gebaut. Die Kacheln erscheinen also für genau die
Grows, für die sie gedacht sind — das ist die beabsichtigte Wirkung, kein
Rückschritt.

**Der eigentliche Befund ist ein anderer:** drei Weichen, die keine sind. Auf
ihnen verzweigen drei Dienste, als würden sie unterscheiden:

```csharp
if (grow.IrrigationType != IrrigationType.ActiveHydro || !grow.Profile.IsHydro) return;   // Deviation
if (latest is not null && grow.IrrigationType == ... && grow.Profile.IsHydro) { ... }     // Alerts
var hasActiveHydro = tent.ActiveGrows.Any(g => g.IrrigationType == ...);                  // Composer
```

Jede dieser Bedingungen ist konstant. Das ist keine Verzweigung, sondern eine
Behauptung, die aussieht wie eine. Wer eines Tages Erde ergänzt, wird von drei
Stellen begrüßt, die den Fall zu behandeln scheinen und es nicht tun.

**Empfehlung:** entweder die Aufzählungen um ihre fehlenden Werte ergänzen und
`IsHydro` echt ableiten — oder die toten Bedingungen entfernen und im Modell
festhalten, dass Cultivate Control Hydro-only ist. Beides ist besser als der Schwebezustand.

**Ungetestet bleibt** in jedem Fall, welche Kacheln bei welcher Konstellation
erscheinen. Meine vier Composer-Tests prüfen Zielbereiche; der eine, der eine
Reservoir-Kachel findet, findet sie über die Messung, nicht über `hasActiveHydro`.

## 2. Dieselbe Tabelle zweimal, und sie ist schon auseinandergelaufen

**Wirkung: derselbe Wert sieht auf zwei Seiten anders aus**

Nachkommastellen je Messwert stehen doppelt:

| Messwert | `DesktopLiveDashboard.decimalsFor` | `TentDetailPage.metricDecimals` |
|---|---|---|
| VPD | 2 | **0** |
| Lufttemperatur | 1 | **0** |

VPD 0,92 kPa steht auf der Live-Seite als „0,92", auf der Zelt-Detailseite als
„1". Ich habe die zweite Fassung eine Stunde nach der ersten geschrieben und die
beiden Zeilen vergessen — exakt das Muster, das in diesem Projekt schon dreimal
denselben Fehler erzeugt hat.

**Gehört** neben `metricScale`/`targetLabel` in `metric-tile-model.ts`, wo die
übrige Anzeigelogik des Messwerts steht.

---

## 3. Die Kamera-Vorschaubilder laden Vollbilder

**Wirkung: dreifache Last auf dem Pi, bei jedem Rendern**

`CameraStage` fordert Vorschaubilder so an:

```tsx
src={`/api/live/tents/${tentId}/camera?entity=...&thumb=1`}
```

`CameraProxyController.GetTentCamera` kennt keinen `thumb`-Parameter — er wird
ignoriert. Bei drei Kameras lädt die Live-Seite also das große Bild **plus drei
weitere Vollbilder**. Auf einem Raspberry Pi mit drei Kameras ist das genau die
Art von Last, die man nicht bemerkt, bis das Bild ruckelt.

**Zwei Wege:** den Parameter im Proxy umsetzen (skalieren, cachen), oder auf
Vorschaubilder verzichten und den Streifen aus Beschriftungen bauen. Der zweite
ist ehrlicher, solange der erste nicht gebaut ist.

---

## 4. Eine neue Spalte ohne Test

**Wirkung: Datenverlust bliebe unbemerkt**

`HarvestEntries.PlantWeightsJson` wird geschrieben (Insert + Update), gelesen
(`ColumnOrNull`) und über zwei DTO-Schichten gereicht — und kein einziger Test
fährt diesen Weg. Ein vergessener Parameter im Update-Zweig hätte zur Folge, dass
Einzelgewichte beim zweiten Speichern verschwinden, und nichts würde es melden.

Die 13 Frontend-Tests prüfen nur das Rechenmodell, nicht die Speicherung.

---

## 5. Ein Typ-Bruch, der eine echte Annahme verdeckt

```tsx
<LiveCheckPanel draft={draft as unknown as Record<string, string>} ... />
```

Der Entwurf enthält `solutionChange: boolean`. `checkDraft` ruft auf jedem Feld
`.trim()` auf — das hält nur, weil `FIELD_TO_METRIC` zufällig ausschließlich
Zeichenketten-Felder auflistet. Wer dort ein boolesches Feld ergänzt, bekommt
einen Laufzeitfehler, den der Compiler hätte verhindern können.

**Gehört** als eigener Typ ausgedrückt: `checkDraft` nimmt `Record<string, string>`
und die Seite reicht eine Projektion, keine Umdeutung.

---

## 6. Die Erntemaske lädt nacheinander, was nebeneinander ginge

```ts
const nextHarvest = await apiFetch(`/api/grows/${growId}/harvest`)
...
const grow = await apiFetch(`/api/grows/${growId}`)
```

Zwei Rundreisen hintereinander, obwohl die zweite nicht von der ersten abhängt.
Auf dem Pi über WLAN sind das gut zwei Sekunden statt einer. Die übrigen Seiten
in diesem Projekt benutzen dafür `Promise.all` — hier ist es mir durchgerutscht.

---

## 7. Drei fast gleiche Vokabulare für Schweregrade

```
MetricStatus  = 'ok' | 'warn' | 'crit' | 'unknown'   (Live)
CheckSeverity = 'ok' | 'warn' | 'crit'               (Messen)
PlanSeverity  = 'ok' | 'warn' | 'crit'               (Grow anlegen)
```

Dazu `DeviationSeverity` und `RiskEventSeverity` mit `'Info' | 'Warning' | 'Critical'`
aus dem Backend. Fünf Schreibweisen für dieselbe Sache. Kein Fehler, aber jede
Umrechnung dazwischen ist eine Gelegenheit für einen.

**Vorschlag:** ein geteilter Typ in `types/shared.ts` für die Frontend-Seite, und
genau eine Übersetzungsfunktion an der Backend-Grenze.

---

## 8. Zwölf Exporte, die niemand benutzt

Unter anderem `detailSections`, `emptyAutoConfigForm`, `emptyMappingDraft`,
`toNullableInteger` (alle aus `grow-detail-model.ts`, Reste des aufgelösten
Automatik-Editors), `circleDiameterForLiters`, `squareSideForLiters`,
`entityLabel`, `emptySheet` — und `TYPICAL_DRY_RATIO`, das ich selbst exportiert,
aber nur intern benutzt habe.

Exportiert heißt „gehört zur Schnittstelle". Was niemand ruft, sollte entweder
nicht exportiert sein oder nicht existieren.

---

## 9. 53 × `!important` — die meisten sind seit der Schichtung überflüssig

| Datei | Anzahl |
|---|---|
| `styles/primitives-rc2.css` | 18 |
| `styles/conventions.css` | 14 |
| `styles/widgets.css` | 11 |
| Rest (grows, hydro, measurement, tents, primitives) | 10 |

Sie stammen aus der Zeit, als `rc2-overrides` gegen ein unsortiertes Fundament
ankämpfen musste. Seit `@layer tokens, primitives, features` und der bewusst
ungeschichteten Konventionsdatei gewinnt die Reihenfolge ohnehin — jedes
`!important` ist jetzt ein Verdacht, kein Werkzeug.

**Vorgehen:** einzeln entfernen, nach jedem Block der Vergleich der berechneten
Werte (`e2e/computed-snapshot.spec.ts`). Kein Pauschal-Lauf.

---

## Was in Ordnung ist

Damit das Bild stimmt — geprüft und ohne Befund:

- **Kein Stylesheet ohne Importeur**, keine undefinierte CSS-Variable ohne
  Fallback (beides durch `css-variables.node.test.ts` abgesichert).
- **Keine Karte mit zwei Primärbuttons**, über 18 Routen geprüft.
- **44 px Touch-Ziele** auf acht Routen, gemessen an der Trefferfläche.
- **Berechnete Werte unverändert** über alle sieben CSS-Verschiebungen: 0
  Unterschiede bei 799 Elementen.
- **Die vier neuen Rechenmodelle** (Kachel-Skala, Live-Prüfung, Grow-Plan,
  Erntegewichte) sind rein, ohne React-Abhängigkeit, und decken ihre Randfälle
  ab — halboffene Zielbereiche, kaputtes JSON, leere Felder, Komma als
  Dezimaltrenner.

---

## Reihenfolge zum Beheben

1. **Befund 1** — der Standardwert `ActiveHydro` und ein Test für die
   Kachel-Sichtbarkeit. Betrifft jeden Nutzer.
2. **Befund 2** — Nachkommastellen zusammenführen. Zwei Zeilen, verhindert einen
   sichtbaren Widerspruch.
3. **Befund 3** — Vorschaubilder. Last auf dem Zielgerät.
4. **Befund 4** — Test für die Ernte-Spalte.
5. **Befund 5, 6** — Typ und paralleles Laden.
6. **Befund 7, 8, 9** — Aufräumen, ohne Eile, mit dem Vergleichsnetz.

---

## Stand nach dem Durchgang

**Behoben:**

- **2** — eine Tabelle in `metric-tile-model.decimalsForMetric`, beide Aufrufer
  darauf umgestellt. VPD steht jetzt überall gleich.
- **3** — der Streifen zeigt Nummer und Beschriftung statt eines Vollbilds. Der
  Kommentar sagt, wann das Bild zurückdarf: sobald der Proxy skaliert liefert.
- **4** — drei Tests für `PlantWeightsJson`: Hin- und Rückweg, **zweites**
  Speichern (dort läge ein vergessener Update-Parameter), und ein Eintrag ohne
  Einzelgewichte.
- **5** — `LiveCheckPanel` nimmt `Record<string, unknown>` und filtert selbst auf
  Zeichenketten. Die Umdeutung an der Aufrufstelle ist weg.
- **6** — Ernte lädt Ernteeintrag und Grow mit `Promise.all`.
- **8** — sieben Reste des 2026-07-23 aufgelösten Automatik-Editors entfernt
  (`grow-detail-model.ts` 175 → 155 Zeilen), `TYPICAL_DRY_RATIO` nicht mehr
  exportiert.
- **1, teilweise** — zwei Tests halten fest, welche Kacheln bei laufendem Grow
  erscheinen und welche bei leerem Zelt ausbleiben. Die toten Weichen selbst
  bleiben stehen; sie zu entfernen ist eine Produktentscheidung, keine Korrektur.

**Offen, bewusst:**

- **1** — die drei konstanten Bedingungen (`IrrigationType`, `MediumType`,
  `IsHydro`). Entweder Erde wird eines Tages ergänzt, dann bekommen sie Inhalt,
  oder Cultivate Control bleibt Hydro-only, dann können sie weg. Beides ist deine
  Entscheidung, nicht meine.
- **7** — die drei Schweregrad-Vokabulare. Zusammenlegen berührt drei Modelle und
  ihre Tests; das gehört nicht in denselben Durchgang wie ein Release.
- **9** — die 53 `!important`. Einzeln, mit dem Wertevergleich nach jedem Block.

**Nach den Korrekturen:** 719 Backend · 96 Vitest · 107 Playwright, alles grün.
