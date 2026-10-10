import { describe, expect, it } from 'vitest'
import { anteilPlanDosis, mengeDerZeile, type AblaufZeile } from '../vorgang/ablauf-rechnung'
import {
  alterMinuten,
  alterText,
  ART_VON_MODUS,
  artikelFuerName,
  eigenesWasserZeile,
  fuellstand,
  ecWirkung,
  MODI,
  nachmessungZeit,
  nachfuellTagebuch,
  normalisiert,
  phLage,
  phZielText,
  planChips,
  uhrzeitText,
  zielAbstandEc,
  wirksameWerte,
} from './nachfuell-rechnung'

describe('Art je Fall', () => {
  it('jeder der drei Fälle hat seine Art — „nur Zusätze" ist die Korrektur ohne Wasser', () => {
    expect(ART_VON_MODUS).toEqual({ wasser: 'TopOff', mix: 'Addback', zusatz: 'Correction' })
    expect(MODI.map((m) => m.modus)).toEqual(['wasser', 'mix', 'zusatz'])
    // Der Name des dritten Falls nennt Purolyt und Co. mit: „Dünger/pH" war zu eng (Bru, 10.10.2026).
    expect(MODI[2].titel).toBe('Nur Zusätze (ohne Wasser)')
    expect(MODI[2].hinweis).toContain('Purolyt')
  })
})

describe('Artikel zu einem Namen', () => {
  const artikel = [{ id: 1, name: 'Leitungswasser' }, { id: 2, name: 'Purolyt' }, { id: 3, name: 'CANNA Aqua A' }]

  it('findet ohne Groß/Klein und ohne Leerraum am Rand', () => {
    expect(artikelFuerName(artikel, ' purolyt ')?.id).toBe(2)
    expect(artikelFuerName(artikel, 'canna  aqua a')?.id).toBe(3)
    expect(normalisiert('  Pro   Dukt ')).toBe('pro dukt')
  })

  it('findet nichts für einen unbekannten oder leeren Namen — dann fragt die Seite, ob sie ihn anlegen soll', () => {
    expect(artikelFuerName(artikel, 'Cannaboost')).toBeNull()
    expect(artikelFuerName(artikel, '')).toBeNull()
    expect(artikelFuerName(artikel, '   ')).toBeNull()
    // Ein Teil des Namens ist kein Treffer: „Canna" darf nicht an „CANNA Aqua A" hängen bleiben.
    expect(artikelFuerName(artikel, 'Canna')).toBeNull()
  })
})

describe('Füllstand', () => {
  it('danach wieder voll, vorher fehlen die Liter', () => {
    expect(fuellstand(120, '', 18)).toEqual({ danach: 120, vorher: 102 })
  })

  it('wer „danach" einträgt, überschreibt das Anlagevolumen — auch mit Komma', () => {
    expect(fuellstand(120, '118,5', 18)).toEqual({ danach: 118.5, vorher: 100.5 })
  })

  it('ohne Liter ist vorher gleich danach (nur Zusätze)', () => {
    expect(fuellstand(120, '', null)).toEqual({ danach: 120, vorher: 120 })
  })

  it('mehr Liter als der Tank fasst: kein „vorher" erfinden', () => {
    expect(fuellstand(120, '', 130)).toEqual({ danach: 120, vorher: null })
  })

  it('ohne Anlagevolumen oder bei Unlesbarem gibt es keine Zahl', () => {
    expect(fuellstand(null, '', 18)).toEqual({ danach: null, vorher: null })
    expect(fuellstand(120, '1x8', 18)).toEqual({ danach: null, vorher: null })
    expect(fuellstand(120, '0', 18)).toEqual({ danach: null, vorher: null })
  })
})

describe('Nachmessung', () => {
  it('fällt Minuten nach dem Zeitpunkt', () => {
    const zeit = nachmessungZeit('2026-10-10T13:19', 15)
    expect(zeit).not.toBeNull()
    expect(zeit!.getTime() - new Date('2026-10-10T13:19').getTime()).toBe(15 * 60_000)
  })

  it('nur ganze Minuten von 1 bis 240', () => {
    expect(nachmessungZeit('2026-10-10T13:19', 0)).toBeNull()
    expect(nachmessungZeit('2026-10-10T13:19', 241)).toBeNull()
    expect(nachmessungZeit('2026-10-10T13:19', 1.5)).toBeNull()
    expect(nachmessungZeit('2026-10-10T13:19', null)).toBeNull()
    expect(nachmessungZeit('kein Datum', 15)).toBeNull()
    expect(nachmessungZeit('2026-10-10T13:19', 240)).not.toBeNull()
  })

  it('schreibt die Uhrzeit — mit Tag, wenn es nicht heute ist', () => {
    const jetzt = new Date('2026-10-10T12:00')
    expect(uhrzeitText(new Date('2026-10-10T13:34'), jetzt)).toBe('13:34 Uhr')
    expect(uhrzeitText(new Date('2026-10-09T21:05'), jetzt)).toBe('09.10., 21:05 Uhr')
  })
})

describe('Alter eines Sensorwerts', () => {
  const jetzt = new Date('2026-10-10T12:00:00Z')
  it('in Minuten, nie negativ', () => {
    expect(alterMinuten('2026-10-10T11:56:00Z', jetzt)).toBe(4)
    expect(alterMinuten('2026-10-10T12:05:00Z', jetzt)).toBe(0)
  })
  it('liest sich wie gesprochen', () => {
    expect(alterText(0)).toBe('gerade eben')
    expect(alterText(4)).toBe('vor 4 min')
    expect(alterText(89)).toBe('vor 89 min')
    expect(alterText(150)).toBe('vor 3 Std.')
  })
})

describe('Eigene Wasserwerte im Text', () => {
  it('nennt nur, was eingetragen wurde, mit deutschen Zahlen', () => {
    expect(eigenesWasserZeile({ ec: '0,45', ph: '7,2', haerte: '', temp: '' })).toBe('Wasser (eigene Werte): EC 0,45\u00a0mS/cm · pH 7,20')
    expect(eigenesWasserZeile({ ec: '0,45', ph: '7,2', haerte: '8', temp: '19,5' })).toContain('Härte 8\u00a0°dH · Temperatur 19,5\u00a0°C')
  })
  it('ohne Werte keine Zeile', () => {
    expect(eigenesWasserZeile({ ec: '', ph: '', haerte: '', temp: '' })).toBeNull()
  })
})

describe('Tagebuchzeile', () => {
  const leer = { ec: null, ph: null, wt: null, do: null, orp: null }
  const basis = { liter: 18, wasserName: 'Leitungswasser', vorher: { ...leer, ec: 1.58, ph: 5.9 }, nachher: leer, zugaben: [{ name: 'Purolyt', menge: 12, einheit: 'ml' }], notiz: '' }

  it('nur Wasser heißt „Nachfüllen", mit Dünger „Addback"', () => {
    expect(nachfuellTagebuch('wasser', { ...basis, titel: '', zugaben: [] }).titel).toBe('Nachfüllen 18 L Leitungswasser')
    expect(nachfuellTagebuch('mix', { ...basis, titel: '' }).titel).toBe('Addback 18 L Leitungswasser')
  })

  it('ohne Wasser steht keine Literzahl im Titel — und die Zugaben im Text', () => {
    const zeile = nachfuellTagebuch('zusatz', { ...basis, titel: '', liter: 0 })
    expect(zeile.titel).toBe('Zusätze zugegeben')
    expect(zeile.titel).not.toMatch(/\bL\b/)
    expect(zeile.text).toContain('Zugaben: Purolyt 12\u00a0ml')
  })
})

describe('Abgewählte Zeilen zählen nicht', () => {
  const zeile = (name: string, ml: number): AblaufZeile => ({ schluessel: `plan:${name}`, name, art: 'plan', rolle: 'Grundduenger', vorschlagMl: ml, hinweis: null, artikelId: 1, einheit: 'ml' })
  const A = zeile('Aqua Flores A', 100)
  const B = zeile('Aqua Flores B', 100)

  it('ohne Abwahl bleibt der Vorschlag, mit Abwahl wird die Zeile zu 0', () => {
    expect(wirksameWerte({}, new Set())).toEqual({})
    const wirksam = wirksameWerte({}, new Set([A.schluessel]))
    expect(mengeDerZeile(A, wirksam)).toBe(0)
    expect(mengeDerZeile(B, wirksam)).toBe(100)
  })

  it('die Dosis sinkt, wenn ein Grunddünger abgewählt ist — der EC-Anteil rechnet nicht mit dem Vorschlag weiter', () => {
    expect(anteilPlanDosis([A, B], {})).toBe(1)
    expect(anteilPlanDosis([A, B], wirksameWerte({}, new Set([A.schluessel])))).toBe(0.5)
    expect(anteilPlanDosis([A, B], wirksameWerte({}, new Set([A.schluessel, B.schluessel])))).toBe(0)
  })

  it('eine eigene Menge wird von der Abwahl überstimmt, nicht umgekehrt', () => {
    expect(mengeDerZeile(A, wirksameWerte({ [A.schluessel]: '250' }, new Set([A.schluessel])))).toBe(0)
    expect(mengeDerZeile(A, wirksameWerte({ [A.schluessel]: '250' }, new Set()))).toBe(250)
  })
})

describe('Dein Plan heute (A-006, Plankasten)', () => {
  it('Blüte: „Tag 35" und „Blütewoche 5" — die Woche steht genau einmal', () => {
    expect(planChips({ phase: 'Bluete', tagInPhase: 35, wocheInPhase: 5 })).toEqual({ tag: 'Tag 35', woche: 'Blütewoche 5' })
  })

  it('andere Phasen nennen ihre Phase bei der Woche', () => {
    expect(planChips({ phase: 'Veg', tagInPhase: 9, wocheInPhase: 2 })).toEqual({ tag: 'Tag 9', woche: 'Woche 2 Wachstum' })
  })

  it('ohne Phasenanker oder ohne Tageszahl: keine Chips statt erfundener Zahlen', () => {
    expect(planChips(null)).toBeNull()
    expect(planChips({ phase: 'Bluete', tagInPhase: 0, wocheInPhase: 0 })).toBeNull()
  })

  it('Abstand zum EC-Ziel in Worten', () => {
    expect(zielAbstandEc(1.18, 1.2)).toBe('Jetzt im Tank: EC 1,18 · Ziel 1,20 → 0,02 darunter')
    expect(zielAbstandEc(1.35, 1.2)).toBe('Jetzt im Tank: EC 1,35 · Ziel 1,20 → 0,15 darüber')
    expect(zielAbstandEc(1.2, 1.2)).toBe('Jetzt im Tank: EC 1,20 · Ziel 1,20 → im Ziel')
    expect(zielAbstandEc(null, 1.2)).toBeNull()
    expect(zielAbstandEc(1.2, null)).toBeNull()
  })

  it('pH-Lage gegen das Band des Plans', () => {
    expect(phLage(6.06, 5.8, 6.2)).toBe('im Ziel')
    expect(phLage(5.5, 5.8, 6.2)).toBe('darunter')
    expect(phLage(6.5, 5.8, 6.2)).toBe('darüber')
    expect(phLage(6.06, null, 6.2)).toBeNull()
    expect(phLage(null, 5.8, 6.2)).toBeNull()
    // Punktziel: kein Band, also keine Lage und nur eine Zahl im Text
    expect(phLage(5.86, 5.8, 5.8)).toBeNull()
    expect(phZielText(5.8, 5.8)).toBe('5,8')
    expect(phZielText(5.8, 6.2)).toBe('5,8–6,2')
    expect(phZielText(null, 6.2)).toBeNull()
  })

  it('Wirkung auf den Tank: bleibt, sinkt, steigt', () => {
    expect(ecWirkung(1.18, 1.18)).toEqual({ text: 'Wirkung ±0,00 – EC bleibt', richtung: 'gleich' })
    expect(ecWirkung(1.18, 1.183)).toEqual({ text: 'Wirkung ±0,00 – EC bleibt', richtung: 'gleich' })
    expect(ecWirkung(1.18, 0.9697)).toEqual({ text: 'Wirkung −0,21 – EC sinkt', richtung: 'sinkt' })
    expect(ecWirkung(1.18, 1.3)).toEqual({ text: 'Wirkung +0,12 – EC steigt', richtung: 'steigt' })
    expect(ecWirkung(null, 1.3)).toBeNull()
  })
})
