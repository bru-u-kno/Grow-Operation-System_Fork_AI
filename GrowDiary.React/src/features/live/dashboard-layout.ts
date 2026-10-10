import type { MetricPayload } from '../../types'
import { maschinenZahl } from '../../zahlenfeld'

/**
 * Das Layout eines Zelt-Dashboards und die Rechnungen darauf.
 *
 * Die Umsortier-Logik liegt hier und nicht in der Komponente: „Kachel von
 * Bereich A an Position 3 in Bereich B" ist die Stelle, an der ein
 * Drag-and-Drop still danebengreift, und im DOM ist das nicht zu prüfen.
 */

export type DashboardTileKind = 'Metric' | 'Entity' | 'Camera' | 'Chart'

export type DashboardTile = {
  id: string
  kind: DashboardTileKind
  metricKey: string | null
  entityId: string | null
  /** Nur fuer 'Chart': welche Messwerte zusammen gezeichnet werden. */
  metricKeys?: string[] | null
  label: string | null
  unit: string | null
  /** Wie viel Platz die Kachel bekommt, 1–3. Bestandslayouts tragen das schon. */
  span: number
}

export type DashboardSection = { id: string; title: string; tiles: DashboardTile[] }

export type DashboardLayout = {
  tentId: number
  sections: DashboardSection[]
  /** false = das ausgelieferte Standard-Layout, nicht die Anordnung des Nutzers. */
  isCustom: boolean
}

/** Die Werte, die Grow OS selbst kennt — im Dialog beim Namen genannt, nicht als Schlüssel. */
export const KNOWN_METRICS: ReadonlyArray<{ key: string; label: string }> = [
  { key: 'temperature', label: 'Temperatur' },
  { key: 'humidity', label: 'Luftfeuchte' },
  { key: 'vpd', label: 'VPD' },
  { key: 'co2', label: 'CO₂' },
  { key: 'ppfd', label: 'PPFD' },
  { key: 'light-cycle', label: 'Licht' },
  // Die Luft um das Zelt (z. B. der Sensor am Controller) — nicht die von draußen.
  // Dieselben Namen wie im Verlauf und in den Meldungen (AlertEvaluationService.MetricDisplay).
  { key: 'outside-temperature', label: 'Außen Temp.' },
  { key: 'outside-humidity', label: 'Außen RLF' },
  { key: 'outside-vpd', label: 'Außen VPD' },
  { key: 'reservoir-ph', label: 'pH' },
  { key: 'reservoir-ec', label: 'EC' },
  { key: 'reservoir-temp', label: 'Wassertemperatur' },
  { key: 'reservoir-level', label: 'Wasserstand (L)' },
  { key: 'reservoir-level-cm', label: 'Wasserstand (cm)' },
  { key: 'orp', label: 'ORP' },
  { key: 'dissolved-oxygen', label: 'Sauerstoff' },
  { key: 'power', label: 'Strom' },
]

/**
 * Eine Verlaufs-Kachel für die Messwerte eines Bereichs.
 *
 * Sie nimmt die Messwerte mit, die im Bereich ohnehin stehen — wer „Klima"
 * einen Verlauf gibt, will Temperatur, Feuchte, VPD und CO₂ zusammen sehen und
 * nicht noch einmal auswählen. Nachträglich lässt sich die Kachel entfernen
 * wie jede andere.
 */
export function chartTile(metricKeys: string[], label = 'Verlauf'): DashboardTile {
  return {
    id: newId(),
    kind: 'Chart',
    metricKey: null,
    entityId: null,
    metricKeys,
    label,
    unit: null,
    // Ein Verlauf über eine Drittel-Breite waere unlesbar.
    span: 3,
  }
}

export function newId(): string {
  return Math.random().toString(36).slice(2, 10)
}

function withSections(layout: DashboardLayout, sections: DashboardSection[]): DashboardLayout {
  return { ...layout, sections }
}

/**
 * Verschiebt eine Kachel — innerhalb eines Bereichs oder in einen anderen.
 *
 * Der Fall, der zweimal danebenging: innerhalb DESSELBEN Bereichs nach hinten
 * ziehen. Erst entfernen und dann an der Zielposition einsetzen verschiebt die
 * Zielposition um eins nach vorn, die Kachel landet einen Platz zu früh.
 * Deshalb hier eine einzige Liste umsortieren statt entfernen + einsetzen.
 */
export function moveTile(
  layout: DashboardLayout,
  from: { sectionId: string; index: number },
  to: { sectionId: string; index: number },
): DashboardLayout {
  const source = layout.sections.find((section) => section.id === from.sectionId)
  const tile = source?.tiles[from.index]
  if (!source || !tile) return layout

  if (from.sectionId === to.sectionId) {
    if (from.index === to.index) return layout
    const tiles = [...source.tiles]
    tiles.splice(from.index, 1)
    // Nach dem Entfernen sind alle Positionen hinter der Quelle um eins gerückt.
    tiles.splice(from.index < to.index ? to.index - 1 : to.index, 0, tile)
    return withSections(layout, layout.sections.map((section) =>
      section.id === source.id ? { ...section, tiles } : section))
  }

  return withSections(layout, layout.sections.map((section) => {
    if (section.id === from.sectionId) {
      return { ...section, tiles: section.tiles.filter((_, index) => index !== from.index) }
    }
    if (section.id === to.sectionId) {
      const tiles = [...section.tiles]
      tiles.splice(Math.min(to.index, tiles.length), 0, tile)
      return { ...section, tiles }
    }
    return section
  }))
}

export function addTile(layout: DashboardLayout, sectionId: string, tile: DashboardTile): DashboardLayout {
  const target = layout.sections.some((section) => section.id === sectionId) ? sectionId : layout.sections[0]?.id
  if (!target) return layout
  return withSections(layout, layout.sections.map((section) =>
    section.id === target ? { ...section, tiles: [...section.tiles, tile] } : section))
}

export function removeTile(layout: DashboardLayout, tileId: string): DashboardLayout {
  return withSections(layout, layout.sections.map((section) =>
    ({ ...section, tiles: section.tiles.filter((tile) => tile.id !== tileId) })))
}

export function renameSection(layout: DashboardLayout, sectionId: string, title: string): DashboardLayout {
  return withSections(layout, layout.sections.map((section) =>
    section.id === sectionId ? { ...section, title } : section))
}

export function addSection(layout: DashboardLayout, title = 'Neuer Bereich'): DashboardLayout {
  return withSections(layout, [...layout.sections, { id: newId(), title, tiles: [] }])
}

export function removeSection(layout: DashboardLayout, sectionId: string): DashboardLayout {
  return withSections(layout, layout.sections.filter((section) => section.id !== sectionId))
}

export function moveSection(layout: DashboardLayout, from: number, to: number): DashboardLayout {
  if (from === to || to < 0 || to >= layout.sections.length) return layout
  const sections = [...layout.sections]
  const [section] = sections.splice(from, 1)
  sections.splice(to, 0, section)
  return withSections(layout, sections)
}

/**
 * Ein leeres Layout ist kein Layout.
 *
 * Der Server wirft ein leeres wieder weg und liefert den Standard zurück — wer
 * die letzte Kachel entfernt und speichert, bekäme also wortlos das Standard-
 * Dashboard zurück. Besser, das Speichern sagt vorher, dass so nichts geht.
 */
export function layoutIsEmpty(layout: DashboardLayout): boolean {
  return layout.sections.every((section) => section.tiles.length === 0)
}

/** Eine Kachel für einen Wert, den Grow OS selbst misst. */
/**
 * Eine Kachel für einen Messwert, den Grow OS kennt.
 *
 * Das Label wird mitgegeben, obwohl `resolveTile` es normalerweise aus den
 * aktuellen Messwerten holt. Grund: findet es den Schlüssel dort nicht, fiel
 * die Kachel auf den Schlüssel selbst zurück und schrieb „co2" statt „CO₂" in
 * die Oberfläche. Der Auswahldialog kennt den richtigen Namen — er soll ihn
 * auch weitergeben, statt ihn wegzuwerfen.
 */
export function metricTile(metricKey: string, label: string | null = null): DashboardTile {
  return { id: newId(), kind: 'Metric', metricKey, entityId: null, label, unit: null, span: 1 }
}

/** Eine Kachel für eine beliebige Home-Assistant-Entität. */
export function entityTile(entityId: string, label: string | null, unit: string | null): DashboardTile {
  return { id: newId(), kind: 'Entity', metricKey: null, entityId, label, unit, span: 1 }
}

/**
 * Das Start-Layout beim ersten Klick auf „Anpassen": genau das, was gerade auf
 * dem Bildschirm steht.
 *
 * Nicht der Standard des Servers. Der kennt die Feinheiten dieser Seite nicht —
 * dass Füllstand entweder in Litern ODER in Zentimetern gezeigt wird, und dass
 * Licht hinter dem Klima steht. Säte man daraus, erschienen beim Umschalten in
 * den Anpassen-Modus plötzlich Kacheln, die vorher nicht da waren.
 */
export function seedLayout(
  tentId: number,
  bands: ReadonlyArray<{ title: string; metrics: ReadonlyArray<{ key: string }> }>,
): DashboardLayout {
  return {
    tentId,
    isCustom: false,
    sections: bands.map((band, index) => ({
      id: `seed-${index}`,
      title: band.title,
      tiles: band.metrics.map((metric) => ({
        id: `${metric.key}-${index}`,
        kind: 'Metric' as const,
        metricKey: metric.key,
        entityId: null,
        label: null,
        unit: null,
        span: 1,
      })),
    })),
  }
}

export type EntityValue = { entityId: string; friendlyName: string | null; state: string | null; unit: string | null }

/** Aus „switch.eheim_uv" wird „Eheim uv" — besser als der rohe Schlüssel. */
export function entityLabel(tile: DashboardTile, value: EntityValue | undefined): string {
  if (tile.label) return tile.label
  if (value?.friendlyName) return value.friendlyName
  const id = tile.entityId ?? ''
  const short = id.includes('.') ? id.slice(id.indexOf('.') + 1) : id
  const spaced = short.replace(/[_-]+/g, ' ').trim()
  return spaced ? spaced.charAt(0).toUpperCase() + spaced.slice(1) : 'Sensor'
}

/**
 * Macht aus einer Kachel etwas, das die Messwert-Kachel anzeigen kann.
 *
 * Eigene Werte behalten ihren Zielbereich und damit ihre Ampelfarbe. Fremde
 * Entitäten haben keinen — ihr Zustand wird gezeigt, wie er ist. „on"/„off"
 * bleibt „on"/„off"; etwas anderes zu behaupten wäre geraten.
 */
export function resolveTile(
  tile: DashboardTile,
  metricsByKey: Map<string, MetricPayload>,
  entityValues: Map<string, EntityValue>,
): MetricPayload {
  if (tile.kind === 'Metric' && tile.metricKey) {
    const found = metricsByKey.get(tile.metricKey)
    const base: MetricPayload = found ?? {
      key: tile.metricKey,
      label: tile.label ?? tile.metricKey,
      value: '–',
      unit: tile.unit,
      tone: 'muted',
      hint: null,
      numericValue: null,
      targetMin: null,
      targetMax: null,
    }
    return { ...base, label: tile.label ?? base.label, unit: tile.unit ?? base.unit }
  }

  const value = tile.entityId ? entityValues.get(tile.entityId) : undefined
  const raw = value?.state ?? null
  // Ein Zustand aus Home Assistant ist ein Maschinenwert: „1.234" heisst dort
  // eins-komma-zwei-drei-vier, nicht tausendzweihundertvierunddreissig.
  const zahl = maschinenZahl(raw)
  return {
    key: tile.entityId ?? tile.id,
    label: entityLabel(tile, value),
    value: raw == null || raw.trim() === '' ? '–' : raw,
    unit: tile.unit ?? value?.unit ?? null,
    tone: 'muted',
    hint: null,
    numericValue: zahl,
    targetMin: null,
    targetMax: null,
  }
}

/**
 * Wohin eine gezogene Kachel fallen darf, als Zeichenkette im DOM.
 *
 * Gebraucht für das Sortieren mit dem Finger: HTML5-Drag-and-Drop kennt kein
 * Touch, also wird beim Ziehen das Element unter dem Finger gesucht und aus
 * seinem Attribut das Ziel gelesen. Der Index steht vorne und wird an der
 * ersten Trennung abgeschnitten — so darf eine Bereichs-Kennung alles
 * enthalten, auch das Trennzeichen selbst.
 */
const DROP_SEPARATOR = '|'

export function encodeDropTarget(sectionId: string, index: number): string {
  return `${index}${DROP_SEPARATOR}${sectionId}`
}

export function parseDropTarget(raw: string | null | undefined): { sectionId: string; index: number } | null {
  if (!raw) return null
  const cut = raw.indexOf(DROP_SEPARATOR)
  if (cut <= 0) return null

  const index = Number(raw.slice(0, cut))
  const sectionId = raw.slice(cut + 1)
  if (!Number.isInteger(index) || index < 0 || sectionId === '') return null

  return { sectionId, index }
}
