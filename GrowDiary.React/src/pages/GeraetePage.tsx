import { useEffect, useMemo, useState } from 'react'
import type { ReactNode } from 'react'

import { apiFetch, formatApiError } from '../api'
import { V1Alert, V1Button, V1Card, V1Empty, V1Page, V1Skeleton } from '../components/v1'
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
  modell: string | null
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

type SpeichernRequest = {
  name: string
  elternSchluessel: string | null
  anschluss: string | null
  tentId: number | null
  hardwareItemId: number | null
}

export default function GeraetePage() {
  const [seite, setSeite] = useState<Seite | null>(null)
  const [fehler, setFehler] = useState<string | null>(null)
  const [laedt, setLaedt] = useState(true)
  const [offen, setOffen] = useState<string | null>(null)
  const [bearbeitet, setBearbeitet] = useState<string | null>(null)
  // Offenes Aktionsmenue (⋯). Immer nur eines — zwei offene Menues auf einem
  // Handybildschirm sind genau der Platzfresser, den wir loswerden wollten.
  const [menue, setMenue] = useState<string | null>(null)
  const [entwurf, setEntwurf] = useState('')
  const [speichert, setSpeichert] = useState(false)
  // Aufgeklappte Controller. Die Liste startet eingeklappt: bei acht Ports am
  // RDWC ist die kurze Uebersicht der Zweck der Seite, nicht die lange Liste.
  const [auf, setAuf] = useState<Set<string>>(new Set())

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

  async function schreiben(pfad: string, methode: 'PUT', koerper?: unknown) {
    setSpeichert(true)
    try {
      setSeite(await apiFetch<Seite>(pfad, {
        method: methode,
        ...(koerper ? { body: JSON.stringify(koerper) } : {}),
      }))
      setFehler(null)
      setBearbeitet(null)
    } catch (caught) {
      setFehler(formatApiError(caught, 'Das Speichern hat nicht geklappt.'))
    } finally {
      setSpeichert(false)
    }
  }

  const umbenennen = (geraet: Geraet, name: string) => schreiben(
    `/api/geraete/${encodeURIComponent(geraet.schluessel)}`, 'PUT',
    {
      name,
      // null heisst 'nicht aendern', leer heisst 'haengt an nichts' — deshalb
      // den heutigen Wert mitschicken statt null.
      elternSchluessel: geraet.elternSchluessel ?? '',
      anschluss: geraet.anschluss,
      tentId: geraet.tentId,
      hardwareItemId: geraet.hardwareItemId,
    } satisfies SpeichernRequest,
  )

  // Kein Name: Aushängen soll den Namen nicht als Korrektur festschreiben.
  const aushaengen = (geraet: Geraet) => schreiben(
    `/api/geraete/${encodeURIComponent(geraet.schluessel)}`, 'PUT',
    {
      name: '',
      elternSchluessel: '',
      anschluss: null,
      tentId: geraet.tentId,
      hardwareItemId: geraet.hardwareItemId,
    } satisfies SpeichernRequest,
  )

  // Ausgeschrieben statt über `schreiben`: die Zählung der Löschwege sucht nach
  // `method: 'DELETE'` und einem Pfad davor — ein durchgereichter Parameter wäre
  // für sie unsichtbar, und der Knopf gälte als nicht vorhanden.
  async function verwerfen(geraet: Geraet) {
    setSpeichert(true)
    try {
      setSeite(await apiFetch<Seite>(`/api/geraete/${encodeURIComponent(geraet.schluessel)}/korrektur`, { method: 'DELETE' }))
      setFehler(null)
      setBearbeitet(null)
    } catch (caught) {
      setFehler(formatApiError(caught, 'Das Zurücksetzen hat nicht geklappt.'))
    } finally {
      setSpeichert(false)
    }
  }

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

  function werkzeug(geraet: Geraet) {
    if (bearbeitet === geraet.schluessel) {
      return (
        <div className="gr-werkzeug">
          <input
            value={entwurf}
            onChange={(event) => setEntwurf(event.target.value)}
            aria-label={`Name von ${geraet.name}`}
            placeholder="Name des Geräts"
          />
          <div className="gr-knoepfe">
            <V1Button onClick={() => setBearbeitet(null)}>Abbrechen</V1Button>
            <V1Button variant="primary" disabled={speichert || entwurf.trim() === ''} onClick={() => void umbenennen(geraet, entwurf.trim())}>
              {speichert ? 'Speichert …' : 'Speichern'}
            </V1Button>
          </div>
        </div>
      )
    }

    if (menue !== geraet.schluessel) return null

    return (
      <div className="gr-menue" role="menu" aria-label={`Aktionen für ${geraet.name}`}>
        <button type="button" role="menuitem" onClick={() => { setMenue(null); setBearbeitet(geraet.schluessel); setEntwurf(geraet.name) }}>
          <span aria-hidden="true">✎</span>Umbenennen
        </button>
        {geraet.elternSchluessel && (
          <button type="button" role="menuitem" disabled={speichert} onClick={() => { setMenue(null); void aushaengen(geraet) }}>
            <span aria-hidden="true">⤴</span>Aushängen
          </button>
        )}
        <button type="button" role="menuitem" disabled={speichert} onClick={() => { setMenue(null); void verwerfen(geraet) }}>
          <span aria-hidden="true">↺</span>Korrektur verwerfen
        </button>
      </div>
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

      {gruppen.map(({ geraet, kinder }) => {
        const zugeklappt = kinder.length > 0 && !auf.has(geraet.schluessel)
        return (
          <V1Card key={geraet.schluessel}>
            <GeraetZeile
              geraet={geraet}
              werkzeug={werkzeug(geraet)}
              onMenue={() => setMenue(menue === geraet.schluessel ? null : geraet.schluessel)}
              menueOffen={menue === geraet.schluessel}
              alleGeraete={seite.geraete}
              aufGeraet={(entityId, ziel) => void schreiben('/api/geraete/entitaet', 'PUT', { entityId, schluessel: ziel })}
              kinderZahl={kinder.length}
              zugeklappt={zugeklappt}
              offen={offen === geraet.schluessel}
              onKlick={() => {
                // Traegt das Geraet Ports, klappt der Pfeil DIE auf und zu.
                // Sonst — ein Geraet fuer sich — seine eigenen Entitaeten.
                if (kinder.length > 0) {
                  setAuf((bisher) => {
                    const neu = new Set(bisher)
                    if (neu.has(geraet.schluessel)) neu.delete(geraet.schluessel)
                    else neu.add(geraet.schluessel)
                    return neu
                  })
                  return
                }
                setOffen(offen === geraet.schluessel ? null : geraet.schluessel)
              }}
            />
            {!zugeklappt && kinder.map((kind) => (
              <GeraetZeile
                key={kind.schluessel}
                geraet={kind}
                werkzeug={werkzeug(kind)}
                onMenue={() => setMenue(menue === kind.schluessel ? null : kind.schluessel)}
                menueOffen={menue === kind.schluessel}
                alleGeraete={seite.geraete}
                aufGeraet={(entityId, ziel) => void schreiben('/api/geraete/entitaet', 'PUT', { entityId, schluessel: ziel })}
                eingerueckt
                offen={offen === kind.schluessel}
                onKlick={() => setOffen(offen === kind.schluessel ? null : kind.schluessel)}
              />
            ))}
          </V1Card>
        )
      })}

      <p className="gr-fuss">
        „Vermutet" heißt: weder Home Assistant noch du habt gesagt, zu welchem Gerät die Entität
        gehört — der Name war die einzige Spur. Geändert wird vorerst weiter dort, wo es heute steht;
        die Marke hinter jeder Entität sagt, wo das ist.
      </p>
    </V1Page>
  )
}

function GeraetZeile({ geraet, offen, onKlick, werkzeug, alleGeraete, aufGeraet, onMenue, menueOffen = false, eingerueckt = false, kinderZahl = 0, zugeklappt = false }: {
  geraet: Geraet
  offen: boolean
  onKlick: () => void
  werkzeug: ReactNode
  onMenue?: () => void
  menueOffen?: boolean
  alleGeraete: Geraet[]
  aufGeraet: (entityId: string, ziel: string) => void
  eingerueckt?: boolean
  kinderZahl?: number
  zugeklappt?: boolean
}) {
  const hatKinder = kinderZahl > 0
  // Das Modell faellt weg, wenn es nur den Namen wiederholt: „FRITZ!Box 7590 (UI)
  // · Controller · FRITZ!Box 7590 (UI)" liest sich wie ein Fehler.
  const modell = geraet.modell && geraet.modell.trim().toLowerCase() !== geraet.name.trim().toLowerCase()
    ? geraet.modell
    : null
  const unterzeile = [
    geraet.anschluss,
    geraet.istController ? 'Controller' : null,
    hatKinder ? modell : null,
    geraet.entitaeten.length === 0
      ? null
      : geraet.entitaeten.length === 1 ? '1 Entität' : `${geraet.entitaeten.length} Entitäten`,
  ].filter(Boolean).join(' · ')

  return (
    <div className={eingerueckt ? 'gr-zeile is-kind' : 'gr-zeile'}>
      <button type="button" className="gr-kopf" onClick={onKlick} aria-expanded={hatKinder ? !zugeklappt : offen}>
        <span className="gr-name">
          {geraet.name}
          {geraet.vermutet && <em className="gr-vermutet">vermutet</em>}
          <small>{unterzeile}</small>
        </span>
        {onMenue && (
          <span
            className={menueOffen ? 'gr-mehr is-offen' : 'gr-mehr'}
            role="button"
            tabIndex={0}
            aria-label={`Aktionen für ${geraet.name}`}
            aria-expanded={menueOffen}
            onClick={(event) => { event.stopPropagation(); onMenue() }}
            onKeyDown={(event) => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); event.stopPropagation(); onMenue() } }}
          >
            ⋯
          </span>
        )}
        {hatKinder && (
          <span className="gr-anzahl" title={kinderZahl === 1 ? '1 angeschlossenes Gerät' : `${kinderZahl} angeschlossene Geräte`}>
            {kinderZahl}
          </span>
        )}
        <span className="gr-pfeil" aria-hidden="true">{(hatKinder ? !zugeklappt : offen) ? '⌄' : '›'}</span>
      </button>

      {hatKinder && !zugeklappt && werkzeug}

      {!hatKinder && offen && (
        <>
          {werkzeug}
          {geraet.entitaeten.length === 0 ? (
            <p className="gr-leer">Keine Entität — das Gerät steht nur im Inventar.</p>
          ) : (
            <ul className="gr-entitaeten">
            {geraet.entitaeten.map((entitaet) => (
              <li key={entitaet.entityId}>
                <code>{entitaet.entityId}</code>
                <select
                  className="gr-umhaengen"
                  value={geraet.schluessel}
                  aria-label={`${entitaet.entityId} einem Gerät zuordnen`}
                  onChange={(event) => aufGeraet(entitaet.entityId, event.target.value)}
                >
                  {alleGeraete.map((ziel) => (
                    <option key={ziel.schluessel} value={ziel.schluessel}>{ziel.name}</option>
                  ))}
                </select>
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
          )}
        </>
      )}
    </div>
  )
}
