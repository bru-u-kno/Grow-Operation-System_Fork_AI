import { describe, expect, it } from 'vitest'
import { aktiveRegeln, speicherbareRegeln, vertauschteGrenzen, type Grenzwertzeile } from './grenzwerte-modell'

/**
 * Ein abgewählter Grenzwert wird pausiert, nicht gelöscht.
 *
 * <b>Der Anlass (01.09.2026).</b> Die Seite schickte nur die Zeilen mit Haken,
 * und der Server ersetzt beim Speichern den ganzen Satz. Wer den Haken
 * herausnahm, verlor seine Zahlen — mit der Meldung „gespeichert" und den
 * Zahlen weiter sichtbar im Formular. Erst beim nächsten Aufruf der Seite fiel
 * es auf.
 */

const zeile = (teil: Partial<Grenzwertzeile> = {}): Grenzwertzeile =>
  ({ min: '', max: '', cooldown: '30', enabled: true, quelle: 'Fest', toleranz: '', ...teil })

describe('speicherbareRegeln', () => {
  it('schickt eine abgewaehlte Zeile MIT — nur eben pausiert', () => {
    const regeln = speicherbareRegeln(['reservoir-ph'], {
      'reservoir-ph': zeile({ min: '5,8', max: '6,3', enabled: false }),
    })

    expect(regeln, 'Die abgewaehlte Zeile faellt aus dem Aufruf — der Server ersetzt den '
      + 'ganzen Satz und loescht damit die Grenzen.').toHaveLength(1)
    expect(regeln[0].enabled).toBe(false)
    expect(regeln[0].minValue).toBe(5.8)
    expect(regeln[0].maxValue).toBe(6.3)
  })

  it('laesst eine Zeile ganz ohne Grenzen weg', () => {
    // Ohne Grenze ist nichts zu speichern — das lehnt der Server ohnehin ab.
    expect(speicherbareRegeln(['reservoir-ph'], { 'reservoir-ph': zeile() })).toHaveLength(0)
  })

  it('eine Grenze genuegt', () => {
    expect(speicherbareRegeln(['x'], { x: zeile({ max: '30' }) })).toHaveLength(1)
    expect(speicherbareRegeln(['x'], { x: zeile({ min: '18' }) })).toHaveLength(1)
  })

  it('liest deutsche Kommazahlen', () => {
    expect(speicherbareRegeln(['x'], { x: zeile({ min: '5,85' }) })[0].minValue).toBe(5.85)
  })

  it('faengt eine unbrauchbare Schonfrist ab', () => {
    // Math.max(1, …): 0 Minuten hiesse „bei jedem Messwert melden".
    expect(speicherbareRegeln(['x'], { x: zeile({ min: '1', cooldown: '0' }) })[0].cooldownMinutes).toBe(1)
    expect(speicherbareRegeln(['x'], { x: zeile({ min: '1', cooldown: '' }) })[0].cooldownMinutes).toBe(30)
  })
})

describe('aktiveRegeln', () => {
  it('zaehlt nur die, die wirklich wachen', () => {
    const regeln = speicherbareRegeln(['a', 'b'], {
      a: zeile({ min: '1', enabled: true }),
      b: zeile({ min: '2', enabled: false }),
    })

    expect(regeln).toHaveLength(2)
    expect(aktiveRegeln(regeln)).toBe(1)
  })
})

describe('vertauschteGrenzen', () => {
  it('meldet ein Paar, dessen Untergrenze ueber der Obergrenze liegt', () => {
    /* Bei min 22 / max 18 rechnet der Server `wert < min ? unten : wert > max ?
       oben : im Rahmen` — bei 20 °C greift die erste Bedingung, und die Regel
       meldet dauerhaft „zu kalt", obwohl 20 zwischen den Zahlen liegt. */
    const regeln = speicherbareRegeln(['reservoir-temp'], {
      'reservoir-temp': zeile({ min: '22', max: '18' }),
    })

    expect(vertauschteGrenzen(regeln)).toEqual(['reservoir-temp'])
  })

  it('schweigt bei gleichen Grenzen und bei nur einer', () => {
    expect(vertauschteGrenzen(speicherbareRegeln(['x'], { x: zeile({ min: '20', max: '20' }) }))).toEqual([])
    expect(vertauschteGrenzen(speicherbareRegeln(['x'], { x: zeile({ min: '20' }) }))).toEqual([])
  })
})

/**
 * Eine Zeile, die dem Wochenplan folgt, trägt keine eigenen Zahlen.
 *
 * <b>Der Anlass (10.09.2026).</b> Der Filter oben ließ jede Zeile ohne Min/Max
 * fallen — genau so sieht eine Plan-Zeile aus. Sie wäre beim Speichern
 * lautlos verschwunden, und der Nutzer hätte beim nächsten Aufruf wieder
 * „Fest" dastehen sehen.
 */
describe('Plan-Zeilen', () => {
  it('geht ohne eigene Grenzen mit raus', () => {
    const regeln = speicherbareRegeln(['reservoir-ec'], {
      'reservoir-ec': zeile({ quelle: 'Plan', toleranz: '0,2' }),
    })

    expect(regeln).toHaveLength(1)
    expect(regeln[0].quelle).toBe('Plan')
    expect(regeln[0].toleranz).toBe(0.2)
    expect(regeln[0].minValue, 'Plan-Zeilen tragen keine eigenen Zahlen.').toBeNull()
    expect(regeln[0].maxValue).toBeNull()
  })

  it('vergisst eingetippte Zahlen beim Umschalten auf Plan', () => {
    const regeln = speicherbareRegeln(['reservoir-ph'], {
      'reservoir-ph': zeile({ quelle: 'Plan', min: '5,6', max: '6,1' }),
    })

    expect(regeln[0].minValue, 'Sonst überstimmte die alte Zahl still den Plan.').toBeNull()
    expect(regeln[0].maxValue).toBeNull()
  })

  it('gilt nicht fuer Messgroessen ohne Planwert', () => {
    // Luftfeuchte steht in keinem Profil und in keinem Feed-Chart.
    expect(speicherbareRegeln(['humidity'], { humidity: zeile({ quelle: 'Plan' }) })).toHaveLength(0)
  })

  it('eine Fest-Zeile bleibt unveraendert', () => {
    const regeln = speicherbareRegeln(['reservoir-ec'], { 'reservoir-ec': zeile({ min: '0,7', max: '1,2' }) })
    expect(regeln[0].quelle).toBe('Fest')
    expect(regeln[0].toleranz).toBeNull()
    expect(regeln[0].maxValue).toBe(1.2)
  })
})
