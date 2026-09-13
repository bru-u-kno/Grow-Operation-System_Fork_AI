import { useRef, useState, type PointerEvent as ReactPointerEvent } from 'react'
import type { MetricPayload } from '../../types'
import type { HistoryPoint } from '../../components/SensorChart'
import { SensorChart } from '../../components/SensorChart'
import { MetricTile } from './MetricTile'
import { HistoryChart } from './HistoryChart'
import { decimalsForMetric } from './metric-tile-model'
import { metricProvenance } from './live-model'
import { SectionHead } from './DashboardEditor'
import {
  encodeDropTarget,
  moveSection,
  moveTile,
  parseDropTarget,
  removeSection,
  removeTile,
  renameSection,
  resolveTile,
  type DashboardLayout,
  type EntityValue,
} from './dashboard-layout'
import { classNames } from '../../utils'

/**
 * Die Messwert-Bereiche aus dem Layout des Zelts.
 *
 * Im Anpassen-Modus lassen sich Kacheln ziehen — innerhalb eines Bereichs und
 * zwischen zweien. Das Rechnen dahinter liegt in `dashboard-layout.ts` und ist
 * dort geprüft; hier steht nur, was der Zeigefinger auslöst.
 *
 * Gezogen wird über Pointer-Events, nicht über HTML5-Drag-and-Drop. Letzteres
 * kennt kein Touch: am Handy liess sich bisher nur hinzufügen, entfernen und
 * umbenennen, aber nichts umsortieren — und gerade am Handy will man die
 * wichtigste Kachel nach oben holen. Pointer-Events decken Maus und Finger mit
 * demselben Code ab.
 *
 * Gezogen wird nur am Griff, nicht an der ganzen Kachel. Sonst bliebe am Handy
 * jeder Wischer zum Scrollen an einer Kachel hängen.
 */
export function DashboardBands({
  layout, metricsByKey, entityValues, trends, editing, onChange,
}: {
  layout: DashboardLayout
  metricsByKey: Map<string, MetricPayload>
  entityValues: Map<string, EntityValue>
  trends: Map<string, HistoryPoint[]>
  editing: boolean
  onChange: (layout: DashboardLayout) => void
}) {
  // Der Zustand liegt zusaetzlich in einem Ref, und die Handler lesen NUR den.
  // Zwischen Greifen und erster Bewegung liegt nicht zwangslaeufig ein Rendern:
  // ein schneller Zug — oder ein Testlauf, der beide Ereignisse hintereinander
  // schickt — saehe im State noch null und wuerde die erste Bewegung verwerfen.
  const griff = useRef<{ sectionId: string; index: number } | null>(null)
  // Welche Kachel gerade ihre Historie zeigt — Tester-Wunsch: „auf eine
  // Kachel klicken und mehr Daten einsehen". Toggle, nichts Modales.
  const [offeneMetrik, setOffeneMetrik] = useState<string | null>(null)

  const [dragged, setDragged] = useState<{ sectionId: string; index: number } | null>(null)
  const [over, setOver] = useState<string | null>(null)

  function greifen(event: ReactPointerEvent<HTMLElement>, sectionId: string, index: number) {
    if (!editing) return
    // Verhindert, dass die Geste stattdessen die Seite scrollt oder Text markiert.
    event.preventDefault()
    event.currentTarget.setPointerCapture(event.pointerId)
    griff.current = { sectionId, index }
    setDragged(griff.current)
  }

  function ziehen(event: ReactPointerEvent<HTMLElement>) {
    if (!griff.current) return
    // Unter dem Finger liegt die Kachel, nicht der Griff — also wird das
    // Element an der Position gesucht, nicht das Ereignisziel genommen.
    const unten = document.elementFromPoint(event.clientX, event.clientY)
    setOver(unten?.closest('[data-drop-target]')?.getAttribute('data-drop-target') ?? null)
  }

  function loslassen(event: ReactPointerEvent<HTMLElement>) {
    const von = griff.current
    griff.current = null
    if (!von) { setOver(null); return }

    const unten = document.elementFromPoint(event.clientX, event.clientY)
    const ziel = parseDropTarget(unten?.closest('[data-drop-target]')?.getAttribute('data-drop-target'))
    if (ziel) onChange(moveTile(layout, von, ziel))

    setDragged(null)
    setOver(null)
  }

  return (
    <>
      {layout.sections.map((section, sectionIndex) => (
        <div key={section.id} className="ls-band" data-audit={`live-section-${section.id}`}>
          <SectionHead
            title={section.title}
            editing={editing}
            canUp={sectionIndex > 0}
            canDown={sectionIndex < layout.sections.length - 1}
            onRename={(value) => onChange(renameSection(layout, section.id, value))}
            onRemove={() => onChange(removeSection(layout, section.id))}
            onUp={() => onChange(moveSection(layout, sectionIndex, sectionIndex - 1))}
            onDown={() => onChange(moveSection(layout, sectionIndex, sectionIndex + 1))}
          />

          <div className={classNames('gos-metric-row', editing && 'is-editing')}>
            {section.tiles.map((tile, index) => {
              const metric = resolveTile(tile, metricsByKey, entityValues)
              const trend = tile.kind === 'Metric' && tile.metricKey ? trends.get(tile.metricKey) : undefined
              return (
                <div
                  key={tile.id}
                  className={classNames(
                    'ls-tile-slot',
                    editing && 'is-draggable',
                    dragged?.sectionId === section.id && dragged.index === index && 'is-dragging',
                    over === encodeDropTarget(section.id, index) && 'is-over')}
                  // Ein Verlauf teilt sich die Zeile mit nichts: neben drei Kacheln
                  // waere er ein Streifen, in dem man keine Kurve mehr erkennt.
                  style={tile.kind === 'Chart'
                    ? { flex: '1 1 100%' }
                    : { flex: `${Math.min(Math.max(tile.span, 1), 3)} 1 150px` }}
                  data-drop-target={encodeDropTarget(section.id, index)}
                >
                  {tile.kind === 'Chart' ? (
                    <div className="ls-chart-tile">
                      <div className="ls-chart-head">{tile.label ?? 'Verlauf · 24 h'}</div>
                      <HistoryChart
                        lines={(tile.metricKeys ?? []).map((key) => ({
                          key,
                          label: metricsByKey.get(key)?.label ?? key,
                          unit: metricsByKey.get(key)?.unit ?? null,
                          points: trends.get(key) ?? [],
                        }))}
                      />
                    </div>
                  ) : (
                  <MetricTile
                    label={metric.label}
                    value={metric.numericValue}
                    unit={metric.unit}
                    targetMin={metric.targetMin}
                    targetMax={metric.targetMax}
                    decimals={decimalsForMetric(metric.key)}
                    display={metric.numericValue == null && metric.value !== '–' ? metric.value : undefined}
                    footer={metric.targetMin == null && metric.targetMax == null ? (metric.hint ?? undefined) : undefined}
                    trend={trend}
                    targetNote={metric.targetNote}
                    dayMin={metric.targetDayMin}
                    dayMax={metric.targetDayMax}
                    nightMin={metric.targetNightMin}
                    nightMax={metric.targetNightMax}
                    targetPhase={metric.targetPhase}
                    sourceNote={metricProvenance(metric).sourceNote}
                    stale={metricProvenance(metric).stale}
                    onOpen={!editing && tile.kind === 'Metric' && tile.metricKey && (trends.get(tile.metricKey)?.length ?? 0) > 1
                      ? () => setOffeneMetrik(offeneMetrik === tile.metricKey ? null : tile.metricKey)
                      : undefined}
                    open={offeneMetrik === tile.metricKey}
                  />
                  )}
                  {editing && (
                    <span className="ls-tile-tools">
                      <button
                        type="button"
                        className="dots"
                        aria-label={`${metric.label} verschieben`}
                        onPointerDown={(event) => greifen(event, section.id, index)}
                        onPointerMove={ziehen}
                        onPointerUp={loslassen}
                        onPointerCancel={() => { griff.current = null; setDragged(null); setOver(null) }}
                      >
                        ⠿
                      </button>
                      <button
                        type="button"
                        className="x"
                        aria-label={`${metric.label} entfernen`}
                        onClick={() => onChange(removeTile(layout, tile.id))}
                      >
                        ×
                      </button>
                    </span>
                  )}
                </div>
              )
            })}

            {editing && (
              // Das Ziel zum Fallenlassen. Ohne es weiss niemand, wohin eine
              // gezogene Kachel ueberhaupt darf — und ein leerer Bereich waere
              // sonst gar nicht zu befuellen.
              <div
                className={classNames('ls-dropzone',
                  over === encodeDropTarget(section.id, section.tiles.length) && 'is-over')}
                data-drop-target={encodeDropTarget(section.id, section.tiles.length)}
              >
                hierher ziehen
              </div>
            )}
          </div>

          {/* Die aufgeklappte Historie einer Kachel — unter ihrer Zeile, nicht
              modal: man will die Nachbarn zum Vergleich weiter sehen. */}
          {offeneMetrik && section.tiles.some((tile) => tile.metricKey === offeneMetrik) && (() => {
            const metric = metricsByKey.get(offeneMetrik)
            const punkte = trends.get(offeneMetrik) ?? []
            if (!metric || punkte.length < 2) return null
            return (
              <div className="ls-metric-detail" data-audit="metric-detail">
                <SensorChart
                  series={{ metricKey: offeneMetrik, label: metric.label, unit: metric.unit, points: punkte }}
                  target={{ min: metric.targetMin, max: metric.targetMax }}
                />
              </div>
            )
          })()}
        </div>
      ))}
    </>
  )
}
