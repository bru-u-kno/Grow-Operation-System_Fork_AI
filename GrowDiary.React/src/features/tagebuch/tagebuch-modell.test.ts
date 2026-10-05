import { describe, expect, it } from 'vitest'
import type { TagebuchEreignisDto } from '../../types'
import { aenderung, markenFuer, passtZumFilter, tagTitel, wasserName, zellen } from './tagebuch-modell'

function ereignis(teil: Partial<TagebuchEreignisDto>): TagebuchEreignisDto {
  return {
    schluessel: 'x', art: 'messung', zeitpunktUtc: '', uhrzeit: '16:10', minute: 970, titel: '', wasser: false,
    messung: null, wechsel: null, addback: null, dosis: null, notiz: null, auffaellig: null, posten: [], fotos: [],
    ...teil,
  }
}

describe('Grow-Tagebuch — Modell', () => {
  it('Filter: Messwerte zeigen Messungen und Auffälliges, Wasser folgt dem Server-Kennzeichen', () => {
    expect(passtZumFilter(ereignis({ art: 'messung' }), 'messwerte')).toBe(true)
    expect(passtZumFilter(ereignis({ art: 'auffaellig' }), 'messwerte')).toBe(true)
    expect(passtZumFilter(ereignis({ art: 'wechsel' }), 'messwerte')).toBe(false)
    expect(passtZumFilter(ereignis({ art: 'wechsel', wasser: true }), 'wasser')).toBe(true)
    expect(passtZumFilter(ereignis({ art: 'messung', wasser: false }), 'wasser')).toBe(false)
    expect(passtZumFilter(ereignis({ art: 'notiz' }), 'notizen')).toBe(true)
    expect(passtZumFilter(ereignis({ art: 'messung' }), 'notizen')).toBe(false)
  })

  it('Änderung mit deutschem Komma und echtem Minus', () => {
    expect(aenderung(1.63, 1.15, 2)).toEqual({ text: '−0,48', richtung: 'runter' })
    expect(aenderung(6.12, 6.15, 2)).toEqual({ text: '+0,03', richtung: 'hoch' })
    expect(aenderung(6.12, 6.121, 2)).toEqual({ text: '±0', richtung: 'gleich' })
    expect(aenderung(null, 6.1, 2)).toBeNull()
  })

  it('Zellen nur für gemessene Felder, Zahlen deutsch', () => {
    const z = zellen({ ph: 6.2, ec: 1.7, wasserC: null, luftC: 23.66, feuchteProzent: null, co2Ppm: null, orpMv: null, sauerstoffMgL: null, fuellstandL: null, ppfd: null })
    expect(z).toEqual([
      { name: 'pH', wert: '6,20', einheit: null },
      { name: 'EC', wert: '1,70', einheit: 'mS/cm' },
      { name: 'Luft', wert: '23,7', einheit: '°C' },
    ])
  })

  it('Striche: Wasser blau, Notiz und Auffälliges gelb, automatische Notizen keine', () => {
    const marken = markenFuer([
      ereignis({ art: 'wechsel', minute: 1050 }),
      ereignis({ art: 'auffaellig', minute: 970 }),
      ereignis({ art: 'notiz', minute: 900, notiz: { id: 1, entryType: 'Action', titel: 'CO₂', text: null, occurredAtUtc: '', automatisch: true } }),
      ereignis({ art: 'messung', minute: 333 }),
    ])
    expect(marken).toEqual([{ minute: 1050, farbe: 'blau' }, { minute: 970, farbe: 'gelb' }])
  })

  it('Tagestitel und Wasserart ohne rohe Werte', () => {
    expect(tagTitel('2026-10-03', 'Samstag')).toBe('Samstag, 03.10.')
    expect(wasserName('Tap')).toBe('Leitungswasser')
    expect(wasserName('RO')).toBe('Osmose / VE-Wasser')
    expect(wasserName(null)).toBeNull()
  })
})
