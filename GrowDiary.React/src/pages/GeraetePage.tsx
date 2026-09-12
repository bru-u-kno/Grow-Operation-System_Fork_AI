import { useEffect, useMemo, useState } from 'react'
import type { ReactNode } from 'react'

import { apiFetch, formatApiError } from '../api'
import { V1Alert, V1Button, V1Card, V1Empty, V1Page, V1Skeleton } from '../components/v1'
import { V1Select } from '../components/V1Select'
import type { V1Option } from '../components/V1Select'
import { V1Sheet } from '../components/V1Sheet'
import { V1Tabs } from '../components/v1'
import { MessgroessenReiter } from '../features/geraete/MessgroessenReiter'
import type { HomeAssistantEntity } from '../types'
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
type Entitaet = { entityId: string; verwendungen: Verwendung[]; verschoben: boolean; herkunftName: string | null }
type Geraet = {
  schluessel: string
  name: string
  elternSchluessel: string | null
  anschluss: string | null
  istController: boolean
  istRubrik: boolean
  elternVomNutzer: boolean
  nameVomNutzer: boolean
  abgeleiteterEltern: string | null
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
  anzahlVerschoben: number
}

/** Die Ziele einer Entität: erst der Rückweg, dann Rubriken, dann Geräte. */
function zielOptionen(alle: Geraet[]): V1Option[] {
  return [
    { wert: '', text: '— dorthin, wo Home Assistant sie zählt —', betont: true },
    ...alle.map((ziel): V1Option => ({
      wert: ziel.schluessel,
      text: ziel.name,
      hinweis: ziel.anschluss,
      gruppe: ziel.istRubrik ? 'Rubriken' : 'Geräte',
    })),
  ]
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
  const [rubrikName, setRubrikName] = useState<string | null>(null)
  // Die Korrekturliste klappt zu: drei Kameras schoben die Geraete weit nach
  // unten. Zugeklappt bleibt eine ZEILE MIT TEXT stehen — eine blosse Zahl
  // hatte niemand als Schalter erkannt.
  const [korrekturenOffen, setKorrekturenOffen] = useState(false)
  const [reiter, setReiter] = useState<'geraete' | 'messgroessen'>('geraete')
  const [entities, setEntities] = useState<HomeAssistantEntity[]>([])
  // Was zuletzt geschah, samt Rueckweg. Die Auswahl unter einer Entitaet
  // schreibt sofort; ohne diese Zeile merkt man einen Fehlgriff erst Tage
  // spaeter und sucht die Entitaet dann im falschen Geraet.
  const [letzte, setLetzte] = useState<{ text: string; zurueck?: () => Promise<void> } | null>(null)
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

  // Die Entitaetenliste braucht nur der Messgroessen-Reiter — sie kommt trotzdem
  // einmal fuer die ganze Seite, damit ein Reiterwechsel nicht jedes Mal laedt.
  useEffect(() => {
    const abbruch = new AbortController()
    void (async () => {
      try {
        setEntities(await apiFetch<HomeAssistantEntity[]>('/api/home-assistant/entities', { signal: abbruch.signal }))
      } catch {
        // Ohne Home Assistant bleibt die Liste leer; die Auswahl zeigt dann nur den Rückweg.
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
  // Ein Gerät in eine Rubrik oder unter einen Controller hängen. Leeres Ziel =
  // hängt an nichts; der Name bleibt leer, damit HA weiter durchschlägt.
  const verschieben = (geraet: Geraet, ziel: string) => schreiben(
    `/api/geraete/${encodeURIComponent(geraet.schluessel)}`, 'PUT',
    {
      name: '',
      elternSchluessel: ziel,
      anschluss: ziel === '' ? null : geraet.anschluss,
      tentId: geraet.tentId,
      hardwareItemId: geraet.hardwareItemId,
    } satisfies SpeichernRequest,
  )

  async function rubrikAnlegen(name: string) {
    setSpeichert(true)
    try {
      setSeite(await apiFetch<Seite>('/api/geraete/rubrik', { method: 'POST', body: JSON.stringify({ name }) }))
      setRubrikName(null)
    } catch (caught) {
      setFehler(formatApiError(caught, 'Die Rubrik konnte nicht angelegt werden.'))
    } finally {
      setSpeichert(false)
    }
  }

  async function entitaetVerschieben(entityId: string, vonGeraet: Geraet, zielSchluessel: string) {
    const ziel = (seite?.geraete ?? []).find((g) => g.schluessel === zielSchluessel)
    await schreiben('/api/geraete/entitaet', 'PUT', { entityId, schluessel: zielSchluessel })
    setLetzte({
      text: zielSchluessel === ''
        ? `${entityId} steht wieder dort, wo Home Assistant sie zählt.`
        : `${entityId} steht jetzt bei „${ziel?.name ?? zielSchluessel}".`,
      // Zurueck heisst: wieder dem Geraet zuschlagen, aus dem sie kam.
      zurueck: async () => {
        await schreiben('/api/geraete/entitaet', 'PUT', { entityId, schluessel: vonGeraet.schluessel })
        setLetzte(null)
      },
    })
  }

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

  // Alle von Hand verschobenen Entitäten — die Antwort auf „wohin habe ich das
  // eigentlich geschoben?". Ohne diese Liste muss man jede Karte aufklappen.
  const verschobene = (seite?.geraete ?? []).flatMap((g) =>
    g.entitaeten.filter((e) => e.verschoben).map((e) => ({ entitaet: e, geraet: g })))

  // Korrigierte Geräte: umgehängt oder umbenannt. Rubriken sind keine Korrektur,
  // sie sind Absicht.
  const korrigierteGeraete = (seite?.geraete ?? []).filter((g) => !g.istRubrik && (g.elternVomNutzer || g.nameVomNutzer))

  const nameVon = (schluessel: string | null) =>
    (seite?.geraete ?? []).find((g) => g.schluessel === schluessel)?.name ?? null

  // Wohin sich ein Gerät hängen lässt: in eine Rubrik oder unter einen
  // Controller. Ein Port-Gerät als Ziel wäre eine dritte Ebene — die zeigt die
  // Liste nicht, also bietet sie es auch nicht an.
  const ziele = (seite?.geraete ?? []).filter((g) => g.istRubrik || g.istController)

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
        {!geraet.istRubrik && ziele.length > 0 && (
          <div className="gr-menue-ziel">
            <V1Select
              label="Verschieben nach"
              titel="Verschieben nach"
              unterzeile={geraet.name}
              wert={geraet.elternSchluessel ?? ''}
              disabled={speichert}
              onWahl={(ziel) => { setMenue(null); void verschieben(geraet, ziel) }}
              optionen={[
                { wert: '', text: '— an nichts —', betont: true },
                ...ziele
                  .filter((ziel) => ziel.schluessel !== geraet.schluessel)
                  .map((ziel): V1Option => ({
                    wert: ziel.schluessel,
                    text: ziel.name,
                    gruppe: ziel.istRubrik ? 'Rubriken' : 'Controller',
                  })),
              ]}
            />
          </div>
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
      <V1Tabs
        label="Bereich"
        active={reiter}
        onChange={setReiter}
        items={[
          { value: 'geraete', label: 'Geräte' },
          { value: 'messgroessen', label: 'Messgrößen' },
        ]}
      />

      {reiter === 'messgroessen' ? <MessgroessenReiter entities={entities} /> : <>

      {letzte && (
        <div className="gr-meldung">
          <span>{letzte.text}</span>
          {letzte.zurueck && (
            <button type="button" disabled={speichert} onClick={() => void letzte.zurueck?.()}>Rückgängig</button>
          )}
          <button type="button" aria-label="Meldung schließen" onClick={() => setLetzte(null)}>✕</button>
        </div>
      )}

      <V1Card>
        <div className="gr-zahlen">
          <div><b>{seite.anzahlGeraete}</b><span>Geräte</span></div>
          <div><b>{seite.anzahlEntitaeten}</b><span>Entitäten</span></div>
          <div><b className={seite.anzahlVermutet > 0 ? 'is-warn' : undefined}>{seite.anzahlVermutet}</b><span>vermutet</span></div>
          <div>
            <b className={seite.anzahlVerschoben > 0 ? 'is-korrigiert' : undefined}>{seite.anzahlVerschoben}</b>
            <span>korrigiert</span>
          </div>
        </div>

        {(verschobene.length > 0 || korrigierteGeraete.length > 0) && (
          <>
          <button
            type="button"
            className="gr-korrekturen-schalter"
            aria-expanded={korrekturenOffen}
            onClick={() => setKorrekturenOffen((offen) => !offen)}
          >
            <span>
              {verschobene.length + korrigierteGeraete.length === 1
                ? '1 Eintrag von Hand gesetzt'
                : `${verschobene.length + korrigierteGeraete.length} Einträge von Hand gesetzt`}
            </span>
            <em aria-hidden="true">{korrekturenOffen ? '⌄' : '›'}</em>
          </button>
          {korrekturenOffen && (
          <ul className="gr-verschobene">
            {verschobene.map(({ entitaet, geraet }) => (
              <li key={entitaet.entityId}>
                <code>{entitaet.entityId}</code>
                <small>
                  steht bei „{geraet.name}"
                  {entitaet.herkunftName ? ` · laut Home Assistant: ${entitaet.herkunftName}` : ''}
                </small>
                <button type="button" disabled={speichert} onClick={() => void entitaetVerschieben(entitaet.entityId, geraet, '')}>
                  Zurück
                </button>
              </li>
            ))}
            {korrigierteGeraete.map((g) => (
              <li key={`geraet-${g.schluessel}`}>
                <code>{g.name}</code>
                <small>
                  {g.elternVomNutzer
                    ? (g.elternSchluessel
                        ? `hängt an „${nameVon(g.elternSchluessel) ?? g.elternSchluessel}"`
                        : 'hängt an nichts')
                    : 'umbenannt'}
                  {g.abgeleiteterEltern && g.abgeleiteterEltern !== g.elternSchluessel
                    ? ` · laut Home Assistant: ${nameVon(g.abgeleiteterEltern) ?? g.abgeleiteterEltern}`
                    : ''}
                </small>
                <button type="button" disabled={speichert} onClick={() => void verwerfen(g)}>Zurück</button>
              </li>
            ))}
          </ul>
          )}
          </>
        )}
        <div className="gr-knoepfe">
          <V1Button onClick={() => setRubrikName('')}>Rubrik anlegen</V1Button>
        </div>
      </V1Card>

      {gruppen.map(({ geraet, kinder }) => {
        const zugeklappt = kinder.length > 0 && !auf.has(geraet.schluessel)
        return (
          <V1Card key={geraet.schluessel}>
            <GeraetZeile
              geraet={geraet}
              // Beim Controller zaehlt der ganze Baum mit: sonst sieht man die
              // gelbe Marke erst nach dem Aufklappen und sucht sie genau dort,
              // wo man sie nicht vermutet.
              verschobenImBaum={
                geraet.entitaeten.filter((e) => e.verschoben).length
                + (!geraet.istRubrik && (geraet.elternVomNutzer || geraet.nameVomNutzer) ? 1 : 0)
                + kinder.reduce((summe, kind) => summe
                  + kind.entitaeten.filter((e) => e.verschoben).length
                  + (kind.elternVomNutzer || kind.nameVomNutzer ? 1 : 0), 0)
              }
              werkzeug={werkzeug(geraet)}
              onMenue={() => setMenue(menue === geraet.schluessel ? null : geraet.schluessel)}
              menueOffen={menue === geraet.schluessel}
              alleGeraete={seite.geraete}
              aufGeraet={(entityId, ziel) => void entitaetVerschieben(entityId, geraet, ziel)}
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
                verschobenImBaum={kind.entitaeten.filter((e) => e.verschoben).length
                  + (kind.elternVomNutzer || kind.nameVomNutzer ? 1 : 0)}
                werkzeug={werkzeug(kind)}
                onMenue={() => setMenue(menue === kind.schluessel ? null : kind.schluessel)}
                menueOffen={menue === kind.schluessel}
                alleGeraete={seite.geraete}
                aufGeraet={(entityId, ziel) => void entitaetVerschieben(entityId, kind, ziel)}
                eingerueckt
                offen={offen === kind.schluessel}
                onKlick={() => setOffen(offen === kind.schluessel ? null : kind.schluessel)}
              />
            ))}
          </V1Card>
        )
      })}

      <V1Sheet
        open={rubrikName !== null}
        onClose={() => setRubrikName(null)}
        title="Rubrik anlegen"
        subtitle="Ein Fach für Geräte, die zusammengehören — etwa alle Kameras."
        footer={
          <div className="gr-knoepfe">
            <V1Button onClick={() => setRubrikName(null)}>Abbrechen</V1Button>
            <V1Button
              variant="primary"
              disabled={speichert || (rubrikName ?? '').trim() === ''}
              onClick={() => void rubrikAnlegen((rubrikName ?? '').trim())}
            >
              {speichert ? 'Legt an …' : 'Anlegen'}
            </V1Button>
          </div>
        }
      >
        <label className="v1-field">
          <span>Name</span>
          <input
            value={rubrikName ?? ''}
            autoFocus
            onChange={(event) => setRubrikName(event.target.value)}
            placeholder="z. B. Kameras"
          />
        </label>
      </V1Sheet>

      </>}

      <p className="gr-fuss">
        „Vermutet" heißt: weder Home Assistant noch du habt gesagt, zu welchem Gerät die Entität
        gehört — der Name war die einzige Spur. Geändert wird vorerst weiter dort, wo es heute steht;
        die Marke hinter jeder Entität sagt, wo das ist.
      </p>
    </V1Page>
  )
}

function GeraetZeile({ geraet, offen, onKlick, werkzeug, alleGeraete, aufGeraet, onMenue, menueOffen = false, eingerueckt = false, kinderZahl = 0, zugeklappt = false, verschobenImBaum = 0 }: {
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
  verschobenImBaum?: number
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
        {verschobenImBaum > 0 && (
          <span
            className="gr-anzahl is-korrigiert"
            title={verschobenImBaum === 1
              ? '1 Korrektur von Hand — hier oder an einem Port'
              : `${verschobenImBaum} Korrekturen von Hand`}
          >
            {verschobenImBaum}
          </span>
        )}
        {hatKinder && (
          <span className="gr-anzahl" title={kinderZahl === 1 ? '1 angeschlossenes Gerät' : `${kinderZahl} angeschlossene Geräte`}>
            {kinderZahl}
          </span>
        )}
        <span className="gr-pfeil" aria-hidden="true">{(hatKinder ? !zugeklappt : offen) ? '⌄' : '›'}</span>
      </button>

      {/* Das Menue haengt am ⋯, nicht am Aufklappen: es soll auch bei einer
          zugeklappten Zeile erscheinen. `werkzeug` liefert null, solange das
          Menue dieses Geraets nicht offen ist. */}
      {werkzeug}

      {!hatKinder && offen && (
        <>
          {geraet.entitaeten.length === 0 ? (
            <p className="gr-leer">Keine Entität — das Gerät steht nur im Inventar.</p>
          ) : (
            <ul className="gr-entitaeten">
            {geraet.entitaeten.map((entitaet) => (
              <li key={entitaet.entityId}>
                <code>{entitaet.entityId}</code>
                <V1Select
                  label="Gehört zu"
                  titel="Gehört zu"
                  unterzeile={entitaet.entityId}
                  wert={geraet.schluessel}
                  onWahl={(ziel) => aufGeraet(entitaet.entityId, ziel)}
                  optionen={zielOptionen(alleGeraete)}
                />
                {entitaet.verschoben && (
                  <p className="gr-verschoben-hinweis">
                    <em>verschoben</em>
                    {entitaet.herkunftName ? ` laut Home Assistant: ${entitaet.herkunftName}` : ' — von Hand zugeordnet'}
                  </p>
                )}
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
