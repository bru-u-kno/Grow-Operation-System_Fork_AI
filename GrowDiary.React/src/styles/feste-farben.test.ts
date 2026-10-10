import { describe, expect, it } from 'vitest'
import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'

/**
 * Keine neue Farbe, die nur zu einem Thema passt.
 *
 * <b>Warum es diesen Test gibt.</b> Diese Falle ist in Grow OS VIERMAL
 * zugeschnappt: jemand trägt eine Farbe fest ein, weil sie im dunklen Thema gut
 * aussieht, und im hellen steht dann dunkler Text auf dunklem Grund. Zuletzt im
 * Handy-Audit vom 18.08.2026 — die drei wichtigsten Eingabefelder des
 * Addback-Assistenten hatten einen gemessenen Kontrast von 1,12 statt 4,5.
 *
 * <b>Warum als Quelltext-Test und nicht im Browser.</b> Der E2E-Lauf fährt in CI
 * OHNE Backend. Eine Unterseite wie `/grows/1/addback` ist dort nur ein
 * Ladezustand — ein Kontrast-Test darauf wäre grün, ohne je die Felder gesehen
 * zu haben. Genau daran ist der bestehende Kontrast-Test viermal vorbeigelaufen.
 * Dieser hier braucht keinen Server.
 *
 * <b>Was er NICHT ist.</b> Kein Kontrast-Messer. Er sagt nur: hier steht eine
 * Farbe, die keinem Thema folgt — schau hin. Ob sie tatsächlich stört, misst der
 * Durchgang im Browser.
 */

/** Eine bewusste Ausnahme. Ohne Begründung gibt es keine. */
type Ausnahme = { datei: string; wert: string; grund: string }

const ERLAUBT: Ausnahme[] = [
  {
    datei: 'src/features/live/live-instrument.css',
    wert: '#020603',
    grund: 'Rückgrund der Kamerakachel. Liegt hinter einem Foto und soll in beiden Themen dunkel sein.',
  },
  {
    datei: 'src/features/live/live-instrument.css',
    wert: 'rgba(0,0,0,0.64)',
    grund: 'Beschriftungsbalken auf dem Kamerabild. Bleibt bewusst dunkel — die Schrift darauf ist deshalb fest hell gesetzt.',
  },
  {
    datei: 'src/features/live/live-instrument.css',
    wert: '#f2f6f3',
    grund: 'Die Schrift auf ebendiesem dunklen Balken. Sie darf dem Thema NICHT folgen, sonst steht sie im hellen schwarz auf schwarz.',
  },
]

/**
 * Der Bestand vom 18.08.2026.
 *
 * Diese Farben standen schon da, als der Test entstand. Sie sind NICHT gebilligt
 * — sie sind gemessen: der Durchgang durch alle 31 Seiten in beiden Themen fand
 * an keiner von ihnen unlesbaren Text. Deshalb bleiben sie vorerst stehen,
 * statt in einem Zug blind umgeschrieben zu werden.
 *
 * Die Liste darf nur kürzer werden. Wer eine dieser Stellen auf ein Token
 * umstellt, streicht sie hier; wer eine neue feste Farbe schreibt, kommt hier
 * nicht hinein, sondern nach {@link ERLAUBT} — mit Begründung.
 */
const BESTAND = [
  // Am 18.08.2026 gestartet mit 24 Eintraegen; zwei davon sind noch im selben
  // Durchgang auf Token umgestellt worden (die Ergebniskarte des
  // Addback-Assistenten und die Trefferliste der Suche).
  'src/features/grow-detail/growdetail-instrument.css  background: rgba(4, 7, 10, 0.6)',
  'src/features/grow-detail/growdetail-instrument.css  background: rgba(10, 16, 14, 0.5)',
  'src/features/grow-detail/growdetail-instrument.css  background: rgba(10, 16, 14, 0.72)',
  'src/features/grow-detail/growdetail-instrument.css  background: rgba(82, 233, 140, 0.06)',
  'src/features/grow-detail/growdetail-instrument.css  background: rgba(255, 255, 255, 0.05)',
  'src/features/grow-detail/growdetail-instrument.css  color: #001905',
  'src/features/hardware/hardware.css  background: color-mix(in oklab, #fff 30%, transparent)',
  'src/features/live/live-screen.css  background: rgba(2, 5, 4, .72)',
  'src/features/mobile/mobile.css  background: #ffffff',
  'src/features/mobile/mobile.css  color: #5f5e5a',
  'src/features/tents/tents.css  background: rgba(255, 255, 255, .02)',
  'src/styles/90-operations.css  background: linear-gradient(180deg, rgba(0,0,0,0), var(--v1-bg) 42%)',
  'src/styles/90-operations.css  background: rgba(255, 255, 255, 0.02)',
  'src/styles/primitives.css  background: rgba(0,0,0,0.25)',
]

const wurzel = fileURLToPath(new URL('../..', import.meta.url))

function alleCss(verzeichnis: string): string[] {
  let gefunden: string[] = []
  for (const name of readdirSync(verzeichnis)) {
    const pfad = join(verzeichnis, name)
    if (statSync(pfad).isDirectory()) gefunden = gefunden.concat(alleCss(pfad))
    else if (name.endsWith('.css')) gefunden.push(pfad)
  }
  return gefunden
}

/** Eigenschaften, bei denen eine feste Farbe ein Thema brechen kann. */
const EIGENSCHAFT = /(?:^|[;{\s])(background|background-color|color|border-color)\s*:\s*([^;}]+)/gi
const LITERAL = /#[0-9a-fA-F]{3,8}\b|\brgba?\(|\boklch\(|\bhsla?\(/

describe('Feste Farben im CSS', () => {
  const dateien = alleCss(join(wurzel, 'src')).filter((f) => !f.endsWith('tokens.css'))

  const funde = dateien.flatMap((datei) => {
    const kurz = datei.slice(wurzel.length).split('\\').join('/').replace(/^\//, '')
    return readFileSync(datei, 'utf8').split('\n').flatMap((zeile, i) => {
      const treffer: Array<{ datei: string; zeile: number; eigenschaft: string; wert: string; schluessel: string }> = []
      EIGENSCHAFT.lastIndex = 0
      let m: RegExpExecArray | null
      while ((m = EIGENSCHAFT.exec(zeile))) {
        const wert = m[2].trim()
        if (LITERAL.test(wert)) {
          treffer.push({ datei: kurz, zeile: i + 1, eigenschaft: m[1], wert, schluessel: `${kurz}  ${m[1]}: ${wert}` })
        }
      }
      return treffer
    })
  })

  it('findet die Stildateien überhaupt', () => {
    // Ohne diese Prüfung wäre alles darunter trivial grün, sobald der Pfad nicht
    // mehr stimmt — und das ist genau die Sorte Wächter, die nichts hält.
    expect(dateien.length, 'keine CSS-Dateien gefunden — stimmt der Pfad noch?').toBeGreaterThan(20)
    expect(funde.length, 'kein einziger Farbwert gefunden — greift der Ausdruck noch?').toBeGreaterThan(10)
  })

  it('erlaubt keine neue feste Farbe', () => {
    const bestand = new Set(BESTAND)
    const neu = funde.filter(
      (fund) =>
        !bestand.has(fund.schluessel)
        && !ERLAUBT.some((a) => fund.datei === a.datei && fund.wert.includes(a.wert)),
    )

    expect(
      neu.map((f) => `${f.datei}:${f.zeile}  ${f.eigenschaft}: ${f.wert}`),
      'Neue feste Farbe. Sie folgt keinem Thema — im hellen kann daraus schwarz '
        + 'auf schwarz werden. Entweder ein Token benutzen (var(--…)) oder in '
        + 'feste-farben.test.ts unter ERLAUBT eintragen, mit Begründung:',
    ).toEqual([])
  })

  it('lässt den Bestand nicht wachsen', () => {
    // Der Bestand ist eine Schuld, kein Vorrat. Er darf schrumpfen; jeder
    // Eintrag, der nicht mehr im Quelltext steht, gehört hier gestrichen.
    const vorhanden = new Set(funde.map((f) => f.schluessel))
    const veraltet = BESTAND.filter((eintrag) => !vorhanden.has(eintrag))

    expect(veraltet, 'Diese Bestands-Einträge gibt es im Quelltext nicht mehr — bitte aus der Liste streichen:')
      .toEqual([])
  })

  it('hat für jede Ausnahme einen Grund', () => {
    for (const a of ERLAUBT) {
      expect(a.grund.length, `Ausnahme ohne Grund: ${a.datei} ${a.wert}`).toBeGreaterThan(30)
    }
  })
})
