import { useEffect, useRef, useState } from 'react'
import { apiFetch } from '../api'
import { V1Alert, V1Button, V1Card, V1Field, V1Page, V1Section, V1Skeleton } from '../components/v1'
import { classNames } from '../utils'
import GiltGerade from '../features/setpoints/GiltGerade'
import '../features/setpoints/setpoints.css'

/**
 * Sollwert-Profile: die mitgelieferten und die eigenen.
 *
 * Ein eigenes Profil speichert NUR, was der Nutzer geändert hat. Alles andere
 * bleibt an der Wissensbasis hängen und wandert mit, wenn wir sie
 * aktualisieren — eine Vollkopie hätte ihn beim ersten Speichern von allen
 * künftigen Verbesserungen abgeschnitten, ohne dass er es merkt.
 */

type Profile = {
  id: string
  name: string
  baseProfileId: string
  isShipped: boolean
  changedValueCount: number
  stages: Array<{ stage: string; values: Record<string, number>; changed: string[] }>
}

const STAGES: Array<{ key: string; label: string }> = [
  { key: 'Seedling', label: 'Sämling' },
  { key: 'Clone', label: 'Steckling' },
  { key: 'Veg', label: 'Vegetativ' },
  { key: 'Transition', label: 'Transition' },
  { key: 'Flower', label: 'Blüte' },
  { key: 'Finish', label: 'Finish' },
]

/** Die Spalten der Tabelle: Überschrift und die zwei Felder dahinter. */
const COLUMNS: Array<{ label: string; min: string; max: string }> = [
  { label: 'pH', min: 'phMin', max: 'phMax' },
  { label: 'EC', min: 'ecMin', max: 'ecMax' },
  { label: 'ORP', min: 'orpMin', max: 'orpMax' },
  { label: 'H₂O °C', min: 'waterTempNightC', max: 'waterTempDayC' },
  { label: 'VPD', min: 'vpdMin', max: 'vpdMax' },
  { label: 'PPFD', min: 'ppfdMin', max: 'ppfdMax' },
  { label: 'CO₂', min: 'co2Min', max: 'co2Max' },
]

function zahl(value: number | undefined): string {
  return value == null ? '' : String(value).replace('.', ',')
}

function parse(value: string): number | null {
  const parsed = Number(value.replace(',', '.'))
  return Number.isFinite(parsed) ? parsed : null
}

/**
 * „6–6,2" statt zweier Zahlen nebeneinander.
 *
 * Beim Ansehen fehlen die Feldrahmen, die Min und Max sonst trennen — ohne
 * Gedankenstrich läse sich „6" neben „6,2" als eine einzige Zahl.
 */
function spanne(min: number | undefined, max: number | undefined): string {
  const unten = zahl(min)
  const oben = zahl(max)
  if (unten === '' && oben === '') return '—'
  if (unten === '') return `bis ${oben}`
  if (oben === '') return `ab ${unten}`
  return unten === oben ? unten : `${unten}–${oben}`
}

function SetpointProfilesPage() {
  const [profiles, setProfiles] = useState<Profile[]>([])
  const [openId, setOpenId] = useState<string | null>(null)
  const [draft, setDraft] = useState<Record<string, Record<string, string>>>({})
  const [draftName, setDraftName] = useState('')
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [refresh, setRefresh] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    async function laden() {
      try {
        const data = await apiFetch<Profile[]>('/api/setpoint-profiles', { signal: controller.signal })
        if (!controller.signal.aborted) setProfiles(data)
      } catch (caught) {
        if (!controller.signal.aborted) setError(caught instanceof Error ? caught.message : 'Profile konnten nicht geladen werden.')
      } finally {
        if (!controller.signal.aborted) setLoading(false)
      }
    }
    void laden()
    return () => controller.abort()
  }, [refresh])

  const offen = profiles.find((profile) => profile.id === openId) ?? null

  // Das Formular steht an anderer Stelle als der Knopf, der es oeffnet. Bei
  // wenigen Zeilen faellt das nicht auf, bei vielen schon: dann geht es
  // ausserhalb des Sichtbaren auf, nichts scrollt hin, und fuer den Nutzer
  // „reagiert der Knopf nicht". Genau so kam die Meldung zu /sensoren.
  //
  // `block: start`, nicht `center`: die Ueberschrift ist die Rueckmeldung,
  // dass der Klick angekommen ist.
  const offenRef = useRef<HTMLDivElement | null>(null)

  useEffect(() => {
    if (!offen) return
    offenRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' })
  }, [offen])

  /**
   * Ein mitgeliefertes Profil aufschlagen — nur lesen.
   *
   * Vorher gab es dafür gar keinen Weg: an den mitgelieferten Profilen stand
   * nur „Kopieren". Wer wissen wollte, was in RDWC oder DWC überhaupt drinsteht,
   * musste sich erst eine Kopie anlegen — also etwas verändern, um etwas
   * nachsehen zu können.
   */
  function ansehen(profile: Profile) {
    setOpenId(profile.id)
    setDraftName(profile.name)
    setDraft({})
    setError(null)
    setMessage(null)
  }

  function bearbeiten(profile: Profile) {
    // Der Entwurf startet leer: eingetragen wird nur, was der Nutzer anfasst.
    setOpenId(profile.id)
    setDraftName(profile.name)
    const vorhanden: Record<string, Record<string, string>> = {}
    for (const eintrag of profile.stages) {
      if (eintrag.changed.length === 0) continue
      vorhanden[eintrag.stage] = {}
      for (const feld of eintrag.changed) vorhanden[eintrag.stage][feld] = zahl(eintrag.values[feld])
    }
    setDraft(vorhanden)
    setError(null)
    setMessage(null)
  }

  async function anlegen(basis: Profile) {
    setSaving(true)
    setError(null)
    try {
      const created = await apiFetch<Profile>('/api/setpoint-profiles', {
        method: 'POST',
        body: JSON.stringify({
          name: `Meine ${basis.name}`,
          baseProfileId: basis.isShipped ? basis.id : basis.baseProfileId,
          overrides: {},
        }),
      })
      setMessage(`„${created.name}" angelegt — jetzt deine Werte eintragen.`)
      setRefresh((value) => value + 1)
      setOpenId(created.id)
      setDraftName(created.name)
      setDraft({})
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Anlegen fehlgeschlagen.')
    } finally {
      setSaving(false)
    }
  }

  function aendern(stage: string, feld: string, wert: string) {
    setDraft((current) => {
      const naechste = { ...current, [stage]: { ...(current[stage] ?? {}) } }
      if (wert.trim() === '') delete naechste[stage][feld]
      else naechste[stage][feld] = wert
      if (Object.keys(naechste[stage]).length === 0) delete naechste[stage]
      return naechste
    })
  }

  async function speichern() {
    if (!offen) return
    const id = offen.id.replace('custom:', '')
    const overrides: Record<string, Record<string, number>> = {}
    for (const [stage, felder] of Object.entries(draft)) {
      for (const [feld, wert] of Object.entries(felder)) {
        const zahlWert = parse(wert)
        if (zahlWert == null) continue
        overrides[stage] ??= {}
        overrides[stage][feld] = zahlWert
      }
    }

    setSaving(true)
    setError(null)
    try {
      await apiFetch(`/api/setpoint-profiles/${id}`, {
        method: 'PUT',
        body: JSON.stringify({ name: draftName.trim(), baseProfileId: offen.baseProfileId, overrides }),
      })
      setMessage('Gespeichert.')
      setRefresh((value) => value + 1)
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Speichern fehlgeschlagen.')
    } finally {
      setSaving(false)
    }
  }

  async function loeschen(profile: Profile) {
    setSaving(true)
    try {
      await apiFetch(`/api/setpoint-profiles/${profile.id.replace('custom:', '')}`, { method: 'DELETE' })
      setOpenId(null)
      setMessage(`„${profile.name}" entfernt. Grows und Systeme, die darauf zeigten, folgen wieder ihrem Standard.`)
      setRefresh((value) => value + 1)
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Löschen fehlgeschlagen.')
    } finally {
      setSaving(false)
    }
  }

  if (loading) return <V1Skeleton rows={6} label="Lade Profile" />

  return (
    <V1Page
      eyebrow="Betrieb"
      title="Sollwert-Profile"
      subtitle="Was Grow OS als Ziel ansieht — je Phase. Die mitgelieferten kannst du kopieren und mit deinen Erfahrungswerten überschreiben."
    >
      {error && <V1Alert message={error} tone="critical" />}
      {message && <V1Alert message={message} tone="ok" />}

      {/* Fork AI: erst was gerade gilt, dann die Profile. Die Tabelle darunter
          zeigt Phasenwerte — ohne diesen Kopf liest man sie als das Ergebnis,
          obwohl Feed-Chart und eigene Grenzwerte noch darüber liegen. */}
      <GiltGerade />

      <V1Section title="Profile">
        <div className="sp-list">
          {profiles.map((profile) => (
            <article
              key={profile.id}
              className={classNames('sp-card', !profile.isShipped && 'is-mine', profile.id === openId && 'is-open')}
              data-audit={`setpoint-profile-${profile.id}`}
            >
              <div className="sp-card-top">
                <span className="sp-name">{profile.name}</span>
                <span className={classNames('sp-tag', profile.isShipped ? 'is-shipped' : 'is-mine')}>
                  {profile.isShipped ? 'mitgeliefert' : 'meins'}
                </span>
              </div>
              <div className="sp-facts">
                {profile.isShipped
                  ? 'Wird mit Updates gepflegt.'
                  : `Kopie von ${profiles.find((p) => p.id === profile.baseProfileId)?.name ?? profile.baseProfileId} · ${profile.changedValueCount} Werte geändert`}
              </div>
              <div className="sp-actions">
                {profile.isShipped ? (
                  <>
                    <V1Button onClick={() => ansehen(profile)}>Ansehen</V1Button>
                    <V1Button onClick={() => void anlegen(profile)} disabled={saving}>Kopieren</V1Button>
                  </>
                ) : (
                  <>
                    <V1Button onClick={() => bearbeiten(profile)}>Bearbeiten</V1Button>
                    <V1Button variant="danger" onClick={() => void loeschen(profile)} disabled={saving}>Löschen</V1Button>
                  </>
                )}
              </div>
            </article>
          ))}
        </div>
      </V1Section>

      {offen && (
      <div ref={offenRef} className="scroll-ziel" data-audit="profil-panel">
        <V1Section
          title={`${offen.isShipped ? 'Ansehen' : 'Bearbeiten'} · ${offen.name}`}
          action={
            offen.isShipped ? (
              <>
                <V1Button variant="primary" onClick={() => void anlegen(offen)} disabled={saving}>
                  Kopieren und anpassen
                </V1Button>
                <V1Button onClick={() => setOpenId(null)}>Schließen</V1Button>
              </>
            ) : (
              <>
                <V1Button variant="primary" onClick={() => void speichern()} disabled={saving}>
                  {saving ? 'Speichert…' : 'Speichern'}
                </V1Button>
                <V1Button onClick={() => setOpenId(null)}>Schließen</V1Button>
              </>
            )
          }
        >
          <V1Card>
            {offen.isShipped ? (
              <p className="sp-hint">
                Das sind unsere Werte, gepflegt mit den Updates. Ändern lässt sich hier nichts —
                für eigene Erfahrungswerte legst du dir mit „Kopieren und anpassen" eine eigene
                Fassung an. Darin gehört dir nur, was du wirklich einträgst; alles andere wandert
                weiter mit unseren Updates mit.
              </p>
            ) : (
              <>
                <V1Field label="Name">
                  <input value={draftName} onChange={(event) => setDraftName(event.target.value)} />
                </V1Field>
                <p className="sp-hint">
                  Leer lassen heißt „wie mitgeliefert". Nur was du einträgst, gehört dir — alles andere bekommt
                  weiterhin unsere Updates.
                </p>
              </>
            )}
          </V1Card>

          <div className="co-table-wrap">
            <div className="sp-table">
              <div className="sp-th">Phase</div>
              {COLUMNS.map((column) => <div key={column.label} className="sp-th">{column.label}</div>)}

              {STAGES.map((stage) => (
                <StageRow key={stage.key}>
                  <div className="sp-td is-name">{stage.label}</div>
                  {COLUMNS.map((column) => {
                    const basis = offen.stages.find((eintrag) => eintrag.stage === stage.key)?.values ?? {}
                    return (
                      <div key={column.label} className="sp-td">
                        {/* Beim Ansehen keine Eingabefelder: ein Feld, in das man
                            tippen kann und das nichts speichert, ist schlimmer als
                            gar keins. Und ohne Feldrahmen braucht die Spanne einen
                            Trenner, sonst liest sich „6" neben „6,2" als eine Zahl. */}
                        {offen.isShipped ? (
                          <span className="sp-static">
                            {spanne(basis[column.min], basis[column.max])}
                          </span>
                        ) : (
                          <div className="sp-pair">
                            {[column.min, column.max].map((feld) => {
                              const eigen = draft[stage.key]?.[feld]
                              const grenze = feld.endsWith('Max') || feld === 'waterTempDayC' ? 'oben' : 'unten'
                              return (
                                <input
                                  key={feld}
                                  className={classNames('sp-input', eigen != null && 'is-changed')}
                                  inputMode="decimal"
                                  value={eigen ?? ''}
                                  placeholder={zahl(basis[feld])}
                                  onChange={(event) => aendern(stage.key, feld, event.target.value)}
                                  aria-label={`${stage.label} ${column.label} ${grenze}`}
                                />
                              )
                            })}
                          </div>
                        )}
                      </div>
                    )
                  })}
                </StageRow>
              ))}
            </div>
          </div>
          <p className="sp-hint">
            Graue Zahlen sind die mitgelieferten Werte. Was du eintippst, wird blau — das sind deine.
          </p>
        </V1Section>
      </div>
      )}
    </V1Page>
  )
}

/** Nur ein Fragment — die Zellen müssen direkte Grid-Kinder bleiben. */
function StageRow({ children }: { children: React.ReactNode }) {
  return <>{children}</>
}

export default SetpointProfilesPage
