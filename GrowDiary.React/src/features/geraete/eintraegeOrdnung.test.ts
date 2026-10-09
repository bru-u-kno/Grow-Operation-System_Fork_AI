import { describe, expect, it } from 'vitest'
import { domaeneVon, kurzerZweck, ordneEintraege, titelVon } from './eintraegeOrdnung'

const e = (entityId: string, ...zwecke: string[]) => ({
  entityId,
  verwendungen: zwecke.map((zweck) => ({ zweck, quelle: zweck.startsWith('Steuerung') ? 'steuerung' : 'messgroesse' })),
})

describe('Eintraege unter einem Geraet', () => {
  it('kuerzt Zwecke auf das, was der Nutzer lesen will', () => {
    expect(kurzerZweck('Steuerung BLUELAB · EC · oben')).toBe('EC · oben')
    expect(kurzerZweck('Messgröße ReservoirEc')).toBe('ReservoirEc')
    expect(kurzerZweck('Stromzähler')).toBe('Stromzähler')
  })

  it('nimmt die Domaene aus der Entity-ID', () => {
    expect(domaeneVon('binary_sensor.big_port_5_zustand')).toBe('binary_sensor')
    expect(domaeneVon('ohne_punkt')).toBe('')
  })

  it('ordnet das Bluelab-Geraet in Messwerte und Einstellungen, sortiert nach Zweck', () => {
    const gruppen = ordneEintraege([
      e('number.bluelab_guardian_temp_low_alarm', 'Steuerung BLUELAB · Wassertemperatur · unten'),
      e('number.bluelab_guardian_ec_high_alarm', 'Steuerung BLUELAB · EC · oben'),
      e('sensor.bluelab_guardian_ph', 'Messgröße ReservoirPh'),
      e('number.bluelab_guardian_ph_high_alarm', 'Steuerung BLUELAB · pH · oben'),
      e('sensor.bluelab_guardian_electrical_conductivity', 'Messgröße ReservoirEc'),
      e('number.bluelab_guardian_ec_low_alarm', 'Steuerung BLUELAB · EC · unten'),
    ])

    expect(gruppen.map((g) => g.label)).toEqual(['Messwerte', 'Einstellungen'])
    expect(gruppen[0].eintraege.map((x) => x.titel)).toEqual(['ReservoirEc', 'ReservoirPh'])
    expect(gruppen[1].eintraege.map((x) => x.titel)).toEqual(['EC · oben', 'EC · unten', 'pH · oben', 'Wassertemperatur · unten'])
  })

  it('haelt die Gruppenreihenfolge fest und laesst leere Gruppen weg', () => {
    const gruppen = ordneEintraege([
      e('camera.pro'), e('script.edenic_set_alarm'), e('switch.dehumi'), e('number.x', 'Steuerung A · b'), e('sensor.y', 'Messgröße Z'), e('weird.thing'),
    ])
    expect(gruppen.map((g) => g.label)).toEqual(['Messwerte', 'Einstellungen', 'Schalter', 'Skripte', 'Kameras', 'Sonstige'])
    expect(ordneEintraege([e('sensor.a')]).map((g) => g.label)).toEqual(['Messwerte'])
  })

  it('stellt Eintraege mit Zweck vor die ohne und sortiert diese nach ID', () => {
    const [gruppe] = ordneEintraege([e('sensor.b_ohne'), e('sensor.z_mit', 'Messgröße A'), e('sensor.a_ohne')])
    expect(gruppe.eintraege.map((x) => x.entitaet.entityId)).toEqual(['sensor.z_mit', 'sensor.a_ohne', 'sensor.b_ohne'])
    expect(gruppe.eintraege[1].titel).toBeNull()
  })

  it('nimmt den ersten Zweck als Titel und zaehlt Zahlen natuerlich', () => {
    expect(titelVon(e('sensor.t', 'Messgröße ReservoirWaterTemp', 'Steuerung CHILLER · Wasserfühler'))).toBe('ReservoirWaterTemp')
    const [g] = ordneEintraege([e('sensor.a', 'Messgröße Port 10'), e('sensor.b', 'Messgröße Port 2')])
    expect(g.eintraege.map((x) => x.titel)).toEqual(['Port 2', 'Port 10'])
  })
})
