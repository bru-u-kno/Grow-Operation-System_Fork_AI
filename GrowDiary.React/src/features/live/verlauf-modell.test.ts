import { describe, expect, it } from 'vitest'
import { naechsterZeitpunkt, schaltzeitAmTag } from './licht-restzeit'
import {
  MAX_BREITE, MIN_BREITE, MINUTE, QUELLE_SPIEL, STUNDE, TAG,
  abschnitte, abschnitteImFenster, achsenMarken, amZeiger, begrenzeFenster, blaettern, datenGrenzen, dunkelphasen,
  fensterFuerZeitraum, fensterUmAnker, imFenster, kannBlaettern, kurvenFarbe, lichtPhasen,
  lueckenGrenze, quelleWaehlen, spannenTeile, statistik, teilung, yTeilung, zahl, zeitraumBeiBreite,
  speicherSchluessel, wochenGrenzen, zielBrauchtLichtplan, zielbandFuer, zielbandStuecke, zielUrteil, zoomUm, zusammenfuehren, type Punkt,
} from './verlauf-modell'

/** Ortszeit — die Tests laufen in jeder Zeitzone, weil das Modell in Ortszeit rechnet. */
const ort = (j: number, m: number, d: number, h = 0, min = 0) => new Date(j, m - 1, d, h, min).getTime()

const reihe = (von: number, schritt: number, anzahl: number, wert = (i: number) => i): Punkt[] =>
  Array.from({ length: anzahl }, (_, i) => ({ t: von + i * schritt, v: wert(i) }))

describe('Spanne über dem Diagramm', () => {
  it('nennt am gleichen Kalendertag den Tag einmal und beide Uhrzeiten', () => {
    const teile = spannenTeile({ von: ort(2026, 10, 3, 0, 18), bis: ort(2026, 10, 3, 6, 18) })
    expect(teile.join(' ')).toBe('Sa 03.10. · 00:18 – 06:18')
    // Auch hier bricht eine zu schmale Zeile am Strich, nie zwischen Tag und Uhrzeit.
    expect(teile).toEqual(['Sa 03.10. · 00:18 –', '06:18'])
  })

  it('teilt über Mitternacht in zwei Teile, der Strich am Ende des ersten', () => {
    expect(spannenTeile({ von: ort(2026, 10, 2, 8, 18), bis: ort(2026, 10, 3, 8, 18) }))
      .toEqual(['Fr 02.10. 08:18 –', 'Sa 03.10. 08:18'])
  })

  it('gilt schon eine Minute nach Mitternacht als zwei Tage', () => {
    expect(spannenTeile({ von: ort(2026, 10, 2, 23, 30), bis: ort(2026, 10, 3, 0, 1) }))
      .toEqual(['Fr 02.10. 23:30 –', 'Sa 03.10. 00:01'])
  })

  it('lässt bei mehr als zwei Tagen die Uhrzeit weg', () => {
    expect(spannenTeile({ von: ort(2026, 9, 26, 8, 18), bis: ort(2026, 10, 3, 8, 18) }))
      .toEqual(['Sa 26.09. –', 'Sa 03.10.'])
  })

  it('schreibt den Wochentag ohne Punkt und ohne Komma', () => {
    const text = spannenTeile({ von: ort(2026, 10, 2, 8), bis: ort(2026, 10, 3, 8) }).join(' ')
    expect(text).not.toMatch(/[A-Za-z]\./)
    expect(text).not.toContain(',')
  })
})

describe('Fenster begrenzen', () => {
  const grenzen = { von: ort(2026, 9, 26), bis: ort(2026, 10, 3, 12) }

  it('hält mindestens 15 Minuten und schiebt nicht über „jetzt"', () => {
    const f = begrenzeFenster({ von: grenzen.bis - MINUTE, bis: grenzen.bis + MINUTE }, grenzen)
    expect(f.bis - f.von).toBe(MIN_BREITE)
    expect(f.bis).toBeLessThanOrEqual(grenzen.bis)
  })

  it('hält höchstens 7 Tage und nie mehr als die Daten', () => {
    const f = begrenzeFenster({ von: grenzen.von - 30 * TAG, bis: grenzen.bis }, grenzen)
    expect(f.bis - f.von).toBeLessThanOrEqual(MAX_BREITE)
    expect(f.von).toBeGreaterThanOrEqual(grenzen.von)
  })

  it('schiebt ein Fenster vor den Daten nach rechts', () => {
    const f = begrenzeFenster({ von: grenzen.von - 2 * STUNDE, bis: grenzen.von + 4 * STUNDE }, grenzen)
    expect(f).toEqual({ von: grenzen.von, bis: grenzen.von + 6 * STUNDE })
  })

  it('der Zeitraum steht rechtsbündig an den neuesten Daten', () => {
    expect(fensterFuerZeitraum('6h', grenzen)).toEqual({ von: grenzen.bis - 6 * STUNDE, bis: grenzen.bis })
  })

  it('blättert um eine Fensterbreite und hält an den Grenzen an', () => {
    const start = fensterFuerZeitraum('24h', grenzen)
    const zurueck = blaettern(start, -1, grenzen)
    expect(zurueck).toEqual({ von: start.von - TAG, bis: start.bis - TAG })
    expect(blaettern(start, 1, grenzen)).toEqual(start)
    expect(kannBlaettern(start, grenzen)).toEqual({ zurueck: true, vor: false })
    let f = start
    for (let i = 0; i < 20; i++) f = blaettern(f, -1, grenzen)
    expect(f.von).toBe(grenzen.von)
    expect(kannBlaettern(f, grenzen).zurueck).toBe(false)
  })

  it('erkennt den Zeitraum auch nach dem Blättern', () => {
    const f = blaettern(fensterFuerZeitraum('6h', grenzen), -1, grenzen)
    expect(zeitraumBeiBreite(f)).toBe('6h')
    expect(zeitraumBeiBreite({ von: 0, bis: 5 * STUNDE })).toBeNull()
  })
})

describe('Zoom', () => {
  const grenzen = { von: ort(2026, 9, 26), bis: ort(2026, 10, 3, 12) }
  const start = { von: ort(2026, 10, 2, 12), bis: ort(2026, 10, 3, 12) }

  it('das Mausrad hält die Zeit unter der Maus fest', () => {
    const maus = ort(2026, 10, 3, 0)
    const f = zoomUm(start, maus, 0.5, grenzen)
    expect(f.bis - f.von).toBe(12 * STUNDE)
    expect((maus - f.von) / (f.bis - f.von)).toBeCloseTo((maus - start.von) / (start.bis - start.von))
  })

  it('Zwei Finger: der Anker wandert mit dem Mittelpunkt (Verschieben) und die Breite folgt dem Abstand', () => {
    const anker = ort(2026, 10, 3, 0)
    // Finger gehen auf den doppelten Abstand → halbe Breite, Mittelpunkt bei 25 %.
    const f = fensterUmAnker(anker, 0.25, 12 * STUNDE, grenzen)
    expect(f.bis - f.von).toBe(12 * STUNDE)
    expect(f.von + 0.25 * (f.bis - f.von)).toBe(anker)
  })

  it('zoomt nicht enger als 15 Minuten und nicht weiter als die Daten, und bleibt dabei am Anker', () => {
    const anker = ort(2026, 10, 3, 0)
    const eng = fensterUmAnker(anker, 0.5, MINUTE, grenzen)
    expect(eng.bis - eng.von).toBe(MIN_BREITE)
    expect((eng.von + eng.bis) / 2).toBe(anker)
    const weit = zoomUm(start, anker, 1000, grenzen)
    expect(weit.bis - weit.von).toBeLessThanOrEqual(Math.min(MAX_BREITE, grenzen.bis - grenzen.von))
    expect(weit.von).toBeGreaterThanOrEqual(grenzen.von)
    expect(weit.bis).toBeLessThanOrEqual(grenzen.bis)
  })
})

describe('Teilung der Zeitachse', () => {
  it('wählt 15 min / 1 h / 4 h / 12 h / Tag nach der Fensterbreite', () => {
    expect(teilung(STUNDE).schrittMinuten).toBe(15)
    expect(teilung(6 * STUNDE).schrittMinuten).toBe(60)
    expect(teilung(TAG).schrittMinuten).toBe(240)
    expect(teilung(2 * TAG).schrittMinuten).toBe(720)
    expect(teilung(7 * TAG).schrittMinuten).toBe(1440)
  })

  it('richtet 4-h-Marken auf volle Ortszeit-Stunden aus, mit mindestens 52 px Abstand und nie über den Rand', () => {
    const f = { von: ort(2026, 10, 2, 8, 18), bis: ort(2026, 10, 3, 8, 18) }
    const marken = achsenMarken(f, 6, 300, 312)
    expect(marken.length).toBeGreaterThanOrEqual(3)
    for (const marke of marken) {
      expect(new Date(marke.t).getMinutes()).toBe(0)
      expect(new Date(marke.t).getHours() % 4).toBe(0)
      expect(marke.x - marke.text.length * 3.45).toBeGreaterThanOrEqual(0)
      expect(marke.x + marke.text.length * 3.45).toBeLessThanOrEqual(312)
    }
    for (let i = 1; i < marken.length; i++) expect(marken[i].x - marken[i - 1].x).toBeGreaterThanOrEqual(52)
  })

  it('beschriftet die Woche mit Tagen auf Mitternacht', () => {
    const marken = achsenMarken({ von: ort(2026, 9, 26, 8), bis: ort(2026, 10, 3, 8) }, 6, 700, 712)
    expect(marken.length).toBeGreaterThanOrEqual(4)
    for (const marke of marken) {
      expect(new Date(marke.t).getHours()).toBe(0)
      expect(marke.text).toMatch(/^[A-Z][a-z] \d\d\.\d\d\.$/)
    }
  })

  it('dünnt bei schmaler Breite aus, statt Texte übereinander zu legen', () => {
    const marken = achsenMarken({ von: ort(2026, 9, 26, 8), bis: ort(2026, 10, 3, 8) }, 6, 280, 292)
    for (let i = 1; i < marken.length; i++) {
      const luecke = (marken[i].x - marken[i].text.length * 3.45) - (marken[i - 1].x + marken[i - 1].text.length * 3.45)
      expect(luecke).toBeGreaterThan(0)
    }
  })

  it('y-Achse: runde Schritte, die die Werte einschließen', () => {
    const t = yTeilung(23.4, 27.9)
    expect([1, 2, 5].map((f) => f * 10 ** Math.floor(Math.log10(t.schritt)))).toContain(t.schritt)
    expect(t.unten).toBeLessThanOrEqual(23.4)
    expect(t.oben).toBeGreaterThanOrEqual(27.9)
    expect(t.marken.length).toBeGreaterThanOrEqual(3)
    expect(t.marken.length).toBeLessThanOrEqual(7)
    // Eine flache Reihe bekommt trotzdem eine Achse.
    expect(yTeilung(6, 6).oben).toBeGreaterThan(yTeilung(6, 6).unten)
  })
})

describe('Licht und Dunkelphase', () => {
  it('eine Nacht ist EIN Streifen', () => {
    const f = { von: ort(2026, 10, 2, 12), bis: ort(2026, 10, 3, 12) }
    expect(dunkelphasen(f, '08:00', '20:00')).toEqual([{ von: ort(2026, 10, 2, 20), bis: ort(2026, 10, 3, 8) }])
  })

  it('Licht über Mitternacht (an 20:00, aus 08:00): die Nacht liegt am Tag', () => {
    const f = { von: ort(2026, 10, 2, 0), bis: ort(2026, 10, 3, 0) }
    expect(dunkelphasen(f, '20:00', '08:00')).toEqual([{ von: ort(2026, 10, 2, 8), bis: ort(2026, 10, 2, 20) }])
    expect(lichtPhasen(f, '20:00', '08:00')).toEqual([
      { von: ort(2026, 10, 2, 0), bis: ort(2026, 10, 2, 8) },
      { von: ort(2026, 10, 2, 20), bis: ort(2026, 10, 3, 0) },
    ])
  })

  it('über sieben Tage je Nacht ein Stück', () => {
    const f = { von: ort(2026, 9, 26, 12), bis: ort(2026, 10, 3, 12) }
    expect(dunkelphasen(f, '08:00', '20:00')).toHaveLength(7)
  })

  it('ohne Lichtplan keine Nacht und keine Lichtphasen', () => {
    const f = { von: ort(2026, 10, 2), bis: ort(2026, 10, 3) }
    expect(dunkelphasen(f, null, '20:00')).toEqual([])
    expect(lichtPhasen(f, '08:00', 'kaputt')).toBeNull()
    expect(lichtPhasen(f, '08:00', '08:00')).toBeNull()
  })
})

describe('Zielband', () => {
  /* Die Testobjekte sind so gebaut, wie das Backend sie WIRKLICH liefert
     (GET /api/live/tents/1 im Demobestand, `KachelZiele.ZieleSetzen`):
     `targetMin`/`targetMax` ist das Band der GERADE gültigen Phase. */
  const luftNachts = { targetMin: 19, targetMax: 19, targetDayMin: 23, targetDayMax: 23, targetNightMin: 19, targetNightMax: 19, targetPhase: 'night' }
  const feuchte = { targetMax: 50, targetDayMax: 50, targetNightMax: 50, targetPhase: 'night' }
  const ph = { targetMin: 5.8, targetMax: 6.2 }

  it('nimmt je Phase das eigene Band', () => {
    expect(zielbandFuer(luftNachts, 'tag')).toEqual({ min: 23, max: 23 })
    expect(zielbandFuer(luftNachts, 'nacht')).toEqual({ min: 19, max: 19 })
  })

  it('nimmt das Band der aktuellen Phase NICHT als Ersatz für die andere', () => {
    const nurTag = { targetMin: 24, targetMax: 28, targetDayMin: 24, targetDayMax: 28 }
    expect(zielbandFuer(nurTag, 'nacht')).toBeNull()
  })

  it('ohne Tag/Nacht gilt das eine Band', () => {
    expect(zielbandFuer(ph, 'nacht')).toEqual({ min: 5.8, max: 6.2 })
  })

  it('ohne Ziel kein Band', () => {
    expect(zielbandFuer({}, 'tag')).toBeNull()
    expect(zielbandFuer(null, 'tag')).toBeNull()
    expect(zielbandStuecke({ von: 0, bis: TAG }, { targetMin: null, targetMax: null }, null)).toEqual([])
  })

  it('ein einseitiges Ziel bleibt einseitig', () => {
    expect(zielbandFuer(feuchte, 'tag')).toEqual({ min: null, max: 50 })
  })

  it('zeichnet nachts das Nachtband und am Tag das Tagband', () => {
    const f = { von: ort(2026, 10, 2, 12), bis: ort(2026, 10, 3, 12) }
    const licht = lichtPhasen(f, '08:00', '20:00')
    expect(zielbandStuecke(f, luftNachts, licht)).toEqual([
      { von: ort(2026, 10, 2, 12), bis: ort(2026, 10, 2, 20), min: 23, max: 23 },
      { von: ort(2026, 10, 2, 20), bis: ort(2026, 10, 3, 8), min: 19, max: 19 },
      { von: ort(2026, 10, 3, 8), bis: ort(2026, 10, 3, 12), min: 23, max: 23 },
    ])
  })

  it('ohne Lichtplan und mit verschiedenem Tag- und Nachtziel: KEIN Band', () => {
    // Früher stand hier das Band der aktuellen Phase rund um die Uhr — nachts
    // um 3 Uhr gezeichnet hieß das: auch am Mittag 19 °C als Ziel.
    expect(zielBrauchtLichtplan(luftNachts)).toBe(true)
    expect(zielbandStuecke({ von: 0, bis: TAG }, luftNachts, null)).toEqual([])
  })

  it('ohne Lichtplan und mit gleichem Tag- und Nachtziel: ganztags', () => {
    expect(zielBrauchtLichtplan(feuchte)).toBe(false)
    expect(zielbandStuecke({ von: 0, bis: TAG }, feuchte, null)).toEqual([{ von: 0, bis: TAG, min: null, max: 50 }])
    expect(zielbandStuecke({ von: 0, bis: TAG }, ph, null)).toEqual([{ von: 0, bis: TAG, min: 5.8, max: 6.2 }])
  })

  it('sagt im Fokus, wo der Wert zum Ziel steht', () => {
    expect(zielUrteil(55, { min: null, max: 50 }, 0, '%')).toEqual({ text: 'über dem Ziel (bis 50 %)', imZiel: false })
    expect(zielUrteil(17.5, { min: 18, max: null }, 1, '°C')).toEqual({ text: 'unter dem Ziel (ab 18 °C)', imZiel: false })
    expect(zielUrteil(20, { min: 18, max: 24 }, 1, '°C')).toEqual({ text: 'im Ziel (18–24 °C)', imZiel: true })
    expect(zielUrteil(24.7, { min: 23, max: 23 }, 1, '°C')).toEqual({ text: 'Soll 23 °C', imZiel: false })
    expect(zielUrteil(6.0, { min: 5.8, max: 6.2 }, 2, null)).toEqual({ text: 'im Ziel (5,8–6,2)', imZiel: true })
    expect(zielUrteil(null, { min: 1, max: 2 }, 1, null)).toBeNull()
  })
})

describe('Zone der Lichtzeiten', () => {
  it('rechnet die Schaltzeiten in der Zone des Servers, nicht des Browsers', () => {
    const f = { von: Date.UTC(2026, 9, 2, 12), bis: Date.UTC(2026, 9, 3, 12) }
    // Server in UTC (Versatz 0): Nacht 20:00–08:00 UTC, egal wo der Browser steht.
    expect(dunkelphasen(f, '08:00', '20:00', 0)).toEqual([{ von: Date.UTC(2026, 9, 2, 20), bis: Date.UTC(2026, 9, 3, 8) }])
    // Lichtplan in Berlin (Sommerzeit, +120): 20:00 Berlin = 18:00 UTC.
    expect(dunkelphasen(f, '08:00', '20:00', 120)).toEqual([{ von: Date.UTC(2026, 9, 2, 18), bis: Date.UTC(2026, 9, 3, 6) }])
  })

  it('über Mitternacht und mit negativem Versatz', () => {
    const f = { von: Date.UTC(2026, 9, 2, 0), bis: Date.UTC(2026, 9, 3, 0) }
    // New York (−240): Licht 20:00–08:00 dort = 00:00–12:00 UTC.
    expect(lichtPhasen(f, '20:00', '08:00', -240)).toEqual([{ von: Date.UTC(2026, 9, 2, 0), bis: Date.UTC(2026, 9, 2, 12) }])
  })

  it('Kachel und Diagramm nehmen dieselbe Funktion', () => {
    const jetzt = new Date(Date.UTC(2026, 9, 2, 19, 0))
    expect(naechsterZeitpunkt(jetzt, '20:00', 0)?.getTime()).toBe(Date.UTC(2026, 9, 2, 20))
    expect(naechsterZeitpunkt(jetzt, '20:00', 120)?.getTime()).toBe(Date.UTC(2026, 9, 3, 18))
    expect(schaltzeitAmTag(jetzt.getTime(), { stunde: 20, minute: 0 }, 0)).toBe(lichtPhasen({ von: jetzt.getTime() - STUNDE, bis: jetzt.getTime() + 2 * STUNDE }, '08:00', '20:00', 0)![0].bis)
  })
})

describe('Lücken', () => {
  it('überbrückt eine Lücke von mehr als drei Takten nicht', () => {
    const vorher = reihe(0, 5 * MINUTE, 10)
    const nachher = reihe(9 * 5 * MINUTE + 2 * STUNDE, 5 * MINUTE, 10)
    const stuecke = abschnitte([...vorher, ...nachher])
    expect(stuecke).toHaveLength(2)
    expect(stuecke[0]).toHaveLength(10)
  })

  it('ein einzelner fehlender Punkt ist keine Lücke', () => {
    const punkte = reihe(0, 5 * MINUTE, 20).filter((_, i) => i !== 7)
    expect(abschnitte(punkte)).toHaveLength(1)
  })

  it('zwei Auflösungen in einer Reihe (stündlich, dann viertelstündlich) sind keine Lücke', () => {
    const grob = reihe(0, STUNDE, 30)
    const fein = reihe(30 * STUNDE, 15 * MINUTE, 40)
    expect(abschnitte([...grob, ...fein])).toHaveLength(1)
  })

  it('der Takt ist der örtliche Median', () => {
    expect(lueckenGrenze(reihe(0, 5 * MINUTE, 10), 4)).toBe(15 * MINUTE)
    expect(lueckenGrenze([{ t: 0, v: 1 }, { t: 1, v: 1 }], 0)).toBe(Infinity)
  })

  it('der Zeiger zeigt in einer Lücke nichts statt einer alten Zahl', () => {
    const punkte = [...reihe(0, 5 * MINUTE, 10), ...reihe(3 * STUNDE, 5 * MINUTE, 10)]
    expect(amZeiger(punkte, 2 * STUNDE)).toBeNull()
    expect(amZeiger(punkte, 3 * STUNDE + 6 * MINUTE)?.t).toBe(3 * STUNDE + 5 * MINUTE)
  })
})

describe('Ausschnitt und Statistik', () => {
  const punkte = reihe(0, 10 * MINUTE, 13, (i) => (i === 6 ? 30 : 20 + i * 0.1))

  it('nimmt je einen Nachbarn links und rechts mit', () => {
    const f = { von: 25 * MINUTE, bis: 55 * MINUTE }
    expect(imFenster(punkte, f).map((p) => p.t / MINUTE)).toEqual([20, 30, 40, 50, 60])
  })

  it('Max / Min / Ø nur streng im Ausschnitt', () => {
    const s = statistik(punkte, { von: 25 * MINUTE, bis: 55 * MINUTE })
    expect(s).not.toBeNull()
    expect(s!.min).toBeCloseTo(20.3)
    expect(s!.max).toBeCloseTo(20.5)
    expect(s!.mittel).toBeCloseTo(20.4)
  })

  it('zählt Werte über eine Lücke hinweg, nie die Lücke selbst', () => {
    const mitLuecke = [...reihe(0, 5 * MINUTE, 3, () => 10), ...reihe(5 * STUNDE, 5 * MINUTE, 3, () => 20)]
    expect(statistik(mitLuecke, { von: 0, bis: 6 * STUNDE })!.mittel).toBe(15)
  })

  it('ohne Werte im Ausschnitt: null statt NaN', () => {
    expect(statistik(punkte, { von: 10 * STUNDE, bis: 11 * STUNDE })).toBeNull()
  })

  it('schreibt Zahlen deutsch mit fester Stellenzahl', () => {
    expect(zahl(24.56, 1)).toBe('24,6')
    expect(zahl(5.8, 2)).toBe('5,80')
    expect(zahl(1020, 0)).toBe('1020')
    expect(zahl(null, 1)).toBe('–')
  })
})

describe('Quelle wählen', () => {
  const tagVon = ort(2026, 10, 2, 12)

  it('bis 24 h aus den geladenen Rohwerten', () => {
    expect(quelleWaehlen({ von: tagVon + STUNDE, bis: tagVon + 7 * STUNDE }, tagVon)).toBe('24h')
    expect(quelleWaehlen({ von: tagVon, bis: tagVon + TAG }, tagVon)).toBe('24h')
  })

  it('7 Tage und alles, was älter als die 24 h ist, aus dem Nachladen', () => {
    expect(quelleWaehlen({ von: tagVon - 6 * TAG, bis: tagVon + TAG }, tagVon)).toBe('7t')
    expect(quelleWaehlen({ von: tagVon - STUNDE, bis: tagVon + 5 * STUNDE }, tagVon)).toBe('7t')
    expect(quelleWaehlen({ von: tagVon - QUELLE_SPIEL - 1, bis: tagVon + TAG }, tagVon)).toBe('7t')
  })

  it('die Grundansicht „24 Std" beginnt eine Spur vor dem ersten Punkt und braucht trotzdem keine 7 Tage', () => {
    expect(quelleWaehlen({ von: tagVon - 10 * MINUTE, bis: tagVon - 10 * MINUTE + TAG }, tagVon)).toBe('24h')
    expect(quelleWaehlen({ von: tagVon, bis: tagVon + STUNDE }, null)).toBe('7t')
  })

  it('führt 7 Tage und frische 24 h zusammen, die 24 h gelten', () => {
    const woche = reihe(0, STUNDE, 10, () => 1)
    const tag = reihe(5 * STUNDE, 30 * MINUTE, 12, () => 2)
    const alle = zusammenfuehren(woche, tag)
    expect(alle.filter((p) => p.t >= 5 * STUNDE).every((p) => p.v === 2)).toBe(true)
    expect(alle.filter((p) => p.v === 1)).toHaveLength(5)
  })

  it('die Grenzen sind die Daten, die da sind — vor dem Nachladen nur die 24 h', () => {
    const tag = reihe(10 * TAG, 15 * MINUTE, 96)
    const ohne = datenGrenzen([tag])!
    expect(ohne).toEqual({ von: 10 * TAG, bis: 10 * TAG + 95 * 15 * MINUTE })
    expect(wochenGrenzen(ohne).bis - wochenGrenzen(ohne).von).toBe(MAX_BREITE)
    const woche = reihe(8 * TAG, STUNDE, 48)
    expect(datenGrenzen([zusammenfuehren(woche, tag)])!.von).toBe(8 * TAG)
    expect(datenGrenzen([[]])).toBeNull()
  })

  it('beschneidet die vorab gerechneten Abschnitte auf den Ausschnitt', () => {
    const punkte = [...reihe(0, 5 * MINUTE, 10), ...reihe(3 * STUNDE, 5 * MINUTE, 10)]
    const stuecke = abschnitte(punkte)
    expect(abschnitteImFenster(stuecke, { von: 20 * MINUTE, bis: 30 * MINUTE }).map((s) => s.map((p) => p.t / MINUTE)))
      .toEqual([[15, 20, 25, 30, 35]])
    expect(abschnitteImFenster(stuecke, { von: 0, bis: 4 * STUNDE })).toHaveLength(2)
    expect(abschnitteImFenster(stuecke, { von: STUNDE, bis: 2 * STUNDE })).toEqual([])
  })
})

describe('Farben', () => {
  it('Wassertemperatur und Lufttemperatur sind verschieden', () => {
    expect(kurvenFarbe('reservoir-temp', 0)).not.toBe(kurvenFarbe('temperature', 0))
  })

  it('jede Messgröße mit Verlauf hat eine eigene Farbe', async () => {
    const { VERLAUFS_METRIKEN } = await import('./useTentSparklines')
    const farben = VERLAUFS_METRIKEN.map((key, i) => kurvenFarbe(key, i))
    expect(farben.length).toBeGreaterThanOrEqual(10)
    expect(new Set(farben).size).toBe(farben.length)
  })
})

describe('Gemerkte Auswahl', () => {
  it('hängt an den Werten der Kachel: ändert jemand die Kachel, gilt deren neue Auswahl', () => {
    expect(speicherSchluessel('verlauf', ['temperature', 'humidity'])).toBe(speicherSchluessel('verlauf', ['humidity', 'temperature']))
    expect(speicherSchluessel('verlauf', ['temperature', 'humidity'])).not.toBe(speicherSchluessel('verlauf', ['temperature', 'humidity', 'co2']))
  })
})
