import { useEffect, useMemo, useState } from 'react'

import { apiFetch, formatApiError } from '../../api'
import { V1Alert, V1Card, V1Empty, V1LinkButton, V1Skeleton } from '../../components/v1'

/**
 * Fork AI (forkai.41): Wartung — nach Gerät statt nach Eintrag.
 *
 * <b>Warum hier.</b> „Sensoren & Wartung" listet Einträge; wer wissen will, was
 * am Bluelab ansteht, sucht sie zusammen. Dieser Reiter dreht die Sortierung um
 * und nimmt die Geräte mit, die gar keine Entität haben — CO₂-Flasche,
 * Verschleißteile.
 *
 * <b>Nur lesend.</b> Erfasst und geändert wird weiter auf „Sensoren & Wartung";
 * jede Zeile führt dorthin. Damit bleibt die Originalseite unberührt, und
 * Weiterentwicklungen des Originals kommen ohne Handarbeit an.
 */

type HardwareItem = {
  id: number
  name: string
  category: string
  status: string
  haEntityId: string | null
  calibrationIntervalDays: number | null
  inspectionIntervalDays: number | null
  expectedLifespanDays: number | null
  installedAtUtc: string | null
}

type MaintenanceEvent = {
  id: number
  hardwareItemId: number
  title: string
  status: string
  dueAtUtc: string | null
  performedAtUtc: string | null
  nextDueAtUtc: string | null
}

type Zeile = {
  schluessel: string
  titel: string
  geraet: string
  faellig: Date | null
  /** Ohne Frist: der Eintrag steht nur zur Information da. */
  ohneFrist: boolean
}

const TAG = 24 * 60 * 60 * 1000

function tageBis(datum: Date): number {
  return Math.round((datum.getTime() - Date.now()) / TAG)
}

function frist(datum: Date | null): string {
  if (!datum) return 'ohne Frist'
  const tage = tageBis(datum)
  if (tage < 0) return `überfällig · ${Math.abs(tage)} T`
  if (tage === 0) return 'heute'
  if (tage === 1) return 'morgen'
  return `in ${tage} T`
}

export function WartungReiter({ geraeteNamen }: { geraeteNamen: Map<number, string> }) {
  const [teile, setTeile] = useState<HardwareItem[] | null>(null)
  const [ereignisse, setEreignisse] = useState<MaintenanceEvent[]>([])
  const [fehler, setFehler] = useState<string | null>(null)

  useEffect(() => {
    const abbruch = new AbortController()
    void (async () => {
      try {
        const [geladeneTeile, geladeneEreignisse] = await Promise.all([
          apiFetch<HardwareItem[]>('/api/hardware-items', { signal: abbruch.signal }),
          apiFetch<MaintenanceEvent[]>('/api/maintenance-events', { signal: abbruch.signal }),
        ])
        setTeile(geladeneTeile)
        setEreignisse(geladeneEreignisse)
      } catch (caught) {
        if (!abbruch.signal.aborted) setFehler(formatApiError(caught, 'Die Wartung konnte nicht geladen werden.'))
      }
    })()
    return () => abbruch.abort()
  }, [])

  const zeilen = useMemo<Zeile[]>(() => {
    if (!teile) return []
    const nachId = new Map(teile.map((teil) => [teil.id, teil]))

    // Offene Ereignisse zuerst: sie tragen eine echte Frist.
    const ausEreignissen = ereignisse
      .filter((ereignis) => ereignis.performedAtUtc == null)
      .map((ereignis): Zeile => ({
        schluessel: `e-${ereignis.id}`,
        titel: ereignis.title,
        geraet: geraeteNamen.get(ereignis.hardwareItemId) ?? nachId.get(ereignis.hardwareItemId)?.name ?? 'Unbekannt',
        faellig: ereignis.dueAtUtc ? new Date(ereignis.dueAtUtc) : null,
        ohneFrist: ereignis.dueAtUtc == null,
      }))

    // Dazu die Teile, die ein Intervall tragen, aber noch kein Ereignis haben —
    // sonst sieht man eine Kalibrierfrist erst, nachdem sie einmal lief.
    const mitEreignis = new Set(ausEreignissen.map((zeile) => zeile.geraet))
    const ausTeilen = teile
      .filter((teil) => teil.calibrationIntervalDays != null || teil.inspectionIntervalDays != null)
      .filter((teil) => !mitEreignis.has(geraeteNamen.get(teil.id) ?? teil.name))
      .map((teil): Zeile => {
        const tage = teil.calibrationIntervalDays ?? teil.inspectionIntervalDays ?? 0
        const start = teil.installedAtUtc ? new Date(teil.installedAtUtc) : null
        return {
          schluessel: `t-${teil.id}`,
          titel: teil.calibrationIntervalDays != null ? 'Kalibrieren' : 'Prüfen',
          geraet: geraeteNamen.get(teil.id) ?? teil.name,
          faellig: start ? new Date(start.getTime() + tage * TAG) : null,
          ohneFrist: start == null,
        }
      })

    return [...ausEreignissen, ...ausTeilen].sort((a, b) => {
      if (a.faellig && b.faellig) return a.faellig.getTime() - b.faellig.getTime()
      if (a.faellig) return -1
      if (b.faellig) return 1
      return a.geraet.localeCompare(b.geraet)
    })
  }, [teile, ereignisse, geraeteNamen])

  if (fehler) return <V1Alert tone="critical" message={fehler} />
  if (!teile) return <V1Skeleton rows={5} label="Wartung wird geladen" />

  if (zeilen.length === 0) {
    return (
      <V1Empty
        title="Nichts zu tun"
        text="Sobald ein Gerät ein Kalibrier- oder Prüfintervall trägt, steht es hier — nach Fälligkeit sortiert."
      />
    )
  }

  const offen = zeilen.filter((zeile) => zeile.faellig != null && tageBis(zeile.faellig) <= 7)
  const spaeter = zeilen.filter((zeile) => !offen.includes(zeile))

  return (
    <>
      {[{ titel: 'Fällig', liste: offen }, { titel: 'Später', liste: spaeter }]
        .filter((gruppe) => gruppe.liste.length > 0)
        .map((gruppe) => (
          <V1Card key={gruppe.titel}>
            <div className="gr-sec"><span>{gruppe.titel}</span><b>{gruppe.liste.length}</b></div>
            {gruppe.liste.map((zeile) => {
              const tage = zeile.faellig ? tageBis(zeile.faellig) : null
              return (
                <div key={zeile.schluessel} className="gr-wartung">
                  <span className="nm">
                    <b>{zeile.titel}</b>
                    <small>{zeile.geraet}</small>
                  </span>
                  <span className={tage != null && tage <= 7 ? 'gr-frist is-faellig' : 'gr-frist'}>
                    {frist(zeile.faellig)}
                  </span>
                </div>
              )
            })}
          </V1Card>
        ))}

      <p className="gr-fuss">
        Erfasst und geändert wird weiter unter „Sensoren &amp; Wartung" — hier stehen dieselben
        Einträge, nur nach Gerät und Fälligkeit sortiert.{' '}
        <V1LinkButton to="/sensoren" variant="ghost">Zu Sensoren &amp; Wartung</V1LinkButton>
      </p>
    </>
  )
}
