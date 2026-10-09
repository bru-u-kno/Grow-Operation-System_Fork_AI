/**
 * Fork AI (forkai.195): Die Einträge unter einem Gerät, geordnet statt alphabetisch.
 *
 * Gruppiert wird nach der ART der Entität — das steht im Präfix der Entity-ID und
 * ist damit belegt, nicht geraten. Was keiner Art zugeordnet ist, steht unter
 * „Sonstige"; die Zuordnung rät nicht weiter.
 */
export type OrdnungsVerwendung = { zweck: string; quelle: string }
export type OrdnungsEntitaet = { entityId: string; verwendungen: OrdnungsVerwendung[] }

export type EintragGruppe<E extends OrdnungsEntitaet> = {
  art: string
  label: string
  eintraege: Array<{ entitaet: E; titel: string | null }>
}

/** Reihenfolge der Gruppen und welche Entity-Domänen dazugehören. */
const ARTEN: Array<{ art: string; label: string; domaenen: string[] }> = [
  { art: 'messwerte', label: 'Messwerte', domaenen: ['sensor', 'binary_sensor'] },
  { art: 'einstellungen', label: 'Einstellungen', domaenen: ['number', 'select', 'time', 'text', 'input_number', 'input_select', 'input_boolean', 'input_datetime'] },
  { art: 'schalter', label: 'Schalter', domaenen: ['switch', 'light', 'fan'] },
  { art: 'skripte', label: 'Skripte', domaenen: ['script', 'button', 'automation', 'scene'] },
  { art: 'kameras', label: 'Kameras', domaenen: ['camera'] },
]
const SONSTIGE = { art: 'sonstige', label: 'Sonstige' }

export function domaeneVon(entityId: string): string {
  const punkt = entityId.indexOf('.')
  return punkt < 0 ? '' : entityId.slice(0, punkt).toLowerCase()
}

/** „Steuerung BLUELAB · EC · oben" → „EC · oben"; „Messgröße ReservoirEc" → „ReservoirEc". */
export function kurzerZweck(zweck: string): string {
  return zweck.replace(/^Steuerung [A-Z0-9_]+ · /, '').replace(/^Messgröße /, '').trim()
}

/** Die Bezeichnung eines Eintrags: sein erster Zweck, kurz. Ohne Zweck: null (dann gilt die ID). */
export function titelVon(entitaet: OrdnungsEntitaet): string | null {
  const erster = entitaet.verwendungen[0]
  const kurz = erster ? kurzerZweck(erster.zweck) : ''
  return kurz === '' ? null : kurz
}

const vergleich = (a: string, b: string) => a.localeCompare(b, 'de', { numeric: true, sensitivity: 'base' })

/**
 * Gruppen in fester Reihenfolge; leere Gruppen fallen weg. Innerhalb einer Gruppe
 * stehen erst die Einträge mit Zweck (nach Zweck), dann die ohne (nach ID).
 */
export function ordneEintraege<E extends OrdnungsEntitaet>(entitaeten: readonly E[]): Array<EintragGruppe<E>> {
  const gruppen = new Map<string, EintragGruppe<E>>()
  for (const art of [...ARTEN, SONSTIGE]) gruppen.set(art.art, { art: art.art, label: art.label, eintraege: [] })

  for (const entitaet of entitaeten) {
    const domaene = domaeneVon(entitaet.entityId)
    const art = ARTEN.find((kandidat) => kandidat.domaenen.includes(domaene))?.art ?? SONSTIGE.art
    gruppen.get(art)!.eintraege.push({ entitaet, titel: titelVon(entitaet) })
  }

  for (const gruppe of gruppen.values()) {
    gruppe.eintraege.sort((a, b) => {
      if (a.titel !== null && b.titel !== null) return vergleich(a.titel, b.titel) || vergleich(a.entitaet.entityId, b.entitaet.entityId)
      if (a.titel !== null) return -1
      if (b.titel !== null) return 1
      return vergleich(a.entitaet.entityId, b.entitaet.entityId)
    })
  }

  return [...gruppen.values()].filter((gruppe) => gruppe.eintraege.length > 0)
}
