import { describe, expect, it } from 'vitest'
import { buildPhaseTimeline, currentPhaseLabel, flipLabel, PHASEN_ANZEIGE, shortDate } from './phase-timeline'
import { ankerphaseName, PHASEN, phaseName } from '../../deutsche-woerter'

/** 1. Juni 2026, damit die Tagesrechnung nachvollziehbar bleibt. */
const JETZT = new Date('2026-06-01T12:00:00Z').getTime()
const TAG = 86_400_000
const vorTagen = (n: number) => new Date(JETZT - n * TAG).toISOString()
const inTagen = (n: number) => new Date(JETZT + n * TAG).toISOString()

/** Die Beginne, wie der Server sie im `phasenanker` schickt. */
const anker = (a: { anzucht?: number, veg?: number, bluete?: number, bluetePlan?: number }) => ({
  anzuchtAb: a.anzucht != null ? vorTagen(a.anzucht) : null,
  vegAb: a.veg != null ? vorTagen(a.veg) : null,
  blueteAb: a.bluete != null ? vorTagen(a.bluete) : a.bluetePlan != null ? inTagen(a.bluetePlan) : null,
})

describe('buildPhaseTimeline', () => {
  it('bleibt leer ohne Startdatum', () => {
    expect(buildPhaseTimeline(null, JETZT).phases).toEqual([])
    expect(buildPhaseTimeline({ startDate: null }, JETZT).phases).toEqual([])
    expect(buildPhaseTimeline({ startDate: 'kein Datum' }, JETZT).phases).toEqual([])
  })

  it('zeigt ohne Bestätigung die Anzucht als laufend — und den Rest offen', () => {
    // Seit dem Phasenanker (02.10.2026) schätzt der Strahl keinen Vegi-Beginn
    // mehr. Vorher stand hier nach 14 Tagen „Wachstum · Tag 6", obwohl
    // niemand etwas bestätigt hatte.
    const strahl = buildPhaseTimeline({ startDate: vorTagen(20), phasenanker: anker({ anzucht: 20 }) }, JETZT)

    expect(strahl.phases.map((phase) => phase.label)).toEqual([
      'Anzucht · Tag 21',
      'Wachstum · offen',
      'Blüte · offen',
      'Trocknen 10 T',
      'Aushärten 30 T',
    ])
    expect(strahl.phases[1].days).toBe(0)
    expect(strahl.dates.flip).toBe('—')
    expect(strahl.dates.harvest).toBe('—')
    expect(strahl.dates.ready).toBe('—')
    expect(strahl.daysToFlip).toBeNull()
  })

  it('schätzt nichts, wenn der Anker fehlt', () => {
    // Ohne Anker ist nichts bestätigt — auch nicht nach 90 Tagen.
    const strahl = buildPhaseTimeline({ startDate: vorTagen(90) }, JETZT)
    expect(strahl.phases.find((phase) => phase.state === 'current')!.name).toBe('Anzucht')
  })

  it('nimmt den bestätigten Vegi-Beginn', () => {
    const strahl = buildPhaseTimeline({ startDate: vorTagen(10), phasenanker: anker({ anzucht: 10, veg: 4 }) }, JETZT)

    const laufend = strahl.phases.find((phase) => phase.state === 'current')!
    expect(laufend.name).toBe('Veg')
    expect(laufend.label).toBe('Wachstum · Tag 5')
    expect(strahl.phases.find((phase) => phase.name === 'Anzucht')!.label).toBe('Anzucht 6 T')
  })

  it('rechnet aus der geplanten Veg-Dauer den Flip und die Ernte', () => {
    const strahl = buildPhaseTimeline({
      startDate: vorTagen(40),
      phasenanker: anker({ anzucht: 40, veg: 20 }),
      plannedVegDays: 28,
      breederFlowerWeeksMax: 9,
    }, JETZT)

    // Die geplanten 28 Tage zählen ab dem Vegi-Beginn: Flip acht Tage voraus.
    expect(strahl.flipIsPlanned).toBe(true)
    expect(strahl.daysToFlip).toBe(8)
    expect(strahl.dates.flip).toBe('09.06.')
    expect(strahl.dates.harvest).toBe('11.08.')

    const veg = strahl.phases.find((phase) => phase.state === 'current')!
    expect(veg.label).toBe('Wachstum · Tag 21 von 28')
    expect(veg.progress).toBeCloseTo(21 / 28, 5)

    expect(strahl.phases.find((phase) => phase.name === 'Blüte')!.label).toBe('Blüte 63 T geplant')
  })

  it('plant ohne Vegi-Beginn keinen Flip — die Dauer zählt ab der Bestätigung', () => {
    const strahl = buildPhaseTimeline({ startDate: vorTagen(20), phasenanker: anker({ anzucht: 20 }), plannedVegDays: 28 }, JETZT)

    expect(strahl.dates.flip).toBe('—')
    expect(strahl.phases.find((phase) => phase.name === 'Veg')!.label).toBe('Wachstum · 28 T geplant')
  })

  it('nennt den Flip überfällig, wenn der Plan verstrichen ist', () => {
    const strahl = buildPhaseTimeline({
      startDate: vorTagen(60),
      phasenanker: anker({ anzucht: 60, veg: 40 }),
      plannedVegDays: 28,
    }, JETZT)

    expect(strahl.daysToFlip).toBe(-12)
    const veg = strahl.phases.find((phase) => phase.state === 'current')!
    expect(veg.progress).toBe(1)
    expect(veg.label).toBe('Wachstum · Tag 41 von 28')
  })

  it('schließt das Wachstum ab und zählt die Blüte ab dem Blütebeginn', () => {
    const strahl = buildPhaseTimeline({
      startDate: vorTagen(50),
      phasenanker: anker({ anzucht: 50, veg: 36, bluete: 14 }),
      breederFlowerWeeksMax: 8,
    }, JETZT)

    expect(strahl.flipIsPlanned).toBe(false)
    const veg = strahl.phases.find((phase) => phase.name === 'Veg')!
    expect(veg.state).toBe('done')
    expect(veg.label).toBe('Wachstum 22 T')

    const bluete = strahl.phases.find((phase) => phase.name === 'Blüte')!
    expect(bluete.state).toBe('current')
    expect(bluete.label).toBe('Blüte · Tag 15 von 56')
    expect(bluete.progress).toBeCloseTo(15 / 56, 5)
  })

  it('behandelt einen künftigen Flip als Plan, nicht als erfolgt', () => {
    const strahl = buildPhaseTimeline({
      startDate: vorTagen(24),
      phasenanker: anker({ anzucht: 24, veg: 10, bluetePlan: 5 }),
    }, JETZT)

    const veg = strahl.phases.find((phase) => phase.state === 'current')!
    expect(veg.name).toBe('Veg')
    expect(veg.label).toBe('Wachstum · Tag 11 von 15')
    expect(strahl.flipIsPlanned).toBe(true)
    expect(strahl.daysToFlip).toBe(5)
  })

  it('fällt ohne Breeder-Angabe auf acht Wochen zurück', () => {
    const strahl = buildPhaseTimeline({
      startDate: vorTagen(24),
      phasenanker: anker({ anzucht: 24, veg: 10 }),
      plannedVegDays: 20,
    }, JETZT)

    expect(strahl.phases.find((phase) => phase.name === 'Blüte')!.label).toBe('Blüte 56 T geplant')
  })

  it('ignoriert eine unsinnige Veg-Dauer von null oder weniger', () => {
    const strahl = buildPhaseTimeline({
      startDate: vorTagen(24),
      phasenanker: anker({ anzucht: 24, veg: 10 }),
      plannedVegDays: 0,
    }, JETZT)

    expect(strahl.dates.flip).toBe('—')
    expect(strahl.phases.find((phase) => phase.state === 'current')!.label).toBe('Wachstum · Tag 11')
  })

  it('zeigt keine Anzucht, wo es keine gab (bewurzelt angelegt, Einstieg Vegi)', () => {
    const strahl = buildPhaseTimeline({ startDate: vorTagen(10), phasenanker: anker({ anzucht: 10, veg: 10 }) }, JETZT)

    expect(strahl.phases.some((phase) => phase.name === 'Anzucht')).toBe(false)
    expect(strahl.phases.find((phase) => phase.state === 'current')!.label).toBe('Wachstum · Tag 11')
  })

  it('zeigt eine Blüte ohne bestätigtes Wachstum ehrlich als „nicht bestätigt"', () => {
    const strahl = buildPhaseTimeline({ startDate: vorTagen(40), phasenanker: anker({ anzucht: 40, bluete: 12 }) }, JETZT)

    expect(strahl.phases.find((phase) => phase.name === 'Anzucht')!.label).toBe('Anzucht 28 T')
    expect(strahl.phases.find((phase) => phase.name === 'Veg')!.label).toBe('Wachstum · nicht bestätigt')
    expect(strahl.phases.find((phase) => phase.state === 'current')!.name).toBe('Blüte')
  })
})

describe('flipLabel', () => {
  it('sagt, was Sache ist — statt immer „Flip geplant"', () => {
    expect(flipLabel(false, null, '—')).toBe('Flip offen')
    expect(flipLabel(false, null, '20.05.')).toBe('Geflippt 20.05.')
    expect(flipLabel(true, 8, '09.06.')).toBe('Flip geplant 09.06. · in 8 T')
    expect(flipLabel(true, 0, '01.06.')).toBe('Flip heute geplant')
    expect(flipLabel(true, -3, '29.05.')).toBe('Flip überfällig seit 3 T')
  })

  describe('nach der Ernte', () => {
    it('rechnet vom Erntetag bis fertig und nennt die Herkunft der Dauern', () => {
      // Vor 3 Tagen geerntet: das Trocknen laeuft, das Aushaerten steht bevor.
      const strahl = buildPhaseTimeline(
        { startDate: vorTagen(120), phasenanker: anker({ anzucht: 120, veg: 100, bluete: 70 }), endDate: vorTagen(3) }, JETZT)

      const trocknen = strahl.phases.find((phase) => phase.name === 'Trocknen')!
      expect(trocknen.state).toBe('current')
      expect(trocknen.label).toBe('Trocknen · Tag 4 von 10')

      const aushaerten = strahl.phases.find((phase) => phase.name === 'Aushärten')!
      expect(aushaerten.state).toBe('planned')

      // 3 Tage geerntet + 10 Trocknen + 30 Aushaerten = 37 Tage nach heute.
      expect(strahl.dates.ready).toBe(shortDate(new Date(JETZT + 37 * TAG)))
      // Die Zahlen sind Richtwerte — das muss dabeistehen.
      expect(strahl.readyNote).toContain('7–14')
      expect(strahl.readyNote).toContain('keine Termine')
    })

    it('schaltet nach dem Trocknen auf Aushaerten um', () => {
      const strahl = buildPhaseTimeline(
        { startDate: vorTagen(150), phasenanker: anker({ anzucht: 150, veg: 130, bluete: 100 }), endDate: vorTagen(25) }, JETZT)

      expect(strahl.phases.find((phase) => phase.name === 'Trocknen')!.state).toBe('done')
      const aushaerten = strahl.phases.find((phase) => phase.name === 'Aushärten')!
      expect(aushaerten.state).toBe('current')
      expect(aushaerten.label).toBe('Aushärten · Tag 16 von 30')
    })

    it('laesst einen bluehenden Grow nicht ins Trocknen rutschen', () => {
      // Ohne Erntedatum bleiben beide Vorschau — sonst stuende „Trocknen Tag 3"
      // an einem Grow, der noch in der Bluete haengt.
      const strahl = buildPhaseTimeline({ startDate: vorTagen(120), phasenanker: anker({ anzucht: 120, veg: 100, bluete: 70 }) }, JETZT)

      expect(strahl.phases.find((phase) => phase.name === 'Trocknen')!.state).toBe('planned')
      expect(strahl.phases.find((phase) => phase.name === 'Aushärten')!.state).toBe('planned')
      // Der Erntetermin ist geschaetzt, also auch das Fertig-Datum — aber es steht da.
      expect(strahl.dates.ready).not.toBe('—')
    })
  })
})
/**
 * Die App zeigt dieselbe Phase, die der Server rechnet — weil sie die Beginne
 * vom Server nimmt (`phasenanker`). Vorher liefen Strahl und Server bei
 * Autoflower und mitgebrachten Tagen auseinander, weil beide selbst schätzten.
 */
describe('Zeitstrahl und Server sagen dasselbe', () => {
  it('liest keine Rohfelder: Flipdatum, Keimdatum und Einstieg ändern ohne Anker nichts', () => {
    const roh = {
      startDate: vorTagen(50),
      flipDate: vorTagen(20),
      germinatedAt: vorTagen(50),
      vegStartedAt: vorTagen(36),
      seedType: 'Autoflower',
      entryPoint: 'Veg',
      daysAlreadyInPhase: 20,
    }
    const ohneAnker = buildPhaseTimeline(roh, JETZT)
    expect(ohneAnker.phases.find((phase) => phase.state === 'current')?.name,
      'der Strahl rechnet aus Rohfeldern selbst — die Beginne gehören dem Phasenanker').toBe('Anzucht')

    const mitAnker = buildPhaseTimeline({ ...roh, phasenanker: anker({ anzucht: 50, veg: 36, bluete: 20 }) }, JETZT)
    expect(mitAnker.phases.find((phase) => phase.state === 'current')?.name).toBe('Blüte')
  })
})

/**
 * Der Strahl spricht dieselbe Sprache wie `phaseName`.
 *
 * Befund 02.10.2026: auf der Karte stand „Veg Tag 64" — der Strahl führte für
 * die vegetative Phase einen eigenen Namen, an der Übersetzung vorbei. Der
 * Demobestand hat keinen Grow im Wachstum, also sah es nie jemand. Geprüft
 * wird hier über ALLE Lagen des Strahls (Sämling, Wachstum mit und ohne Plan,
 * Blüte, geerntet), nicht über eine.
 */
describe('Phasennamen auf dem Schirm', () => {
  const lagen = {
    'in der Anzucht': buildPhaseTimeline({ startDate: vorTagen(5), phasenanker: anker({ anzucht: 5 }) }, JETZT),
    'im Wachstum ohne Plan': buildPhaseTimeline({ startDate: vorTagen(30), phasenanker: anker({ anzucht: 30, veg: 16 }) }, JETZT),
    'im Wachstum mit Flip in der Zukunft': buildPhaseTimeline({ startDate: vorTagen(30), phasenanker: anker({ anzucht: 30, veg: 16, bluetePlan: 20 }) }, JETZT),
    'in der Anzucht mit Plan': buildPhaseTimeline({ startDate: vorTagen(5), phasenanker: anker({ anzucht: 5 }), plannedVegDays: 28 }, JETZT),
    'in der Blüte': buildPhaseTimeline({ startDate: vorTagen(60), phasenanker: anker({ anzucht: 60, veg: 46, bluete: 20 }) }, JETZT),
    'geerntet': buildPhaseTimeline({ startDate: vorTagen(120), phasenanker: anker({ anzucht: 120, veg: 106, bluete: 80 }), endDate: vorTagen(3) }, JETZT),
  }

  it('nennt jede Phase so, wie phaseName sie nennt', () => {
    expect(PHASEN_ANZEIGE.Veg).toBe(phaseName('Veg'))
    expect(PHASEN_ANZEIGE.Anzucht).toBe(ankerphaseName('Anzucht'))
    expect(PHASEN_ANZEIGE.Blüte).toBe(phaseName('Flower'))
    expect(PHASEN_ANZEIGE.Trocknen).toBe(phaseName('Dry'))
    expect(PHASEN_ANZEIGE.Aushärten).toBe(phaseName('Cure'))
  })

  it('schreibt in keine Beschriftung einen rohen Phasen-Wert', () => {
    // Mengenwächter: ohne Beschriftungen liefe die Suche ins Leere und wäre grün.
    const texte = Object.entries(lagen).flatMap(([lage, strahl]) =>
      strahl.phases.flatMap((phase) => [`${lage}: ${phase.label}`, `${lage}: ${phase.short}`]))
    expect(texte.length).toBeGreaterThanOrEqual(6 * 2 * 4)

    const roh = texte.filter((text) => PHASEN.some((wert) =>
      phaseName(wert) !== wert && new RegExp(`(?<![\\w-])${wert}(?![\\w-])`).test(text)))
    expect(roh, `rohe Phasen-Werte im Strahl:\n  ${roh.join('\n  ')}`).toEqual([])
  })

  it('nennt die laufende Phase im Kartenkopf auf Deutsch', () => {
    expect(currentPhaseLabel(lagen['im Wachstum mit Flip in der Zukunft'])).toMatch(/^Wachstum Tag \d+$/)
    expect(currentPhaseLabel(lagen['im Wachstum ohne Plan'])).toMatch(/^Wachstum Tag \d+$/)
    expect(currentPhaseLabel(lagen['in der Blüte'])).toMatch(/^Blüte Tag \d+$/)
  })
})
