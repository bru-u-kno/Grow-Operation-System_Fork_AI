import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { ApiRequestError, apiFetch, formatApiError } from '../api'
import { V1Alert, V1Button, V1Card, V1Empty, V1Field, V1Page, V1Section, V1Skeleton } from '../components/v1'
import { V1Select } from '../components/V1Select'
import type { V1Option } from '../components/V1Select'
import type { HomeAssistantEntity } from '../types'
import type { GeraeteSeite, GeraetZeile } from '../features/steuerung/steuerung-typen'
import { haWert } from '../utils'
import '../features/steuerung/steuerung.css'

/**
 * Fork AI (forkai.21): Geräte & Entitäten der Steuerungen.
 *
 * <b>Warum eine eigene Seite.</b> Licht-Status, Canopy-Fühler und Abluft nutzen
 * mehrere Regelungen. Stünde die Zuordnung je Steuerung, trüge man dieselbe
 * Entität mehrfach ein und bei einem Gerätetausch an drei Stellen nach. Hier
 * hängt jedes Gerät an einer Zeile; die Steuerungen verweisen darauf.
 *
 * <b>Vorgabe statt Vorbefüllung.</b> Leer heißt „wie ab Werk" — die Rolle nimmt
 * dann den Wert, mit dem das Add-on ausgeliefert wurde. Nur eine bewusst
 * geänderte Zeile landet in der Datenbank.
 */

const GRUPPEN: Array<{ key: string; label: string }> = [
  { key: 'messen', label: 'Messen' },
  { key: 'schalten', label: 'Schalten' },
  { key: 'umfeld', label: 'Umfeld' },
]

export default function SteuerungGeraetePage() {
  const navigate = useNavigate()
  const [seite, setSeite] = useState<GeraeteSeite | null>(null)
  const [entities, setEntities] = useState<HomeAssistantEntity[]>([])
  const [aktiv, setAktiv] = useState<string | null>(null)
  // Nur die Abweichungen je Steuerung, nicht der ganze Entwurf: so folgt das
  // Formular dem Serverstand von selbst und braucht keinen Effekt, der beim
  // Wechsel der Steuerung Zustand nachzieht.
  const [aenderungen, setAenderungen] = useState<Record<string, Record<string, string>>>({})
  const [feldFehler, setFeldFehler] = useState<Record<string, string>>({})
  const [fehler, setFehler] = useState<string | null>(null)
  const [hinweis, setHinweis] = useState<string | null>(null)
  const [speichert, setSpeichert] = useState(false)
  const [laedt, setLaedt] = useState(true)

  useEffect(() => {
    const controller = new AbortController()
    const laden = async () => {
      setLaedt(true)
      try {
        const [geladen, liste] = await Promise.all([
          apiFetch<GeraeteSeite>('/api/steuerung/geraete', { signal: controller.signal }),
          apiFetch<HomeAssistantEntity[]>('/api/home-assistant/entities', { signal: controller.signal }).catch(() => []),
        ])
        if (controller.signal.aborted) return
        setSeite(geladen)
        setEntities(liste)
        setAktiv((current) => current ?? geladen.module[0]?.modul ?? null)
        setFehler(null)
      } catch (caught) {
        if (!controller.signal.aborted) setFehler(formatApiError(caught, 'Die Geräte konnten nicht geladen werden.'))
      } finally {
        if (!controller.signal.aborted) setLaedt(false)
      }
    }
    void laden()
    return () => controller.abort()
  }, [])

  const modul = useMemo(
    () => seite?.module.find((m) => m.modul === aktiv) ?? seite?.module[0] ?? null,
    [seite, aktiv],
  )

  const entwurf = useMemo(() => {
    if (!modul) return {} as Record<string, string>
    const gespeichert = Object.fromEntries(modul.zeilen.map((zeile) => [zeile.rolle, zeile.eingetragen]))
    return { ...gespeichert, ...(aenderungen[modul.modul] ?? {}) }
  }, [modul, aenderungen])

  const setzeRolle = (rolle: string, wert: string) => {
    if (!modul) return
    setAenderungen((current) => ({ ...current, [modul.modul]: { ...(current[modul.modul] ?? {}), [rolle]: wert } }))
  }

  const offen = modul?.zeilen.filter((zeile) => zeile.pflicht && !zeile.gefunden).length ?? 0

  async function speichern() {
    if (!modul) return
    setSpeichert(true)
    setHinweis(null)
    try {
      const gespeichert = await apiFetch<GeraeteSeite>(`/api/steuerung/geraete/${modul.modul}`, {
        method: 'PUT',
        body: JSON.stringify({ zuordnungen: entwurf }),
      })
      setSeite(gespeichert)
      setAenderungen((current) => ({ ...current, [modul.modul]: {} }))
      setFeldFehler({})
      setFehler(null)
      setHinweis('Gespeichert.')
    } catch (caught) {
      const felder = caught instanceof ApiRequestError ? caught.payload?.fieldErrors : undefined
      if (felder) {
        setFeldFehler(Object.fromEntries(Object.entries(felder).map(([feld, texte]) => [feld, texte.join(' ')])))
        setFehler('Bitte die markierten Zeilen prüfen.')
      } else {
        setFehler(formatApiError(caught, 'Das Speichern hat nicht geklappt.'))
      }
    } finally {
      setSpeichert(false)
    }
  }

  if (laedt) return <V1Page eyebrow="Steuerung" title="Geräte & Entitäten"><V1Skeleton /></V1Page>

  if (!seite || seite.module.length === 0) {
    return (
      <V1Page eyebrow="Steuerung" title="Geräte & Entitäten">
        <V1Empty title="Keine Steuerung mit Geräten" text="Sobald eine Regelung Geräte braucht, stehen sie hier." />
      </V1Page>
    )
  }

  return (
    <V1Page
      eyebrow="Steuerung"
      title="Geräte & Entitäten"
      subtitle="Einmal zuordnen — alle Steuerungen greifen darauf zu."
    >
      {fehler && <V1Alert tone="critical" message={fehler} />}
      {hinweis && <V1Alert tone="ok" message={hinweis} />}
      {!seite.haErreichbar && (
        <V1Alert
          tone="warn"
          message="Home Assistant antwortet gerade nicht — Vorschläge und Livewerte fehlen. Eintragen geht trotzdem."
        />
      )}

      <div className="st-wechsel" role="tablist" aria-label="Steuerung">
        {seite.module.map((eintrag) => {
          const luecken = eintrag.zeilen.filter((zeile) => zeile.pflicht && !zeile.gefunden).length
          return (
            <button
              key={eintrag.modul}
              type="button"
              role="tab"
              className="st-chip"
              aria-current={eintrag.modul === modul?.modul}
              onClick={() => setAktiv(eintrag.modul)}
            >
              <i className={luecken > 0 ? 'is-warn' : 'is-an'} />
              {eintrag.titel}
            </button>
          )
        })}
      </div>

      {modul && GRUPPEN.filter((gruppe) => modul.zeilen.some((zeile) => zeile.gruppe === gruppe.key)).map((gruppe) => {
        const zeilen = modul.zeilen.filter((zeile) => zeile.gruppe === gruppe.key)
        const gesetzt = zeilen.filter((zeile) => zeile.gefunden).length
        return (
          <V1Section key={gruppe.key} title={gruppe.label} action={<span className="st-nurlesen">{gesetzt} / {zeilen.length}</span>}>
            {zeilen.map((zeile) => (
              <RollenZeile
                key={zeile.rolle}
                zeile={zeile}
                wert={entwurf[zeile.rolle] ?? ''}
                fehler={feldFehler[zeile.rolle]}
                entities={entities}
                eigene={seite.eigene.map((g) => g.name)}
                onChange={(wert) => setzeRolle(zeile.rolle, wert)}
              />
            ))}
          </V1Section>
        )
      })}

      <div className="st-knopfleiste">
        <V1Button
          onClick={() => modul && setAenderungen((current) => ({
            ...current,
            [modul.modul]: Object.fromEntries(modul.zeilen.map((zeile) => [zeile.rolle, zeile.vorgabe])),
          }))}
        >
          Auf Vorgabe zurück
        </V1Button>
        <V1Button variant="primary" disabled={speichert} onClick={() => void speichern()}>
          {speichert ? 'Speichert…' : 'Speichern'}
        </V1Button>
      </div>

      {offen > 0 && (
        <p className="st-hinweis">
          {offen === 1 ? 'Eine Rolle findet ihr Gerät nicht' : `${offen} Rollen finden ihr Gerät nicht`} —
          die Regelung läuft dann ohne diesen Wert weiter.
        </p>
      )}

      <EigeneGeraete
        seite={seite}
        entities={entities}
        onSeite={(neu) => setSeite(neu)}
        onFehler={(text) => setFehler(text)}
      />

      <V1Card>
        <p className="st-hinweis">
          Die Regelung selbst läuft weiter in Home Assistant; sie kennt ihre Entitäten dort noch fest.
          Bis das umgestellt ist, wirkt eine Änderung hier auf Anzeige und Auswertung im Add-on.
        </p>
        <V1Button onClick={() => navigate('/steuerung/co2')}>Zur CO₂-Steuerung</V1Button>
      </V1Card>
    </V1Page>
  )
}

/** Eine Rolle: Suchfeld mit Vorschlägen, rechts der Livewert. */
function RollenZeile({
  zeile, wert, fehler, entities, eigene, onChange,
}: {
  zeile: GeraetZeile
  wert: string
  fehler?: string
  entities: HomeAssistantEntity[]
  eigene: string[]
  onChange: (wert: string) => void
}) {
  // Vorgefiltert auf die Domains der Rolle: ein sensor. in einer Schaltrolle
  // hilft niemandem. Findet der Filter nichts, steht die ganze Liste bereit.
  const vorschlaege = useMemo(() => {
    const passend = entities.filter((entity) => zeile.domains.includes(entity.domain))
    return passend.length > 0 ? passend : entities
  }, [entities, zeile.domains])

  return (
    <V1Field
      label={`${zeile.label}${zeile.einheit ? ` (${zeile.einheit})` : ''}`}
      hint={fehler ?? zeile.hinweis ?? undefined}
      wide
    >
      <div className="st-geraet">
        <V1Select
          label={zeile.label}
          titel={zeile.label}
          unterzeile={zeile.hinweis ?? undefined}
          wert={wert}
          platzhalter={zeile.vorgabe}
          onWahl={onChange}
          optionen={[
            { wert: '', text: '— wie ab Werk —', hinweis: zeile.vorgabe, betont: true },
            ...eigene.map((name): V1Option => ({
              wert: `@${name}`, text: name, gruppe: 'Eigene Geräte',
            })),
            ...vorschlaege.map((entity): V1Option => ({
              wert: entity.entityId,
              text: entity.friendlyName ?? entity.entityId,
              hinweis: `${entity.entityId}${entity.state ? ` · ${haWert(entity.state, entity.unitOfMeasurement)}` : ''}`,
              gruppe: 'Aus Home Assistant',
            })),
          ]}
        />
        <span className={zeile.gefunden ? 'st-geraet-wert is-ok' : 'st-geraet-wert is-faint'}>
          {zeile.gefunden ? (haWert(zeile.livewert, zeile.einheit) ?? 'gefunden') : wert.trim() === '' ? 'nicht zugeordnet' : 'unbekannt'}
        </span>
      </div>
    </V1Field>
  )
}

/** Frei benannte Geräte — von mehreren Steuerungen über `@Name` nutzbar. */
function EigeneGeraete({
  seite, entities, onSeite, onFehler,
}: {
  seite: GeraeteSeite
  entities: HomeAssistantEntity[]
  onSeite: (seite: GeraeteSeite) => void
  onFehler: (text: string | null) => void
}) {
  const [name, setName] = useState('')
  const [entityId, setEntityId] = useState('')
  const [offen, setOffen] = useState(false)

  async function anlegen() {
    try {
      const neu = await apiFetch<GeraeteSeite>('/api/steuerung/geraete/eigene', {
        method: 'POST',
        body: JSON.stringify({ name, entityId }),
      })
      onSeite(neu)
      onFehler(null)
      setName('')
      setEntityId('')
      setOffen(false)
    } catch (caught) {
      onFehler(formatApiError(caught, 'Das Gerät konnte nicht gespeichert werden.'))
    }
  }

  async function loeschen(geraet: string) {
    try {
      onSeite(await apiFetch<GeraeteSeite>(`/api/steuerung/geraete/eigene/${encodeURIComponent(geraet)}`, { method: 'DELETE' }))
      onFehler(null)
    } catch (caught) {
      onFehler(formatApiError(caught, 'Das Gerät konnte nicht gelöscht werden.'))
    }
  }

  return (
    <V1Section
      title="Eigene Geräte"
      action={<V1Button onClick={() => setOffen((current) => !current)}>{offen ? 'Abbrechen' : 'Gerät anlegen'}</V1Button>}
    >
      {seite.eigene.length === 0 && !offen && (
        <p className="st-hinweis">
          Noch keine. Hier stehen Geräte, die keiner Steuerung allein gehören — Zuluft, Außenluft-Fühler.
          Eine Rolle greift sie über <code>@Name</code> ab.
        </p>
      )}

      {seite.eigene.map((geraet) => (
        <div className="st-feldzeile" key={geraet.name}>
          <span className="st-etikett">
            {geraet.name}
            <small>{geraet.entityId}</small>
          </span>
          <span className="st-eingaben">
            <span className={geraet.gefunden ? 'st-geraet-wert is-ok' : 'st-geraet-wert is-faint'}>
              {geraet.gefunden ? (haWert(geraet.livewert) ?? 'gefunden') : 'unbekannt'}
            </span>
            <V1Button onClick={() => void loeschen(geraet.name)}>Entfernen</V1Button>
          </span>
        </div>
      ))}

      {offen && (
        <>
          <V1Field label="Name" wide>
            <input value={name} onChange={(event) => setName(event.target.value)} placeholder="Zuluft Zelt" />
          </V1Field>
          <V1Select
            label="Entität"
            titel="Entität wählen"
            unterzeile={name || undefined}
            wert={entityId}
            platzhalter="fan.big_zuluft"
            onWahl={setEntityId}
            optionen={entities.map((entity): V1Option => ({
              wert: entity.entityId,
              text: entity.friendlyName ?? entity.entityId,
              hinweis: entity.entityId,
            }))}
          />
          <V1Button variant="primary" onClick={() => void anlegen()}>Speichern</V1Button>
        </>
      )}
    </V1Section>
  )
}
