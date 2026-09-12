import { useEffect, useMemo, useState } from 'react'

import { apiFetch, formatApiError } from '../../api'
import { V1Alert, V1Button, V1Card, V1Empty, V1Skeleton, V1Switch } from '../../components/v1'
import { V1Select } from '../../components/V1Select'
import type { V1Option } from '../../components/V1Select'
import { definitions, groups, suggestionsForMetric, entityOptionLabel } from '../home-assistant/messgroessen'
import type { HomeAssistantEntity } from '../../types'
import { haWert } from '../../utils'

/**
 * Fork AI (forkai.39): Die Messgrößen des Zelts — am selben Ort wie die Geräte.
 *
 * <b>Warum hier.</b> Wer wissen will, welche Entität den pH-Wert liefert, sucht
 * beim Gerät und nicht unter „Home Assistant". Der Reiter zeigt dieselben Daten
 * wie jene Seite und schreibt über denselben Endpunkt
 * (<c>PUT /api/settings/tents/:id</c>) — die Originalseite bleibt unverändert
 * bestehen und funktioniert weiter.
 *
 * <b>Was angezeigt wird.</b> Zugeordnete Größen und die als „core" gekennzeichneten;
 * die übrigen leeren stehen hinter einem Schalter. Zwölf leere Zeilen über acht
 * belegten wären Rauschen — aber eine fehlende Kernmessgröße ist eine Lücke, die
 * man sehen soll.
 */

type TentSensor = { metricType: string; haEntityId: string; displayLabel: string; isActive: boolean }
type Tent = { id: number; name: string; sensors: TentSensor[] }

export function MessgroessenReiter({ entities }: { entities: HomeAssistantEntity[] }) {
  const [zelte, setZelte] = useState<Tent[] | null>(null)
  const [aktiv, setAktiv] = useState<number | null>(null)
  const [fehler, setFehler] = useState<string | null>(null)
  const [hinweis, setHinweis] = useState<string | null>(null)
  const [speichert, setSpeichert] = useState(false)
  const [alleZeigen, setAlleZeigen] = useState(false)

  useEffect(() => {
    const abbruch = new AbortController()
    void (async () => {
      try {
        const geladen = await apiFetch<Tent[]>('/api/settings/tents', { signal: abbruch.signal })
        setZelte(geladen)
        setAktiv((bisher) => bisher ?? geladen[0]?.id ?? null)
      } catch (caught) {
        if (!abbruch.signal.aborted) setFehler(formatApiError(caught, 'Die Messgrößen konnten nicht geladen werden.'))
      }
    })()
    return () => abbruch.abort()
  }, [])

  const zelt = useMemo(() => zelte?.find((eintrag) => eintrag.id === aktiv) ?? zelte?.[0] ?? null, [zelte, aktiv])

  const livewert = (entityId: string) => {
    const treffer = entities.find((entity) => entity.entityId === entityId.trim())
    return treffer ? haWert(treffer.state, treffer.unitOfMeasurement) : null
  }

  async function setzen(metricType: string, entityId: string) {
    if (!zelt || !zelte) return
    const sensoren = zelt.sensors.filter((sensor) => sensor.metricType !== metricType)
    if (entityId.trim() !== '') {
      sensoren.push({ metricType, haEntityId: entityId.trim(), displayLabel: '', isActive: true })
    }

    setSpeichert(true)
    try {
      // Derselbe Endpunkt wie auf der Home-Assistant-Seite: ein Weg, ein Format.
      const gespeichert = await apiFetch<Tent>(`/api/settings/tents/${zelt.id}`, {
        method: 'PUT',
        body: JSON.stringify({ ...zelt, sensors: sensoren }),
      })
      setZelte(zelte.map((eintrag) => (eintrag.id === gespeichert.id ? gespeichert : eintrag)))
      setHinweis('Gespeichert.')
    } catch (caught) {
      setFehler(formatApiError(caught, 'Das Speichern hat nicht geklappt.'))
    } finally {
      setSpeichert(false)
    }
  }

  if (fehler) return <V1Alert tone="critical" message={fehler} />
  if (!zelte) return <V1Skeleton rows={5} label="Messgrößen werden geladen" />
  if (!zelt) return <V1Empty title="Kein Zelt" text="Sobald ein Zelt eingerichtet ist, stehen seine Messgrößen hier." />

  return (
    <>
      {hinweis && <V1Alert tone="ok" message={hinweis} />}

      {zelte.length > 1 && (
        <div className="gr-knoepfe">
          {zelte.map((eintrag) => (
            <V1Button
              key={eintrag.id}
              variant={eintrag.id === zelt.id ? 'primary' : undefined}
              onClick={() => setAktiv(eintrag.id)}
            >
              {eintrag.name}
            </V1Button>
          ))}
        </div>
      )}

      {groups.map((gruppe) => {
        const zeilen = definitions
          .filter((definition) => definition.group === gruppe.key)
          .map((definition) => ({
            definition,
            sensor: zelt.sensors.find((sensor) => sensor.metricType === definition.metricType),
          }))
          .filter(({ definition, sensor }) =>
            alleZeigen || (sensor?.haEntityId ?? '') !== '' || definition.importance === 'core')

        if (zeilen.length === 0) return null
        const belegt = zeilen.filter(({ sensor }) => (sensor?.haEntityId ?? '') !== '').length

        return (
          <V1Card key={gruppe.key}>
            <div className="gr-sec">
              {/* Der Zeltname steht nur einmal: „Zelt-RDWC · Zelt" las sich wie ein Fehler. */}
              <span>{gruppe.label === 'Zelt' ? zelt.name : `${zelt.name} · ${gruppe.label}`}</span>
              <b>{belegt} / {zeilen.length}</b>
            </div>

            {zeilen.map(({ definition, sensor }) => {
              const entityId = sensor?.haEntityId ?? ''
              const wert = livewert(entityId)
              return (
                <div key={definition.metricType} className="gr-messgroesse">
                  <div className="gr-messkopf">
                    <span>{definition.label}{definition.unit ? ` (${definition.unit})` : ''}</span>
                    <b className={wert ? undefined : 'is-leer'}>{wert ?? '—'}</b>
                  </div>
                  <V1Select
                    label=""
                    ariaLabel={definition.label}
                    titel={definition.label}
                    unterzeile={definition.placeholder}
                    wert={entityId}
                    platzhalter="nicht zugeordnet"
                    disabled={speichert}
                    onWahl={(neu) => void setzen(definition.metricType, neu)}
                    optionen={[
                      { wert: '', text: '— nicht zugeordnet —', betont: true },
                      ...suggestionsForMetric(entities, definition.metricType as never).map((entity): V1Option => ({
                        wert: entity.entityId,
                        text: entity.friendlyName ?? entity.entityId,
                        hinweis: entityOptionLabel(entity),
                      })),
                    ]}
                  />
                </div>
              )
            })}
          </V1Card>
        )
      })}

      <V1Card>
        <V1Switch
          label="Auch leere Messgrößen zeigen"
          checked={alleZeigen}
          onChange={setAlleZeigen}
          hint="Standardmäßig stehen hier die zugeordneten und die wichtigsten — der Rest bleibt weg."
        />
      </V1Card>
    </>
  )
}
