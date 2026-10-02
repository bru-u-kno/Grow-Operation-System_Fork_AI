import { describe, expect, it } from 'vitest'
import { readFileSync, readdirSync } from 'node:fs'
import { feldText, istLeer, istUnlesbar, maschinenZahl, unlesbarMeldung, unlesbareFelder, zahlOderNull } from './zahlenfeld'

/**
 * Keine Seite baut sich ihre eigene Zahlen-Umwandlung.
 *
 * **Der Anlass.** Derselbe Fehler kam dreimal, in drei Fassungen:
 *
 * | Seite | Fassung | Folge |
 * |---|---|---|
 * | `ManualMeasurementPage` | `Number.isFinite`, meldet Unlesbares | 2026-08 behoben |
 * | `MeasurementEditPage` | `Number.isNaN` | „6,2x" wurde still zu `null` |
 * | `DosingPumpSetupPage` | **keine Leerprüfung** | leeres Feld wurde zu `0` |
 *
 * Der dritte war der teuerste: `Number('')` ist `0` und `Number.isFinite(0)`
 * ist `true`. Ein geleerter Mindestabstand wurde damit zur Null, und
 * `DosingService` prüft `seit < TimeSpan.FromMinutes(0)` — das ist nie wahr.
 * Die Pumpe hätte ohne jede Mischpause dosiert, still, mit Erfolgsmeldung.
 *
 * Behoben wurde jedes Mal nur die eine Seite, auf der es auffiel.
 */

const QUELLE = new URL('./', import.meta.url)

/**
 * **Null eigene Umwandlungen — seit dem 02.10.2026.**
 *
 * Bis dahin stand hier eine Ratsche (`HOECHSTENS`), die von 24 über 22 nur
 * sinken durfte. Am 02.10.2026 wurden die letzten 21 Stellen auf
 * `zahlenfeld.ts` umgeleitet, weil die Leseregel selbst sich änderte: „1.200"
 * heisst jetzt 1200 (Punkt = Tausendertrenner). Mit 21 eigenen Fassungen hätte
 * das an 21 Stellen NICHT gegolten — ein Preis „1.200" wäre auf einer Seite
 * 1200 € gewesen und auf der nächsten 1,20 €.
 *
 * Damit ist die Ratsche eine Zählung geworden: außer `zahlenfeld.ts` darf
 * KEINE Datei unter `src/` ein Komma selbst zum Punkt machen. Die eine
 * erlaubte Stelle muss die Suche finden — sonst sieht sie ihre Grundmenge
 * nicht, und null Treffer hiessen nichts.
 */
const ERLAUBT = 'zahlenfeld.ts'

/**
 * Das Kennzeichen: eine Komma-Ersetzung ergibt nur bei getipptem Text Sinn.
 *
 * <b>Die erste Fassung sah nur `Number(…)`.</b> Sie hing an `Number\(` und an
 * `[^)]*` — damit fielen `Number.parseFloat(x.replace(…))` und jede
 * Fassung mit einer inneren Klammer heraus. Zehn Stellen blieben unsichtbar,
 * zwei davon mit genau dem Fehler, gegen den diese Datei angetreten ist:
 * `num()` in `PhenoSheetEditor` und `StrainsPage` liefert für „6,2x" die 6,2
 * und meldet nichts.
 *
 * Gesucht wird die Komma-Ersetzung selbst — die ist das Kennzeichen, und sie
 * steht in jeder Fassung: `replace(',', '.')`, `replaceAll(",", ".")`,
 * `replace(/,/g, '.')`.
 */
const EIGENE_FASSUNG = /\.replace(?:All)?\(\s*(?:(['"`]),\1|\/\\?,\/[a-z]*)\s*,\s*(['"`])\.\2\s*\)/

/**
 * Kommentare entfernen — eine Erwähnung ist keine Verwendung.
 *
 * Block-Kommentare (auch über mehrere Zeilen und `{/* … *\/}` in JSX) und
 * Zeilen-Kommentare. Ein `//` zählt nur am Zeilenanfang oder nach Leerraum,
 * damit `https://…` in einer Zeichenkette stehen bleibt. Die Zeilenumbrüche
 * bleiben erhalten, damit die Zeilennummern der Fundstellen stimmen.
 */
function ohneKommentare(inhalt: string): string {
  return inhalt
    .replace(/\/\*[\s\S]*?\*\//g, (block) => block.replace(/[^\n]/g, ' '))
    .split('\n')
    .map((zeile) => zeile.replace(/(^|\s)\/\/.*$/, '$1'))
    .join('\n')
}
function alleQuellen(ordner = QUELLE, pfad = ''): string[] {
  const raus: string[] = []
  for (const eintrag of readdirSync(ordner, { withFileTypes: true })) {
    if (eintrag.name === 'node_modules') continue
    if (eintrag.isDirectory()) {
      raus.push(...alleQuellen(new URL(eintrag.name + '/', ordner), pfad + eintrag.name + '/'))
    } else if (eintrag.name.endsWith('.tsx') || eintrag.name.endsWith('.ts')) {
      // Prüfungen zählen nicht — sie dürfen die alte Schreibweise als Beispiel
      // tragen; diese Datei hier tut es selbst (und darf sich nicht mitlesen).
      if (eintrag.name.includes('.test.')) continue
      raus.push(pfad + eintrag.name)
    }
  }
  return raus
}

/** Alle Fundstellen mit Datei und Zeile — in einem gegebenen Quelltext. */
function fundstellen(name: string, inhalt: string): string[] {
  const treffer: string[] = []
  ohneKommentare(inhalt).split('\n').forEach((zeile, i) => {
    if (EIGENE_FASSUNG.test(zeile)) treffer.push(`${name}:${i + 1}  ${zeile.trim().slice(0, 90)}`)
  })
  return treffer
}

/** Alle Fundstellen unter `src/`, die eine erlaubte eingeschlossen. */
function eigeneFassungen(): string[] {
  return alleQuellen().flatMap((name) => fundstellen(name, readFileSync(new URL(name, QUELLE), 'utf8')))
}

describe('Zahlenfelder', () => {
  /* ---------------- Die geteilte Fassung selbst ---------------- */

  it('unterscheidet leer von unlesbar', () => {
    // Der ganze Sinn der Übung. Beides ergibt `null`, meint aber Verschiedenes:
    // „nicht gemessen" gegen „vertippt".
    expect(zahlOderNull('')).toBeNull()
    expect(zahlOderNull('   ')).toBeNull()
    expect(zahlOderNull('6,2x')).toBeNull()

    expect(istUnlesbar('')).toBe(false)
    expect(istUnlesbar('   ')).toBe(false)
    expect(istUnlesbar('6,2x')).toBe(true)
  })

  it('macht aus einem leeren Feld KEINE Null', () => {
    // Der Fehler in der Dosierpumpe: `Number('')` ist 0 und gilt als endlich.
    expect(zahlOderNull('')).not.toBe(0)
    expect(zahlOderNull('0')).toBe(0)   // eine getippte Null bleibt eine Null
  })

  it('lässt Unendlich nicht durch', () => {
    // `Number.isNaN` hätte hier ja gesagt — die Fassung der Bearbeiten-Seite.
    expect(zahlOderNull('Infinity')).toBeNull()
    expect(zahlOderNull('1e400')).toBeNull()
  })

  it('nimmt das deutsche Komma', () => {
    expect(zahlOderNull('6,2')).toBe(6.2)
    expect(zahlOderNull(' 6,2 ')).toBe(6.2)
    expect(istLeer(' ')).toBe(true)
  })

  it('nennt unlesbare Felder beim Namen', () => {
    const namen = unlesbareFelder([['6,2x', 'Reservoir-pH'], ['', 'EC'], ['1,2', 'Wassertemperatur']])
    expect(namen).toEqual(['Reservoir-pH'])

    const meldung = unlesbarMeldung(namen)
    expect(meldung).toContain('Reservoir-pH')
    // Die Meldung muss sagen, was sonst passiert — „ungültige Eingabe" allein
    // lässt offen, ob gespeichert wurde.
    expect(meldung).toContain('verloren')
    expect(unlesbarMeldung([])).toBeNull()
  })

  /* ---------------- Die Zählung ---------------- */

  it('sieht ihre Grundmenge überhaupt', () => {
    expect(alleQuellen().length,
      'Keine Quelldatei gefunden — dann liefe alles darunter null Mal durch.')
      .toBeGreaterThan(50)
    // Die Testdateien sind draussen — auch diese hier, die die alte
    // Schreibweise als Beispiel trägt.
    expect(alleQuellen().some((name) => name.includes('.test.'))).toBe(false)
  })

  it('findet die eine erlaubte Stelle (Mengenwächter)', () => {
    // Fände die Suche nicht einmal `zahlenfeld.ts`, wären null Treffer
    // anderswo kein Beleg — dann sähe sie schlicht nichts.
    const dort = eigeneFassungen().filter((t) => t.split(':')[0] === ERLAUBT)
    expect(dort.length,
      `Die Suche findet in ${ERLAUBT} keine Komma-Ersetzung mehr. Entweder wurde die `
      + 'Leseregel dort umgebaut (dann diesen Wächter an die neue Form anpassen), oder die '
      + 'Suche ist blind geworden.').toBeGreaterThanOrEqual(1)
  })

  it('keine Datei ausser zahlenfeld.ts liest Zahlen selbst', () => {
    const anderswo = eigeneFassungen().filter((t) => t.split(':')[0] !== ERLAUBT)

    expect(anderswo,
      'Eigene Zahlen-Umwandlung gefunden. Benutze `zahlOderNull` (getippter Text, deutsche '
      + 'Leseregel: „1.200" = 1200) oder `maschinenZahl` (Home-Assistant-Zustand, '
      + '<input type="number">) aus `src/zahlenfeld.ts`. Jede eigene Fassung liest „1.200" '
      + 'anders als der Rest der App — ein Preis wäre auf einer Seite 1200 € und auf der '
      + 'nächsten 1,20 €.').toEqual([])
  })

  /* ---------------- Dass die Zählung beisst ---------------- */

  it('erkennt jede bekannte Schreibweise der alten Fassung', () => {
    // Die echten Fassungen, wörtlich aus dem Verlauf.
    for (const zeile of [
      "const parsed = Number(value.replace(',', '.'))",
      "const parsed = Number(trimmed.replace(',', '.'))",
      "weightG: Number(neu.weightG.replace(',', '.')),",
      "const parsed = Number.parseFloat(value.replace(',', '.'))",
      "const ml = Number((doseMl[pump.id] ?? '').replace(',', '.'))",
      // Schreibweisen, die die erste Fassung der Suche nicht sah:
      'const x = Number(text.replace(",", "."))',
      "const x = Number(text.replaceAll(',', '.'))",
      "const x = Number(text.replace(/,/g, '.'))",
      "const x = parseFloat(text.replace(/,/, '.'))",
    ]) {
      expect(fundstellen('beispiel.ts', zeile), zeile).toHaveLength(1)
    }

    // Datums-Prüfungen und die Gegenrichtung (Punkt → Komma, fürs Schreiben)
    // gehen NICHT ins Netz. Eine erste Fassung suchte auch nach
    // `Number.isFinite`/`Number.isNaN` und fand 25 Stellen, davon 17
    // Unbeteiligte. Eine Prüfung, die überwiegend Unschuldige meldet, wird
    // abgeschaltet — dann prüft sie gar nichts mehr.
    expect(fundstellen('b.ts', 'if (Number.isNaN(date.getTime())) return null')).toEqual([])
    expect(fundstellen('b.ts', "return String(wert).replace('.', ',')")).toEqual([])
  })

  it('lässt Kommentare in Ruhe — eine Erwähnung ist keine Verwendung', () => {
    expect(fundstellen('k.ts', "// früher: Number(x.replace(',', '.'))")).toEqual([])
    expect(fundstellen('k.ts', " * ein naives `replace(',', '.')` liest den Tausenderpunkt")).toEqual([])
    expect(fundstellen('k.ts', "/* Number(x.replace(',', '.')) */ const a = 1")).toEqual([])
    expect(fundstellen('k.ts', "const a = 1 // Number(x.replace(',', '.'))")).toEqual([])
    expect(fundstellen('k.ts', "/**\n * Number(x.replace(',', '.'))\n */")).toEqual([])
    // … aber Code HINTER einem Block-Kommentar zählt, und die Zeile stimmt.
    expect(fundstellen('k.ts', "/* a\n b */ const z = Number(x.replace(',', '.'))"))
      .toEqual(["k.ts:2  const z = Number(x.replace(',', '.'))"])
    // Eine URL in einer Zeichenkette ist kein Kommentar.
    expect(fundstellen('k.ts', "const u = 'https://x'; const z = Number(x.replace(',', '.'))")).toHaveLength(1)
  })
})

describe('Die Leseregel (zahlOderNull) — deutsch: Komma = Dezimalzeichen, Punkt = Tausender', () => {
  /*
   * Entscheidung des Nutzers am 02.10.2026: „1.200" ist 1200. Vorher war es
   * 1,2 — im Kostenformular wurde ein Preis „1.200" still zu 1,20 €, ein
   * CO₂-Grenzwert „1.200" ppm zu 1,2 ppm.
   */

  it('liest Punkte in Dreiergruppen als Tausendertrenner', () => {
    expect(zahlOderNull('1.200'), 'Der Anlass: ein Preis „1.200" wurde zu 1,20 €.').toBe(1200)
    expect(zahlOderNull('12.500')).toBe(12500)
    expect(zahlOderNull('1.234.567')).toBe(1234567)
    expect(zahlOderNull('-1.200')).toBe(-1200)
  })

  it('liest Tausenderpunkt und Dezimalkomma zusammen', () => {
    expect(zahlOderNull('1.200,5')).toBe(1200.5)
    expect(zahlOderNull('1.234.567,89')).toBe(1234567.89)
  })

  it('liest das Komma als Dezimalzeichen', () => {
    expect(zahlOderNull('1,5')).toBe(1.5)
    expect(zahlOderNull('1,200'), '„1,200" ist deutsch eins-komma-zwei.').toBe(1.2)
    expect(zahlOderNull('0,5')).toBe(0.5)
    expect(zahlOderNull(',5')).toBe(0.5)
    expect(zahlOderNull('-2,5')).toBe(-2.5)
  })

  it('lässt ganze Zahlen ganz', () => {
    expect(zahlOderNull('1200')).toBe(1200)
    expect(zahlOderNull('0')).toBe(0)
  })

  it('liest einen Punkt, der kein Tausendertrenner sein KANN, als Dezimalpunkt', () => {
    // Bewusst beibehalten: „5.8" war in jedem Feld ein gültiger pH-Wert, und
    // es gibt keine zweite Deutung, die man durch Abweisen schützen würde.
    expect(zahlOderNull('5.8'), 'pH-Eingabe mit Punkt').toBe(5.8)
    expect(zahlOderNull('6.25')).toBe(6.25)
    expect(zahlOderNull('1.20'), 'zwei Ziffern hinter dem Punkt: keine Tausendergruppe').toBe(1.2)
    expect(zahlOderNull('1.2000'), 'vier Ziffern hinter dem Punkt: keine Tausendergruppe').toBe(1.2)
    expect(zahlOderNull('1234.5'), 'vier Ziffern vor dem Punkt: keine Tausendergruppe').toBe(1234.5)
    expect(zahlOderNull('0.500'), '„0.500" schreibt niemand für fünfhundert').toBe(0.5)
  })

  it('meldet Gemische als unlesbar, statt zu raten', () => {
    // Ein Punkt vor dem Komma, der keine Tausendergruppe ist, und ein Punkt
    // hinter dem Komma: keine Leseart ergibt einen Sinn.
    expect(istUnlesbar('1.2,5')).toBe(true)
    expect(istUnlesbar('1,5.3')).toBe(true)
    expect(istUnlesbar('1,2,3')).toBe(true)
    expect(istUnlesbar('1.20.0')).toBe(true)
    expect(istUnlesbar(',')).toBe(true)
    expect(istUnlesbar('1.200 €'), 'Einheiten gehören nicht ins Feld — sonst stünde wieder parseFloat da').toBe(true)
  })

  it('übersteht den Rundweg über das Eingabefeld', () => {
    // feldText schreibt nie Tausenderpunkte — also darf ein gespeicherter Wert
    // beim Zurücklesen nicht plötzlich als Tausender gelten.
    for (const wert of [1.234, 12.345, 1.2, 1200, 1234.567, 0.125, -1.25]) {
      expect(zahlOderNull(feldText(wert)), `${wert} → „${feldText(wert)}"`).toBe(wert)
    }
  })
})

describe('Die Maschinen-Regel (maschinenZahl)', () => {
  /*
   * Ein Zustand aus Home Assistant und der Wert eines <input type="number">
   * haben einen Dezimalpunkt und nie Tausendertrenner. Die deutsche Regel
   * machte aus „1.234" Volt 1234 — tausendmal zu viel.
   */

  it('liest den Punkt immer als Dezimalpunkt', () => {
    expect(maschinenZahl('1.234')).toBe(1.234)
    expect(maschinenZahl('5.82')).toBe(5.82)
    expect(maschinenZahl('-2.5')).toBe(-2.5)
    expect(maschinenZahl('1200')).toBe(1200)
  })

  it('nimmt ein Komma nur, wenn kein Punkt dasteht', () => {
    // Manche Vorlagen-Sensoren in Home Assistant schreiben deutsch.
    expect(maschinenZahl('5,8')).toBe(5.8)
    expect(maschinenZahl('1.234,5')).toBeNull()
  })

  it('macht aus Nicht-Zahlen null', () => {
    for (const roh of ['', '   ', 'unavailable', 'unknown', 'on', '–', '-', null, undefined]) {
      expect(maschinenZahl(roh), String(roh)).toBeNull()
    }
  })
})

describe('Der Weg zurueck ins Feld (feldText)', () => {
  /*
   * Die andere Richtung, und der zweite belegte Datenverlust.
   *
   * `feldText` stand am 01.09.2026 FUENFMAL in der Oberflaeche — als
   * `draftNumber`, `numberToInput` und zweimal als `formatDraftNumber` — und
   * alle fuenf schrieben `String(value)`. Ein gespeichertes Nassgewicht von
   * 21,5 g kam damit als „21.5" ins Feld zurueck, direkt neben einer Spalte,
   * die „21,5" schreibt. Wer nichts aenderte und speicherte, schickte den
   * Punkt wieder los.
   *
   * Zusammengefuehrt wurde es damals — gepruef­t nie: die Funktion stand am
   * 02.09.2026 als einzige dieser Datei bei 0 % Abdeckung.
   */

  it('schreibt das Komma, nicht den Punkt', () => {
    expect(feldText(21.5),
      'Ein gespeichertes Gewicht kommt mit PUNKT ins Feld zurueck, direkt neben einer '
      + 'Spalte, die Komma schreibt. Wer nichts aendert und speichert, schickt den Punkt '
      + 'wieder los.').toBe('21,5')
  })

  it('laesst ganze Zahlen ganz', () => {
    expect(feldText(21)).toBe('21')
  })

  it('macht aus fehlend ein leeres Feld, keine Null', () => {
    // Der teure Unterschied: „0" heisst gemessen und null, leer heisst nicht
    // gemessen. Stuende hier „0", waere ein nie erfasster Wert ploetzlich eine
    // Messung — und der Waechter urteilte darueber.
    expect(feldText(null), 'Aus einem nie erfassten Wert wurde eine Null.').toBe('')
    expect(feldText(undefined), 'Aus einem fehlenden Wert wurde eine Null.').toBe('')
  })

  it('macht aus NaN ein leeres Feld', () => {
    expect(feldText(Number.NaN), '„NaN" stand im Eingabefeld.').toBe('')
  })

  it('haelt der Rundreise stand', () => {
    // Was herausgeht, muss wieder hereinkommen — sonst verschiebt sich ein Wert
    // bei jedem Oeffnen des Formulars ein Stueck.
    for (const wert of [0, 1, 5.8, 21.5, 0.28, 1234.56, -3.5]) {
      expect(zahlOderNull(feldText(wert)),
        `${wert} kam als „${feldText(wert)}" ins Feld und daraus wieder als `
        + `${zahlOderNull(feldText(wert))} heraus.`).toBe(wert)
    }
  })
})
