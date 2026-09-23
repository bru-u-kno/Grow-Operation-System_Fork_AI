import { describe, expect, it } from 'vitest'
import { bluelabPaare, co2Stufen, gruppieren, wertText, zeileAus } from './uebergabe-anzeige'

describe('Übergabe aus dem Plan (F-038)', () => {
  it('ordnet Grenzwert-Zeilen der Gruppe Grenzwerte zu und kürzt die Namen', () => {
    const z = zeileAus({ rolle: 'luft-nacht-unten', name: 'Grenzwert Luft unten (Nacht)', entityId: 'zelt:1/temperature/nacht-min', wert: '17', zustand: 'folgt dem Plan' })
    expect(z).toMatchObject({ ziel: 'grenzwerte', name: 'Lufttemperatur unten', zusatz: 'Nacht', einheit: '°C' })
  })

  it('kennt unbekannte Rollen trotzdem', () => {
    const g = gruppieren([
      { rolle: 'neu-ha', name: 'Etwas Neues', entityId: 'input_number.x', wert: '3', zustand: 'folgt dem Plan' },
      { rolle: 'neu-zelt', name: 'Grenze Neu', entityId: 'zelt:1/x/min', wert: '4', zustand: 'folgt dem Plan' },
    ])
    expect(g.ha[0].name).toBe('Etwas Neues')
    expect(g.ha[0].einheit).toBeNull()
    expect(g.grenzwerte[0].name).toBe('Grenze Neu')
  })

  it('setzt ein echtes Minuszeichen', () => {
    expect(wertText('-1')).toBe('−1')
    expect(wertText('1,2')).toBe('1,2')
  })

  it('markiert die geltende CO₂-Stufe', () => {
    const s = co2Stufen(660, 840, 960, 840)
    expect(s.map((x) => x.aktiv)).toEqual([false, true, false])
    expect(co2Stufen(660, 840, 960, null).some((x) => x.aktiv)).toBe(false)
  })

  it('fasst Bluelab-Grenzen zu Paaren und prüft das Gerät', () => {
    const p = bluelabPaare([
      { rolle: 'ec_low', soll: 1.2, geraet: 1.2 },
      { rolle: 'ec_high', soll: 1.8, geraet: 1.3 },
      { rolle: 'ph_low', soll: 5.6, geraet: 5.6 },
      { rolle: 'ph_high', soll: 6.4, geraet: 6.4 },
    ])
    expect(p.map((x) => [x.name, x.stimmt])).toEqual([['pH unten · oben', true], ['EC unten · oben', false]])
  })
})
