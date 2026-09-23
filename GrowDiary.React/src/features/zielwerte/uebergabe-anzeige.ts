/**
 * Fork AI (forkai.142, F-038): Wie die Zeilen der Übergabe aus dem Plan
 * angezeigt werden — Gruppe, kurzer Name, Zusatz, Einheit.
 *
 * Rein und ohne React, damit es getestet werden kann. Die Rollen-Schlüssel
 * kommen aus `WochenplanSyncService` im Backend; eine unbekannte Rolle wird
 * trotzdem angezeigt (Name wie geliefert, Gruppe nach Ziel).
 */
export type UebergabeZiel = 'ha' | 'grenzwerte'

export type UebergabeRoh = { rolle: string; name: string; entityId?: string | null; wert: string; zustand: string }

export type UebergabeZeile = {
  rolle: string
  ziel: UebergabeZiel
  name: string
  zusatz: string | null
  wert: string
  einheit: string | null
  zustand: string
}

const META: Record<string, { ziel: UebergabeZiel; name: string; zusatz?: string; einheit: string }> = {
  'wasser-tag': { ziel: 'ha', name: 'Chiller Tag', einheit: '°C' },
  'wasser-nacht': { ziel: 'ha', name: 'Chiller Nacht', einheit: '°C' },
  'rh-obergrenze': { ziel: 'ha', name: 'RH-Obergrenze', einheit: '%' },
  'vpd-unten': { ziel: 'ha', name: 'Entfeuchter ein', zusatz: 'wenn VPD darunter', einheit: 'kPa' },
  'vpd-oben': { ziel: 'ha', name: 'Entfeuchter aus', zusatz: 'wenn VPD erreicht', einheit: 'kPa' },
  'co2-ziel': { ziel: 'ha', name: 'CO₂-Ziel', einheit: 'ppm' },
  'blatt-offset': { ziel: 'ha', name: 'Blatt-Offset', einheit: 'K' },
  'luft-unten': { ziel: 'grenzwerte', name: 'Luft unten', zusatz: 'Tag', einheit: '°C' },
  'luft-oben': { ziel: 'grenzwerte', name: 'Luft oben', zusatz: 'Tag', einheit: '°C' },
  'luft-nacht-unten': { ziel: 'grenzwerte', name: 'Luft unten', zusatz: 'Nacht', einheit: '°C' },
  'luft-nacht-oben': { ziel: 'grenzwerte', name: 'Luft oben', zusatz: 'Nacht', einheit: '°C' },
  'feuchte-oben': { ziel: 'grenzwerte', name: 'Luftfeuchte oben', zusatz: 'Tag', einheit: '%' },
  'feuchte-nacht-oben': { ziel: 'grenzwerte', name: 'Luftfeuchte oben', zusatz: 'Nacht', einheit: '%' },
}

/** Ein echtes Minuszeichen statt Bindestrich — sonst steht „-1" optisch schief. */
export function wertText(wert: string): string {
  return wert.trim().replace(/^-/, '−')
}

export function zeileAus(roh: UebergabeRoh): UebergabeZeile {
  const meta = META[roh.rolle]
  const ziel: UebergabeZiel = meta?.ziel ?? ((roh.entityId ?? '').startsWith('zelt:') ? 'grenzwerte' : 'ha')
  return {
    rolle: roh.rolle,
    ziel,
    name: meta?.name ?? roh.name,
    zusatz: meta?.zusatz ?? null,
    wert: wertText(roh.wert),
    einheit: meta?.einheit ?? null,
    zustand: roh.zustand,
  }
}

export function gruppieren(roh: readonly UebergabeRoh[]): Record<UebergabeZiel, UebergabeZeile[]> {
  const zeilen = roh.map(zeileAus)
  return { ha: zeilen.filter((z) => z.ziel === 'ha'), grenzwerte: zeilen.filter((z) => z.ziel === 'grenzwerte') }
}

export type Co2Stufe = { bereich: string; ppm: number; aktiv: boolean }

/** Die drei CO₂-Stufen; aktiv ist die, deren Wert gerade als Ziel gilt. */
export function co2Stufen(kuehl: number, mittel: number, warm: number, zielJetzt: number | null): Co2Stufe[] {
  return [
    { bereich: 'unter 25 °C', ppm: kuehl, aktiv: zielJetzt === kuehl },
    { bereich: '25–27 °C', ppm: mittel, aktiv: zielJetzt === mittel && zielJetzt !== kuehl },
    { bereich: 'über 27 °C', ppm: warm, aktiv: zielJetzt === warm && zielJetzt !== mittel && zielJetzt !== kuehl },
  ]
}

export type BluelabGrenze = { rolle: string; soll: number | null; geraet: number | null }
export type BluelabPaar = { name: string; unten: number | null; oben: number | null; einheit: string | null; stimmt: boolean }

const BLUELAB: Array<{ name: string; unten: string; oben: string; einheit: string | null }> = [
  { name: 'pH unten · oben', unten: 'ph_low', oben: 'ph_high', einheit: null },
  { name: 'EC unten · oben', unten: 'ec_low', oben: 'ec_high', einheit: 'mS/cm' },
  { name: 'Wasser unten · oben', unten: 'temp_low', oben: 'temp_high', einheit: '°C' },
]

/** Je Messgröße ein Paar; „stimmt", wenn das Gerät beide Sollwerte trägt. */
export function bluelabPaare(grenzen: readonly BluelabGrenze[]): BluelabPaar[] {
  const finde = (rolle: string) => grenzen.find((g) => g.rolle === rolle)
  const passt = (g?: BluelabGrenze) => !g || g.soll == null || (g.geraet != null && Math.abs(g.soll - g.geraet) < 0.05)
  return BLUELAB
    .map((b) => {
      const u = finde(b.unten)
      const o = finde(b.oben)
      return { name: b.name, unten: u?.soll ?? null, oben: o?.soll ?? null, einheit: b.einheit, stimmt: passt(u) && passt(o) }
    })
    .filter((p) => p.unten != null || p.oben != null)
}

export function zahl(wert: number | null): string {
  return wert == null ? '–' : wert.toLocaleString('de-DE', { maximumFractionDigits: 1 })
}
