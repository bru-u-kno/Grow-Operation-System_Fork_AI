import { describe, expect, it } from 'vitest'
import { abweichungsText } from './entfeuchter-abweichungen'
import type { EntfeuchterAbweichung } from './steuerung-typen'

const a = (name: string, forkWert: number, haWert: number, einheit: string): EntfeuchterAbweichung =>
  ({ entity: `input_number.${name}`, name, forkWert, haWert, einheit })

describe('abweichungsText', () => {
  it('nennt je Helfer den Wert hier und in Home Assistant, deutsch formatiert', () => {
    expect(abweichungsText([a('Feuchte EIN · Tag', 60, 30, '%'), a('Höchsttemperatur Tag', 29.5, 15, '°C')]))
      .toBe('Feuchte EIN · Tag: hier 60 %, in Home Assistant 30 % · Höchsttemperatur Tag: hier 29,5 °C, in Home Assistant 15 °C')
  })

  it('kürzt nach drei Einträgen und zählt den Rest', () => {
    const liste = Array.from({ length: 5 }, (_, i) => a(`Wert ${i + 1}`, 10, 5, 'min'))
    const text = abweichungsText(liste)
    expect(text.split(' · ')).toHaveLength(4)
    expect(text.endsWith('und 2 weitere')).toBe(true)
  })

  it('lässt die Einheit weg, wenn es keine gibt', () => {
    expect(abweichungsText([a('Faktor', 2, 1, '')])).toBe('Faktor: hier 2, in Home Assistant 1')
  })

  it('liefert für eine leere Liste nichts', () => {
    expect(abweichungsText([])).toBe('')
  })
})
