import { useEffect, useMemo, useState } from 'react'

import { apiFetch, formatApiError } from '../api'
import { V1Alert, V1Card, V1Empty, V1Page, V1Skeleton } from '../components/v1'
import './geraete.css'

/**
 * Fork AI (forkai.22): Alle Geräte an einer Stelle.
 *
 * <b>Was die Seite ist.</b> Der Fork benutzt Entitäten an sechs Stellen —
 * Messgrößen, Zelt-Technik, Inventar, Dosierpumpen, Steuerungs-Rollen,
 * Stromzähler. Hier stehen sie nach Gerät sortiert, und hinter jeder Entität
 * steht, wofür sie benutzt wird.
 *
 * <b>Was sie (noch) nicht ist.</b> Eine Pflegestelle. Diese Etappe zeigt nur;
 * geändert wird weiter dort, wo es heute steht. Die Reiter der Geräteseite
 * kommen als nächstes.
 */

type Verwendung = { zweck: string; quelle: string }
type Entitaet = { entityId: string; verwendungen: Verwendung[] }
type Geraet = {
  schluessel: string
  name: string
  elternSchluessel: string | null
  anschluss: string | null
  istController: boolean
  bestaetigt: boolean
  vermutet: boolean
  tentId: number | null
  hardwareItemId: number | null
  entitaeten: Entitaet[]
}
type Seite = {
  geraete: Geraet[]
  anzahlGeraete: number
  anzahlEntitaeten: number
  anzahlVermutet: number
}

const QUELLEN: Record<string, string> = {
  messgroesse: 'Messgröße',
  zelt: 'Zelt-Technik',
  inventar: 'Inventar',
  dosierung: 'Dosierung',
  steuerung: 'Steuerung',
  strom: 'Strom',
}

export default function GeraetePage() {
  const [seite, setSeite] = useState<Seite | null>(null)
  const [fehler, setFehler] = useState<string | null>(null)
  const [laedt, setLaedt] = useState(true)
  const [offen, setOffen] = useState<string | null>(null)

  useEffect(() => {
    const abbruch = new AbortController()
    void (async () => {
      try {
        setSeite(await apiFetch<Seite>('/api/geraete', { signal: abbruch.signal }))
      } catch (caught) {
        if (!abbruch.signal.aborted) setFehler(formatApiError(caught, 'Die Geräte konnten nicht geladen werden.'))
      } finally {
        if (!abbruch.signal.aborted) setLaedt(false)
      }
    })()
    return () => abbruch.abort()
  }, [])

  // Controller zuerst, ihre Ports direkt darunter — die Reihenfolge kommt aus
  // dem Dienst, hier wird nur die Verschachtelung sichtbar gemacht.
  const gruppen = useMemo(() => {
    if (!seite) return []
    const kinder = new Map<string, Geraet[]>()
    for (const geraet of seite.geraete) {
      if (!geraet.elternSchluessel) continue
      const liste = kinder.get(geraet.elternSchluessel) ?? []
      liste.push(geraet)
      kinder.set(geraet.elternSchluessel, liste)
    }
    return seite.geraete
      .filter((geraet) => !geraet.elternSchluessel)
      .map((geraet) => ({ geraet, kinder: kinder.get(geraet.schluessel) ?? [] }))
  }, [seite])

  if (laedt) return <V1Page eyebrow="Betrieb" title="Geräte & Entitäten"><V1Skeleton rows={6} label="Geräte werden geladen" /></V1Page>

  if (fehler) {
    return (
      <V1Page eyebrow="Betrieb" title="Geräte & Entitäten">
        <V1Alert tone="critical" message={fehler} />
      </V1Page>
    )
  }

  if (!seite || seite.geraete.length === 0) {
    return (
      <V1Page eyebrow="Betrieb" title="Geräte & Entitäten">
        <V1Empty
          title="Noch keine Geräte"
          text="Sobald eine Entität irgendwo im Fork benutzt wird — als Messgröße, in einer Steuerung oder für den Strom — steht ihr Gerät hier."
        />
      </V1Page>
    )
  }

  return (
    <V1Page
      eyebrow="Betrieb"
      title="Geräte & Entitäten"
      subtitle="Alles, was der Fork an Home Assistant benutzt — nach Gerät sortiert."
    >
      <V1Card>
        <div className="gr-zahlen">
          <div><b>{seite.anzahlGeraete}</b><span>Geräte</span></div>
          <div><b>{seite.anzahlEntitaeten}</b><span>Entitäten</span></div>
          <div><b className={seite.anzahlVermutet > 0 ? 'is-warn' : undefined}>{seite.anzahlVermutet}</b><span>vermutet</span></div>
        </div>
      </V1Card>

      {gruppen.map(({ geraet, kinder }) => (
        <V1Card key={geraet.schluessel}>
          <GeraetZeile geraet={geraet} offen={offen === geraet.schluessel} onKlick={() => setOffen(offen === geraet.schluessel ? null : geraet.schluessel)} />
          {kinder.map((kind) => (
            <GeraetZeile
              key={kind.schluessel}
              geraet={kind}
              eingerueckt
              offen={offen === kind.schluessel}
              onKlick={() => setOffen(offen === kind.schluessel ? null : kind.schluessel)}
            />
          ))}
        </V1Card>
      ))}

      <p className="gr-fuss">
        „Vermutet" heißt: weder Home Assistant noch du habt gesagt, zu welchem Gerät die Entität
        gehört — der Name war die einzige Spur. Geändert wird vorerst weiter dort, wo es heute steht;
        die Marke hinter jeder Entität sagt, wo das ist.
      </p>
    </V1Page>
  )
}

function GeraetZeile({ geraet, offen, onKlick, eingerueckt = false }: {
  geraet: Geraet
  offen: boolean
  onKlick: () => void
  eingerueckt?: boolean
}) {
  const unterzeile = [
    geraet.anschluss,
    geraet.entitaeten.length === 1 ? '1 Entität' : `${geraet.entitaeten.length} Entitäten`,
    geraet.istController ? 'Controller' : null,
  ].filter(Boolean).join(' · ')

  return (
    <div className={eingerueckt ? 'gr-zeile is-kind' : 'gr-zeile'}>
      <button type="button" className="gr-kopf" onClick={onKlick} aria-expanded={offen}>
        <span className="gr-name">
          {geraet.name}
          {geraet.vermutet && <em className="gr-vermutet">vermutet</em>}
          <small>{unterzeile}</small>
        </span>
        <span className="gr-pfeil" aria-hidden="true">{offen ? '⌄' : '›'}</span>
      </button>

      {offen && (
        geraet.entitaeten.length === 0 ? (
          <p className="gr-leer">Keine Entität — das Gerät steht nur im Inventar.</p>
        ) : (
          <ul className="gr-entitaeten">
            {geraet.entitaeten.map((entitaet) => (
              <li key={entitaet.entityId}>
                <code>{entitaet.entityId}</code>
                <span className="gr-marken">
                  {entitaet.verwendungen.map((verwendung) => (
                    <em key={`${verwendung.quelle}-${verwendung.zweck}`} title={QUELLEN[verwendung.quelle] ?? verwendung.quelle}>
                      {verwendung.zweck}
                    </em>
                  ))}
                </span>
              </li>
            ))}
          </ul>
        )
      )}
    </div>
  )
}
