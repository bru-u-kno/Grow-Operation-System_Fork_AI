import { describe, expect, it } from 'vitest'
import type { HomeAssistantEntity } from '../../types'
import { entitaetsOptionen, GRUPPE_ANDERE, GRUPPE_PASST, HINWEIS_NICHTS_PASSENDES } from './entitaets-optionen'
import { begriffeAusRollenname, rangiere, vorbereiten } from './entitaets-passung'
import type { Kriterium } from './entitaets-passung'

/** Eine Entität wie `GET /api/home-assistant/entities` sie liefert; Domain aus der Kennung. */
function e(entityId: string, friendlyName: string | null, unit: string | null = null, klasse: string | null = null, state = '1'): HomeAssistantEntity {
  return { entityId, friendlyName, state, unitOfMeasurement: unit, deviceClass: klasse, domain: entityId.split('.')[0] }
}

/** Eine fremde Anlage: englische und deutsche Namen, dazu das, was Home Assistant selbst mitbringt. */
const ANLAGE: HomeAssistantEntity[] = [
  e('sensor.backup_backup_manager_state', 'Backup Backup Manager state', null, 'enum', 'idle'),
  e('sensor.sun_next_dawn', 'Sun Next dawn', null, 'timestamp', '2026-10-18T05:12:00+00:00'),
  e('sensor.sun_next_dusk', 'Sun Next dusk', null, 'timestamp', '2026-10-17T17:20:00+00:00'),
  e('sensor.handy_batterie', 'Handy Batterie', '%', 'battery', '80'),
  e('sensor.wohnzimmer_luftfeuchtigkeit', 'Wohnzimmer Luftfeuchtigkeit', '%', 'humidity', '45'),
  e('sensor.zelt_luftfeuchtigkeit', 'Zelt Luftfeuchtigkeit', '%', 'humidity', '65'),
  e('sensor.aussen_luftfeuchtigkeit', 'Außen Luftfeuchtigkeit', '%', 'humidity', '70'),
  e('sensor.keller_luftfeuchtigkeit', 'Keller Luftfeuchtigkeit', '%', 'humidity', '58'),
  e('sensor.zelt_temperatur', 'Zelt Temperatur', '°C', 'temperature', '24.5'),
  e('sensor.aussen_temperatur', 'Außen Temperatur', '°C', 'temperature', '12'),
  e('sensor.keller_temperatur', 'Keller Temperatur', '°C', 'temperature', '16'),
  e('sensor.co2_zelt', 'CO2 Zelt', 'ppm', 'carbon_dioxide', '820'),
  e('sensor.nahrlosung_temperatur', 'Nährlösung Temperatur', '°C', 'temperature', '19'),
  e('sensor.zelt_vpd', 'Zelt VPD', 'kPa', null, '1.1'),
  e('sensor.luftdruck', 'Luftdruck', 'hPa', 'pressure', '1012'),
  e('switch.entfeuchter_steckdose', 'Entfeuchter Steckdose', null, null, 'off'),
  e('switch.zuluft_lufter', 'Zuluft Lüfter', null, null, 'on'),
  e('switch.co2_ventil', 'CO2 Ventil', null, null, 'off'),
  e('switch.kaffeemaschine', 'Kaffeemaschine', null, null, 'off'),
  e('light.growlampe', 'Growlampe', null, null, 'on'),
  e('binary_sensor.haustuer', 'Haustür', null, 'door', 'off'),
]

const vor = (liste: HomeAssistantEntity[]) => liste.map(vorbereiten)
const ids = (liste: HomeAssistantEntity[]) => liste.map((x) => x.entityId)

const feuchteZelt: Kriterium = { domains: ['sensor'], einheit: '%', label: 'Zeltfühler · Feuchte' }
const co2: Kriterium = { domains: ['sensor'], einheit: 'ppm', label: 'CO₂-Sensor' }
const entfeuchterSchalten: Kriterium = { domains: ['select', 'switch', 'input_boolean'], label: 'Entfeuchter · schalten' }
const lampeZustand: Kriterium = { domains: ['binary_sensor', 'switch', 'light'], label: 'Lampe · Zustand' }

describe('Rollenname → Begriffe', () => {
  it.each([
    ['Zeltfühler · Feuchte', ['feuchte', 'zelt']],
    ['CO₂-Sensor', ['co2']],
    ['Kellerfühler · Temperatur', ['temperatur', 'keller']],
    ['Außenfühler · Feuchte', ['feuchte', 'aussen']],
    ['Zuluft-Lüfter · laufende Stufe', ['zuluft', 'luefter', 'stufe']],
    ['Entfeuchter · schalten', ['entfeuchter', 'schalter']],
    ['Lampe · Zustand', ['licht']],
    ['Wasserfühler', ['wasser']],
    ['Wassertemperatur · unten', ['temperatur', 'wasser', 'unten']],
  ])('%s', (label, erwartet) => {
    expect([...begriffeAusRollenname(label)].sort()).toEqual([...erwartet].sort())
  })
})

describe('Messfühler (Einheit)', () => {
  it('Zeltfühler · Feuchte: der Zeltfühler steht ganz oben, Batterie-% und Sonne nicht unter "passt"', () => {
    const { passt, andere } = rangiere(vor(ANLAGE), feuchteZelt)
    expect(passt[0].entityId).toBe('sensor.zelt_luftfeuchtigkeit')
    expect(ids(passt)).toEqual([
      'sensor.zelt_luftfeuchtigkeit',
      // Ort unbekannt vor Ort falsch: "Außen" und "Keller" nennen einen anderen Ort als das Zelt.
      'sensor.wohnzimmer_luftfeuchtigkeit',
      'sensor.aussen_luftfeuchtigkeit',
      'sensor.keller_luftfeuchtigkeit',
    ])
    expect(ids(passt)).not.toContain('sensor.handy_batterie')
    // Die Sonne und die Sicherung stehen am Ende der anderen.
    expect(ids(andere).slice(-3).sort()).toEqual(
      ['sensor.backup_backup_manager_state', 'sensor.sun_next_dawn', 'sensor.sun_next_dusk'].sort())
  })

  it('ohne Zelt im Namen: "Luftfeuchte" vor "Außen Luftfeuchte" (Ort der Rolle nicht genannt, Außen ist es aber sicher nicht)', () => {
    const liste = [
      e('sensor.demo_outside_humidity', 'Demo Controller Außen Luftfeuchte', '%'),
      e('sensor.demo_humidity', 'Demo Luftfeuchte', '%'),
    ]
    expect(ids(rangiere(vor(liste), { domains: ['sensor'], einheit: '%', label: 'Luftfeuchte' }).passt))
      .toEqual(['sensor.demo_humidity', 'sensor.demo_outside_humidity'])
    // Und umgekehrt, wenn die Rolle den Ort nennt.
    expect(ids(rangiere(vor(liste), { domains: ['sensor'], einheit: '%', label: 'Außenfühler · Feuchte' }).passt))
      .toEqual(['sensor.demo_outside_humidity', 'sensor.demo_humidity'])
  })

  it('CO₂-Sensor: CO2 Zelt steht oben', () => {
    expect(ids(rangiere(vor(ANLAGE), co2).passt)).toEqual(['sensor.co2_zelt'])
  })

  it('Zeltfühler · Temperatur: Zelt vor Keller/Außen, Nährlösung zuletzt unter den Temperaturen', () => {
    const { passt } = rangiere(vor(ANLAGE), { domains: ['sensor'], einheit: '°C', label: 'Zeltfühler · Temperatur' })
    expect(passt[0].entityId).toBe('sensor.zelt_temperatur')
    expect(ids(passt)).toContain('sensor.nahrlosung_temperatur')
  })

  it('Außenfühler · Feuchte: Außen vor Zelt und Keller', () => {
    const { passt } = rangiere(vor(ANLAGE), { domains: ['sensor'], einheit: '%', label: 'Außenfühler · Feuchte' })
    expect(passt[0].entityId).toBe('sensor.aussen_luftfeuchtigkeit')
  })

  it('VPD: Einheit kPa genügt ohne Geräteklasse; Luftdruck in hPa passt nicht', () => {
    const { passt } = rangiere(vor(ANLAGE), { domains: ['sensor'], einheit: 'kPa', label: 'Zeltfühler · VPD' })
    expect(ids(passt)).toEqual(['sensor.zelt_vpd'])
  })

  it('Nutzer ohne Geräteklassen: die Einheit trägt, der Name ordnet', () => {
    const ohneKlassen = ANLAGE.map((x) => ({ ...x, deviceClass: null }))
    const { passt } = rangiere(vor(ohneKlassen), feuchteZelt)
    expect(passt[0].entityId).toBe('sensor.zelt_luftfeuchtigkeit')
    // Ohne Klasse ist die Batterie von der Luftfeuchte nicht mehr zu trennen — nur noch der Name ordnet sie nach hinten.
    expect(ids(passt).indexOf('sensor.handy_batterie')).toBe(ids(passt).length - 1)
  })

  it('Nutzer ohne Geräteklasse und ohne Einheit: nur der Name — Treffer unter "passt", der Rest darunter', () => {
    const nurName = [
      e('sensor.tent_humidity', 'Tent Humidity'),
      e('sensor.ph_wert', 'pH Wert'),
      e('sensor.irgendwas', 'Irgendwas'),
    ]
    const { passt, andere } = rangiere(vor(nurName), feuchteZelt)
    expect(ids(passt)).toEqual(['sensor.tent_humidity'])
    expect(ids(andere)).toEqual(['sensor.irgendwas', 'sensor.ph_wert'])
  })

  it('Einheit vor "nur Name": ein Treffer mit Einheit steht über einem ohne', () => {
    const liste = [
      e('sensor.zelt_feuchte_vorlage', 'Zelt Feuchte Vorlage'),
      e('sensor.regal_feuchte', 'Regal Feuchte', '%'),
    ]
    expect(ids(rangiere(vor(liste), feuchteZelt).passt)).toEqual(['sensor.regal_feuchte', 'sensor.zelt_feuchte_vorlage'])
  })

  it('Rang der Passung: Einheit+Name, dann Klasse allein, dann nur Name, zuletzt nur Einheit', () => {
    const liste = [
      e('sensor.handy_signal', 'Handy Signal', '%'),
      e('sensor.zelt_feuchte_vorlage', 'Zelt Feuchte Vorlage'),
      e('sensor.sensor_3', 'Sensor 3', '%', 'humidity'),
      e('sensor.zelt_feuchte_a', 'Zelt Feuchte A', '%', 'humidity'),
    ]
    expect(ids(rangiere(vor(liste), feuchteZelt).passt)).toEqual([
      'sensor.zelt_feuchte_a', 'sensor.sensor_3', 'sensor.zelt_feuchte_vorlage', 'sensor.handy_signal'])
  })

  it('Leistung: kW wird akzeptiert, kWh nicht', () => {
    const liste = [e('sensor.kuehler_kw', 'Kühler', 'kW', 'power'), e('sensor.kuehler_energie', 'Kühler Energie', 'kWh', 'energy')]
    const { passt } = rangiere(vor(liste), { domains: ['sensor'], einheit: 'W', label: 'Kühler · Leistung' })
    expect(ids(passt)).toEqual(['sensor.kuehler_kw'])
  })
})

describe('Schalter und Zustände (nur Domain und Name)', () => {
  it('Entfeuchter · schalten: die Steckdose des Entfeuchters oben, Kaffeemaschine und Sensoren nur unter "anderen"', () => {
    const { passt, andere } = rangiere(vor(ANLAGE), entfeuchterSchalten)
    expect(ids(passt)).toEqual(['switch.entfeuchter_steckdose'])
    // Gleiche Domain zuerst, dann der Rest.
    expect(ids(andere).slice(0, 3).sort()).toEqual(['switch.co2_ventil', 'switch.kaffeemaschine', 'switch.zuluft_lufter'])
    expect(ids(andere)).toContain('sensor.zelt_temperatur')
  })

  it('englische Namen: Dehumidifier Plug', () => {
    const liste = [e('switch.dehumidifier_plug', 'Dehumidifier Plug'), e('switch.tv', 'TV')]
    expect(ids(rangiere(vor(liste), entfeuchterSchalten).passt)).toEqual(['switch.dehumidifier_plug'])
  })

  it('Funktionswörter zählen halb: "Kühler-Steckdose" steht unter dem Entfeuchter, bleibt aber unter "passt"', () => {
    const liste = [e('switch.kuehler_steckdose', 'Kühler-Steckdose'), e('select.entfeuchter_modus', 'Entfeuchter Modus')]
    expect(ids(rangiere(vor(liste), entfeuchterSchalten).passt)).toEqual(['select.entfeuchter_modus', 'switch.kuehler_steckdose'])
  })

  it('Lampe · Zustand: die Lampe steht oben, auch als light.', () => {
    expect(ids(rangiere(vor(ANLAGE), lampeZustand).passt)).toEqual(['light.growlampe'])
  })

  it('CO₂-Ventil · schalten: das Ventil, nicht der Entfeuchter', () => {
    const { passt } = rangiere(vor(ANLAGE), { domains: ['select', 'switch', 'input_boolean'], label: 'CO₂-Ventil · schalten' })
    expect(passt[0].entityId).toBe('switch.co2_ventil')
  })
})

describe('Wortgrenzen', () => {
  it('"template" ist keine Temperatur, "content" kein Zelt, "boden" nicht "oben"', () => {
    // Ohne Einheit und Klasse zählt nur der Name: weder "Template" (temp) noch "Content" (tent) darf treffen.
    const liste = [e('sensor.template_x', 'Template X'), e('sensor.content', 'Content')]
    const k: Kriterium = { domains: ['sensor'], einheit: '°C', label: 'Zeltfühler · Temperatur' }
    expect(rangiere(vor(liste), k).passt).toEqual([])
    expect(begriffeAusRollenname('Fühler Boden')).not.toContain('oben')
  })

  it('Teilwörter ohne Bedeutung treffen nicht: Flightmode/Highlight (Licht), Dehumidifier (Feuchte), Extractor (Zusatz), Pumpkin, Onlineshop', () => {
    const fremd = ['Flightmode', 'Highlight', 'Dehumidifier', 'Extractor', 'Overpowered', 'Zeitung', 'Speedtest', 'Pumpkin', 'Onlineshop']
    const liste = fremd.map((n) => e(`switch.${n.toLowerCase()}`, n))
    // Eine Rolle mit allen betroffenen Begriffen in einem Namen: nichts davon darf als "passt" erscheinen.
    const k: Kriterium = {
      domains: ['switch'],
      label: 'Lampe · Feuchte · Zusatz · Leistung · Stufe · Pumpe · Status · Zeit',
    }
    expect(rangiere(vor(liste), k).passt).toEqual([])
    // Gegenprobe: dieselben Begriffe als ganze Wörter treffen.
    const echt = [e('switch.lamp', 'Lamp'), e('sensor.tent_humidity', 'Tent Humidity'), e('switch.pump', 'Pump'), e('switch.online', 'Online')]
    expect(ids(rangiere(vor(echt), { ...k, domains: ['switch', 'sensor'] }).passt).sort())
      .toEqual(['sensor.tent_humidity', 'switch.lamp', 'switch.online', 'switch.pump'])
  })
})

describe('Nichts geht verloren', () => {
  const kriterien: Kriterium[] = [feuchteZelt, co2, entfeuchterSchalten, lampeZustand,
    { domains: [], label: 'Unbekannt' }, { domains: ['sensor'], einheit: 'kPa', label: 'VPD' }]

  it.each(kriterien.map((k) => [k.label, k] as const))('%s: passt + andere = alle, ohne Doppelte', (_name, k) => {
    const { passt, andere } = rangiere(vor(ANLAGE), k)
    expect([...ids(passt), ...ids(andere)].sort()).toEqual(ids(ANLAGE).sort())
  })

  it('Leere Eingabe ergibt zwei leere Listen', () => {
    expect(rangiere([], feuchteZelt)).toEqual({ passt: [], andere: [] })
  })
})

describe('Auswahl-Einträge', () => {
  it('zwei Gruppen in dieser Reihenfolge, Wert und Hinweis wie bisher', () => {
    const { optionen, leerHinweis } = entitaetsOptionen(vor(ANLAGE), feuchteZelt)
    expect(leerHinweis).toBeNull()
    expect(optionen[0]).toMatchObject({ wert: 'sensor.zelt_luftfeuchtigkeit', text: 'Zelt Luftfeuchtigkeit', gruppe: GRUPPE_PASST })
    expect(optionen[0].hinweis).toContain('sensor.zelt_luftfeuchtigkeit · ')
    const gruppen = optionen.map((o) => o.gruppe)
    expect(gruppen.indexOf(GRUPPE_ANDERE)).toBe(gruppen.lastIndexOf(GRUPPE_PASST) + 1)
    expect(optionen).toHaveLength(ANLAGE.length)
  })

  it('findet nichts Passendes: Hinweis, alles steht unter "Alle anderen"', () => {
    const { optionen, leerHinweis } = entitaetsOptionen(vor([e('sensor.irgendwas', 'Irgendwas')]), feuchteZelt)
    expect(leerHinweis).toBe(HINWEIS_NICHTS_PASSENDES)
    expect(optionen.map((o) => o.gruppe)).toEqual([GRUPPE_ANDERE])
  })

  it('ohne Entitäten (Home Assistant stumm): kein Hinweis, keine Einträge', () => {
    expect(entitaetsOptionen([], feuchteZelt)).toEqual({ optionen: [], leerHinweis: null })
  })
})
