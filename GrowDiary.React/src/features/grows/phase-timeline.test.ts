import { describe, expect, it } from 'vitest'
import { buildPhaseTimeline, currentPhaseLabel, flipLabel, PHASEN_ANZEIGE, shortDate } from './phase-timeline'
import { PHASEN, phaseName } from '../../deutsche-woerter'

/** 1. Juni 2026, damit die Tagesrechnung nachvollziehbar bleibt. */
const JETZT = new Date('2026-06-01T12:00:00Z').getTime()
const TAG = 86_400_000
const vorTagen = (n: number) => new Date(JETZT - n * TAG).toISOString()
const inTagen = (n: number) => new Date(JETZT + n * TAG).toISOString()

describe('buildPhaseTimeline', () => {
  it('bleibt leer ohne Startdatum', () => {
    expect(buildPhaseTimeline(null, JETZT).phases).toEqual([])
    expect(buildPhaseTimeline({ startDate: null }, JETZT).phases).toEqual([])
    expect(buildPhaseTimeline({ startDate: 'kein Datum' }, JETZT).phases).toEqual([])
  })

  it('zeigt immer alle sechs Phasen — auch die, für die nichts feststeht', () => {
    const strahl = buildPhaseTimeline({ startDate: vorTagen(20) }, JETZT)

    // Genau das fehlte: ohne Flip und ohne Plan stand da nur ein Balken „Veg".
    // Der Sämling kam 2.0.0-beta.15 dazu — die Zielwerte kannten ihn längst,
    // der Strahl nicht, und beide widersprachen sich damit offen.
    expect(strahl.phases.map((phase) => phase.label)).toEqual([
      'Keim · nicht erfasst',
      'Sämling 14 T',
      'Wachstum · Tag 6',
      'Blüte · offen',
      // Trocknen und Aushaerten kamen dazu, weil der Strahl an der Ernte endete
      // — und damit vor der Frage, wann es wirklich fertig ist.
      'Trocknen 10 T',
      'Aushärten 30 T',
    ])
    expect(strahl.phases[0].days).toBe(0)
    expect(strahl.phases[3].days).toBe(0)
    expect(strahl.dates.flip).toBe('—')
    expect(strahl.dates.harvest).toBe('—')
    // Ohne Erntedatum gibt es auch kein Fertig-Datum — geraten wird nichts.
    expect(strahl.dates.ready).toBe('—')
    expect(strahl.daysToFlip).toBeNull()
  })

  it('zeigt den Sämling als laufend, solange er läuft', () => {
    // Mitten im Sämling — genau der Fall aus dem echten Zelt. Vorher stand
    // hier „Veg · Tag 7", während die Kacheln Sämlings-Ziele zeigten.
    const strahl = buildPhaseTimeline({ startDate: vorTagen(7) }, JETZT)

    const laufend = strahl.phases.find((phase) => phase.state === 'current')!
    expect(laufend.name).toBe('Sämling')
    // Ohne Eintrag ist es eine Schätzung, und das steht auch dran.
    expect(laufend.label).toBe('Sämling · Tag 7 (geschätzt)')
    expect(strahl.phases.find((phase) => phase.name === 'Veg')!.state).toBe('planned')
  })

  it('nimmt den eingetragenen Übergang statt der Schätzung', () => {
    // Der Wechsel hängt am Aussehen, nicht am Kalender: wer an Tag 6 echte
    // gezackte Blätter sieht, trägt das ein — und ab da läuft die Veg.
    const strahl = buildPhaseTimeline({
      startDate: vorTagen(10),
      vegStartedAt: vorTagen(4),
    }, JETZT)

    const laufend = strahl.phases.find((phase) => phase.state === 'current')!
    expect(laufend.name).toBe('Veg')
    expect(laufend.label).toBe('Wachstum · Tag 4')
    // Und der Sämling steht mit seiner echten Dauer da, ohne „geschätzt".
    expect(strahl.phases.find((phase) => phase.name === 'Sämling')!.label).toBe('Sämling 6 T')
  })

  it('rechnet aus der geplanten Veg-Dauer den Flip und die Ernte', () => {
    const strahl = buildPhaseTimeline({
      startDate: vorTagen(40),
      vegStartedAt: vorTagen(20),
      plannedVegDays: 28,
      breederFlowerWeeksMax: 9,
    }, JETZT)

    // Die geplanten 28 Tage zählen ab dem Veg-Beginn: Flip acht Tage voraus.
    expect(strahl.flipIsPlanned).toBe(true)
    expect(strahl.daysToFlip).toBe(8)
    expect(strahl.dates.flip).toBe('09.06.')
    expect(strahl.dates.harvest).toBe('11.08.')

    const veg = strahl.phases.find((phase) => phase.state === 'current')!
    expect(veg.label).toBe('Wachstum · Tag 20 von 28')
    expect(veg.progress).toBeCloseTo(20 / 28, 5)

    const bluete = strahl.phases.find((phase) => phase.name === 'Blüte')!
    expect(bluete.label).toBe('Blüte 63 T geplant')
  })

  it('nennt den Flip überfällig, wenn der Plan verstrichen ist', () => {
    const strahl = buildPhaseTimeline({
      startDate: vorTagen(60),
      vegStartedAt: vorTagen(40),
      plannedVegDays: 28,
    }, JETZT)

    expect(strahl.daysToFlip).toBe(-12)
    const veg = strahl.phases.find((phase) => phase.state === 'current')!
    // Der Fortschritt bleibt bei 1 stehen statt über den Balken hinauszulaufen.
    expect(veg.progress).toBe(1)
    expect(veg.label).toBe('Wachstum · Tag 40 von 28')
  })

  it('setzt die Keimphase vor den Sämling, sobald bewurzelt bekannt ist', () => {
    const strahl = buildPhaseTimeline({
      startDate: vorTagen(30),
      rootedAt: vorTagen(18),
      vegStartedAt: vorTagen(10),
      plannedVegDays: 40,
    }, JETZT)

    expect(strahl.phases[0].label).toBe('Keim 12 T')
    expect(strahl.phases[0].state).toBe('done')
    // Der Sämling liegt zwischen Bewurzelung und dem eingetragenen Übergang.
    expect(strahl.phases[1].label).toBe('Sämling 8 T')
    // Die geplanten 40 Tage zählen ab dem Veg-Beginn, nicht ab dem Start.
    expect(strahl.phases[2].label).toBe('Wachstum · Tag 10 von 40')
  })

  it('nimmt das Bewurzelungsdatum, wenn beides bekannt ist', () => {
    const strahl = buildPhaseTimeline({
      startDate: vorTagen(30),
      germinatedAt: vorTagen(25),
      rootedAt: vorTagen(20),
    }, JETZT)

    expect(strahl.phases[0].label).toBe('Keim 10 T')
  })

  it('schließt Veg ab und zählt die Blüte, sobald geflippt wurde', () => {
    const strahl = buildPhaseTimeline({
      startDate: vorTagen(50),
      vegStartedAt: vorTagen(36),
      flipDate: vorTagen(14),
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
      vegStartedAt: vorTagen(10),
      flipDate: inTagen(5),
    }, JETZT)

    // Das Datum steht fest, der Flip ist aber noch nicht passiert: Veg läuft.
    const veg = strahl.phases.find((phase) => phase.state === 'current')!
    expect(veg.name).toBe('Veg')
    expect(veg.label).toBe('Wachstum · Tag 10 von 15')
    // Auch ein festes Datum in der Zukunft liest sich als "geplant" — vorher
    // stand unter dem Strahl "Geflippt", obwohl der Termin erst kommt.
    expect(strahl.flipIsPlanned).toBe(true)
    expect(strahl.daysToFlip).toBe(5)
  })

  it('fällt ohne Breeder-Angabe auf acht Wochen zurück', () => {
    const strahl = buildPhaseTimeline({
      startDate: vorTagen(24),
      vegStartedAt: vorTagen(10),
      plannedVegDays: 20,
    }, JETZT)

    expect(strahl.phases.find((phase) => phase.name === 'Blüte')!.label).toBe('Blüte 56 T geplant')
  })

  it('ignoriert eine unsinnige Veg-Dauer von null oder weniger', () => {
    const strahl = buildPhaseTimeline({
      startDate: vorTagen(24),
      vegStartedAt: vorTagen(10),
      plannedVegDays: 0,
    }, JETZT)

    expect(strahl.dates.flip).toBe('—')
    expect(strahl.phases.find((phase) => phase.state === 'current')!.label).toBe('Wachstum · Tag 10')
  })

  it('gibt einem Klon keine Sämlingsphase', () => {
    // Bewurzelt heisst vegetativ — Keimblätter gab es nie.
    const strahl = buildPhaseTimeline({
      startDate: vorTagen(10),
      rootedAt: vorTagen(8),
      startMaterial: 'Clone',
    }, JETZT)

    expect(strahl.phases.some((phase) => phase.name === 'Sämling')).toBe(false)
    const laufend = strahl.phases.find((phase) => phase.state === 'current')!
    expect(laufend.name).toBe('Veg')
    expect(laufend.label).toBe('Wachstum · Tag 8')
  })

  it('lässt die Sämlingsdauer einstellen', () => {
    // Die 14 Tage sind ein Richtwert, kein Gesetz — typisch sind ein bis drei
    // Wochen. Wer es besser weiss, traegt den Uebergang ein; wer eine andere
    // Schaetzung braucht, reicht sie durch.
    const kurz = buildPhaseTimeline({ startDate: vorTagen(7), seedlingDays: 5 }, JETZT)

    expect(kurz.phases.find((phase) => phase.state === 'current')!.name).toBe('Veg')
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
        { startDate: vorTagen(120), flipDate: vorTagen(70), endDate: vorTagen(3) }, JETZT)

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
        { startDate: vorTagen(150), flipDate: vorTagen(100), endDate: vorTagen(25) }, JETZT)

      expect(strahl.phases.find((phase) => phase.name === 'Trocknen')!.state).toBe('done')
      const aushaerten = strahl.phases.find((phase) => phase.name === 'Aushärten')!
      expect(aushaerten.state).toBe('current')
      expect(aushaerten.label).toBe('Aushärten · Tag 16 von 30')
    })

    it('laesst einen bluehenden Grow nicht ins Trocknen rutschen', () => {
      // Ohne Erntedatum bleiben beide Vorschau — sonst stuende „Trocknen Tag 3"
      // an einem Grow, der noch in der Bluete haengt.
      const strahl = buildPhaseTimeline({ startDate: vorTagen(120), flipDate: vorTagen(70) }, JETZT)

      expect(strahl.phases.find((phase) => phase.name === 'Trocknen')!.state).toBe('planned')
      expect(strahl.phases.find((phase) => phase.name === 'Aushärten')!.state).toBe('planned')
      // Der Erntetermin ist geschaetzt, also auch das Fertig-Datum — aber es steht da.
      expect(strahl.dates.ready).not.toBe('—')
    })
  })
})
/**
 * Die App muss dieselbe Phase zeigen, die der Server rechnet.
 *
 * Zwei Fälle liefen auseinander, beide auf demselben Bildschirm sichtbar:
 * ein Autoflower stand für immer in der Veg, während die Kachel daneben
 * Blüte-Zielwerte zeigte; und ein Grow, den man mitten im Lauf eingetragen
 * hatte, fing im Strahl bei Tag 1 an, obwohl die App intern längst weiterzählte.
 */
describe('Zeitstrahl und Server sagen dasselbe', () => {
  const heute = new Date()
  const vorTagen = (n: number) => new Date(heute.getTime() - n * 86_400_000).toISOString().slice(0, 10)

  it('bringt einen Autoflower ohne Flip in die Blüte', () => {
    // Der Server rechnet: 28 Tage nach der Keimung blüht ein Autoflower.
    const timeline = buildPhaseTimeline({
      startDate: vorTagen(50),
      germinatedAt: vorTagen(50),
      seedType: 'Autoflower',
      flipDate: null,
    })

    const laufend = timeline.phases.find((phase) => phase.state === 'current')
    expect(laufend?.name, 'Autoflower steht nach 50 Tagen immer noch nicht in der Blüte').toBe('Blüte')
  })

  it('lässt einen Photoperiod ohne Flip in der Veg', () => {
    // Gegenprobe: ohne Autoflower gilt weiter, dass ein Flip nötig ist.
    const timeline = buildPhaseTimeline({
      startDate: vorTagen(50),
      germinatedAt: vorTagen(50),
      seedType: 'Feminized',
      flipDate: null,
    })

    expect(timeline.phases.find((phase) => phase.state === 'current')?.name).not.toBe('Blüte')
  })

  it('zählt mitgebrachte Tage der Einstiegsphase mit', () => {
    // „Ich trage einen Grow ein, der seit 20 Tagen in der Veg ist."
    const ohne = buildPhaseTimeline({ startDate: vorTagen(1), entryPoint: 'Veg' })
    const mit = buildPhaseTimeline({ startDate: vorTagen(1), entryPoint: 'Veg', daysAlreadyInPhase: 20 })

    const tag = (t: typeof ohne) => t.phases.find((phase) => phase.state === 'current')?.dayInPhase ?? 0
    expect(tag(mit), 'die mitgebrachten Tage werden ignoriert').toBeGreaterThan(tag(ohne))
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
    'im Sämling': buildPhaseTimeline({ startDate: vorTagen(5) }, JETZT),
    'im Wachstum ohne Plan': buildPhaseTimeline({ startDate: vorTagen(30) }, JETZT),
    'im Wachstum mit Flip in der Zukunft': buildPhaseTimeline({ startDate: vorTagen(30), flipDate: inTagen(20) }, JETZT),
    'im Sämling mit Plan': buildPhaseTimeline({ startDate: vorTagen(5), plannedVegDays: 28 }, JETZT),
    'in der Blüte': buildPhaseTimeline({ startDate: vorTagen(60), flipDate: vorTagen(20) }, JETZT),
    'geerntet': buildPhaseTimeline({ startDate: vorTagen(120), flipDate: vorTagen(80), endDate: vorTagen(3) }, JETZT),
  }

  it('nennt jede Phase so, wie phaseName sie nennt', () => {
    expect(PHASEN_ANZEIGE.Veg).toBe(phaseName('Veg'))
    expect(PHASEN_ANZEIGE.Sämling).toBe(phaseName('Seedling'))
    expect(PHASEN_ANZEIGE.Blüte).toBe(phaseName('Flower'))
    expect(PHASEN_ANZEIGE.Trocknen).toBe(phaseName('Dry'))
    expect(PHASEN_ANZEIGE.Aushärten).toBe(phaseName('Cure'))
  })

  it('schreibt in keine Beschriftung einen rohen Phasen-Wert', () => {
    // Mengenwächter: ohne Beschriftungen liefe die Suche ins Leere und wäre grün.
    const texte = Object.entries(lagen).flatMap(([lage, strahl]) =>
      strahl.phases.flatMap((phase) => [`${lage}: ${phase.label}`, `${lage}: ${phase.short}`]))
    expect(texte.length).toBeGreaterThanOrEqual(6 * 2 * 5)

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
