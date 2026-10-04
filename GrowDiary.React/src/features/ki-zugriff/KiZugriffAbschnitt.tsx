import { useEffect, useId, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { apiFetch, formatApiError } from '../../api'
import { V1Alert, V1Badge, V1Empty, V1Field, V1Skeleton, V1Switch } from '../../components/v1'
import { KI_STUFEN, kiStufeName } from '../../deutsche-woerter'
import type {
  KiSchluesselAngelegtDto, KiSchluesselDto, KiSchluesselRequest, KiZugriffSeiteDto,
  KiZugriffSpeichernRequest,
} from '../../types'
import { formatDate, formatDateTime } from '../../utils'
import {
  FELD, STUFEN_ERKLAERUNG, STUFEN_WARNUNG, ZUSTAENDE, anfrageAus, feldFehlerJeFeld, hoechstwerteEntwurf,
  hoechstwerteLesen, istRiskant, praefixAnzeige, sammelmeldung, stufenSchilder, stufenWahl,
  warnungAblehnen, warnungBestaetigen, zustandWaehlen, type HoechstwerteEntwurf, type StufenWahl,
} from './ki-zugriff-logik'
import KiProtokoll from './KiProtokoll'
import { insBildHolen, rollArt } from './ins-bild'
import { altesKopieren } from './kopieren'
import './ki-zugriff.css'

/**
 * KI-Assistent → Reiter „Zugriff & Schlüssel" (bis forkai.164 in den
 * Einstellungen; A-003, Bauplan
 * `docs/ki-zugriff.md`).
 *
 * Lädt sich selbst über `/api/settings/ki-zugriff` — schlägt der Rest der
 * Einstellungsseite fehl, bleibt dieser Abschnitt bedienbar und umgekehrt.
 *
 * Drei Formulare: die Einstellungen (Hauptschalter, Höchstwerte), „Neuer
 * Schlüssel" und „Stufen ändern" je Schlüssel — dort je Stufe Gesperrt · Mit
 * Rückfrage · Frei (Fork AI, A-005, 03.10.2026). Der Klartext eines neuen
 * Schlüssels lebt NUR im Zustand dieses Bauteils — nicht im Speicher des
 * Browsers, nicht in der Liste. Wer die Seite verlässt, sieht ihn nie wieder.
 */
export default function KiZugriffAbschnitt() {
  const [laedt, setLaedt] = useState(true)
  const [ladeFehler, setLadeFehler] = useState<string | null>(null)
  const [gespeichertAktiv, setGespeichertAktiv] = useState(false)

  // --- Einstellungen ---
  const [aktiv, setAktiv] = useState(false)
  const [hoechst, setHoechst] = useState<HoechstwerteEntwurf>({ maxDosisMl: '', maxBefehleJeStunde: '' })
  const [speichert, setSpeichert] = useState(false)
  const [einstFehler, setEinstFehler] = useState<string | null>(null)
  const [einstFelder, setEinstFelder] = useState<Record<string, string>>({})
  const [einstMeldung, setEinstMeldung] = useState<string | null>(null)

  // --- Schlüssel ---
  const [schluessel, setSchluessel] = useState<KiSchluesselDto[]>([])
  const [listenFehler, setListenFehler] = useState<string | null>(null)
  const [listenMeldung, setListenMeldung] = useState<string | null>(null)
  const [neuOffen, setNeuOffen] = useState(false)
  const [neuName, setNeuName] = useState('')
  const [neuWahl, setNeuWahl] = useState<StufenWahl>(() => stufenWahl())
  const [neuFehler, setNeuFehler] = useState<string | null>(null)
  const [neuFelder, setNeuFelder] = useState<Record<string, string>>({})
  const [legtAn, setLegtAn] = useState(false)
  const [angelegt, setAngelegt] = useState<{ id: number; name: string; klartext: string } | null>(null)
  const [bearbeitet, setBearbeitet] = useState<{ id: number; wahl: StufenWahl } | null>(null)
  const [bearbeitFehler, setBearbeitFehler] = useState<string | null>(null)
  const [aendert, setAendert] = useState(false)

  // --- Was die KI zuletzt getan hat (Fork AI, A-003, 03.10.2026) ---
  const [protokollFilter, setProtokollFilter] = useState<number | null>(null)
  const gefilterterSchluessel = schluessel.find((s) => s.id === protokollFilter) ?? null

  function nurDiesenZeigen(id: number) {
    setProtokollFilter((bisher) => (bisher === id ? null : id))
    // Auf dem Telefon liegt die Liste unter allen Schlüsseln — dorthin, wo sich etwas ändert.
    document.getElementById('ki-protokoll')?.scrollIntoView?.({ block: 'start', behavior: rollArt() })
  }

  function uebernehmen(seite: KiZugriffSeiteDto) {
    setGespeichertAktiv(seite.aktiv)
    setAktiv(seite.aktiv)
    setHoechst(hoechstwerteEntwurf(seite.hoechstwerte))
    setSchluessel(seite.schluessel)
  }

  useEffect(() => {
    const abbruch = new AbortController()
    async function laden() {
      try {
        const seite = await apiFetch<KiZugriffSeiteDto>('/api/settings/ki-zugriff', { signal: abbruch.signal })
        if (abbruch.signal.aborted) return
        uebernehmen(seite)
        setLadeFehler(null)
      } catch (caught) {
        if (!abbruch.signal.aborted) setLadeFehler(formatApiError(caught, 'Der Zugriff für KI-Assistenten konnte nicht geladen werden.'))
      } finally {
        if (!abbruch.signal.aborted) setLaedt(false)
      }
    }
    void laden()
    return () => abbruch.abort()
  }, [])

  async function einstellungenSpeichern(event: FormEvent) {
    event.preventDefault()
    setEinstMeldung(null)
    const gelesen = hoechstwerteLesen(hoechst)
    if (!gelesen.ok) {
      setEinstFelder(gelesen.felder)
      setEinstFehler(gelesen.meldung)
      return
    }
    setSpeichert(true)
    setEinstFehler(null)
    setEinstFelder({})
    try {
      const rumpf: KiZugriffSpeichernRequest = { aktiv, hoechstwerte: gelesen.werte }
      const seite = await apiFetch<KiZugriffSeiteDto>('/api/settings/ki-zugriff', { method: 'PUT', body: JSON.stringify(rumpf) })
      uebernehmen(seite)
      setEinstMeldung(seite.aktiv
        ? 'Gespeichert. Der Zugriff ist an — Schlüssel werden angenommen.'
        : 'Gespeichert. Der Zugriff ist aus — jeder Schlüssel wird abgewiesen.')
    } catch (caught) {
      setEinstFelder(feldFehlerJeFeld(caught))
      setEinstFehler(sammelmeldung(caught, [FELD.maxDosis, FELD.maxBefehle], 'Speichern fehlgeschlagen.'))
    } finally {
      setSpeichert(false)
    }
  }

  function neuOeffnen() {
    setNeuOffen(true)
    setNeuName('')
    setNeuWahl(stufenWahl())
    setNeuFehler(null)
    setNeuFelder({})
  }

  async function schluesselAnlegen(event: FormEvent) {
    event.preventDefault()
    setListenMeldung(null)
    const name = neuName.trim()
    const felder: Record<string, string> = {}
    const auswahl = anfrageAus(neuWahl)
    if (name === '') felder[FELD.name] = 'Bitte einen Namen eintragen, z. B. „Claude am Telefon".'
    if (auswahl.stufen.length === 0) felder[FELD.stufen] = 'Mindestens eine Stufe freigeben — mit Rückfrage oder frei.'
    if (neuWahl.offeneWarnung) felder[FELD.stufen] = 'Bitte erst den Warnhinweis beantworten.'
    if (Object.keys(felder).length > 0) {
      setNeuFelder(felder)
      setNeuFehler('Bitte die markierten Felder prüfen.')
      return
    }
    setLegtAn(true)
    setNeuFehler(null)
    setNeuFelder({})
    try {
      const rumpf: KiSchluesselRequest = { name, ...auswahl }
      const neu = await apiFetch<KiSchluesselAngelegtDto>('/api/settings/ki-zugriff/schluessel', { method: 'POST', body: JSON.stringify(rumpf) })
      setSchluessel((liste) => [...liste.filter((s) => s.id !== neu.schluessel.id), neu.schluessel])
      setAngelegt({ id: neu.schluessel.id, name: neu.schluessel.name, klartext: neu.klartext })
      setNeuOffen(false)
    } catch (caught) {
      setNeuFelder(feldFehlerJeFeld(caught))
      setNeuFehler(sammelmeldung(caught, [FELD.name, FELD.stufen, FELD.rueckfrage], 'Der Schlüssel konnte nicht angelegt werden.'))
    } finally {
      setLegtAn(false)
    }
  }

  function ersetzen(neu: KiSchluesselDto) {
    setSchluessel((liste) => liste.map((s) => (s.id === neu.id ? neu : s)))
  }

  async function stufenSpeichern(event: FormEvent) {
    event.preventDefault()
    if (!bearbeitet) return
    const alt = schluessel.find((s) => s.id === bearbeitet.id)
    if (!alt) return
    const auswahl = anfrageAus(bearbeitet.wahl)
    if (bearbeitet.wahl.offeneWarnung) { setBearbeitFehler('Bitte erst den Warnhinweis beantworten.'); return }
    if (auswahl.stufen.length === 0) { setBearbeitFehler('Mindestens eine Stufe freigeben — sonst lieber den Schlüssel sperren.'); return }
    setAendert(true)
    setBearbeitFehler(null)
    try {
      const rumpf: KiSchluesselRequest = { name: alt.name, ...auswahl }
      const neu = await apiFetch<KiSchluesselDto>(`/api/settings/ki-zugriff/schluessel/${alt.id}`, { method: 'PUT', body: JSON.stringify(rumpf) })
      ersetzen(neu)
      setBearbeitet(null)
      setListenMeldung(`Stufen von „${neu.name}" geändert.`)
    } catch (caught) {
      setBearbeitFehler(formatApiError(caught, 'Die Stufen konnten nicht geändert werden.'))
    } finally {
      setAendert(false)
    }
  }

  async function sperren(eintrag: KiSchluesselDto) {
    if (!window.confirm(`Schlüssel „${eintrag.name}" sperren? Er wird ab sofort abgewiesen. Entsperren geht nicht — für neuen Zugang einen neuen Schlüssel anlegen.`)) return
    setListenFehler(null)
    setListenMeldung(null)
    try {
      const neu = await apiFetch<KiSchluesselDto>(`/api/settings/ki-zugriff/schluessel/${eintrag.id}/sperren`, { method: 'POST' })
      ersetzen(neu)
      if (bearbeitet?.id === eintrag.id) setBearbeitet(null)
      setListenMeldung(`„${neu.name}" ist gesperrt.`)
    } catch (caught) {
      setListenFehler(formatApiError(caught, 'Der Schlüssel konnte nicht gesperrt werden.'))
    }
  }

  async function loeschen(eintrag: KiSchluesselDto) {
    if (!window.confirm(`Schlüssel „${eintrag.name}" endgültig löschen? Ein Assistent, der ihn benutzt, wird ab sofort abgewiesen.`)) return
    setListenFehler(null)
    setListenMeldung(null)
    try {
      await apiFetch<void>(`/api/settings/ki-zugriff/schluessel/${eintrag.id}`, { method: 'DELETE' })
      setSchluessel((liste) => liste.filter((s) => s.id !== eintrag.id))
      if (angelegt?.id === eintrag.id) setAngelegt(null)
      if (bearbeitet?.id === eintrag.id) setBearbeitet(null)
      if (protokollFilter === eintrag.id) setProtokollFilter(null)
      setListenMeldung(`„${eintrag.name}" ist gelöscht.`)
    } catch (caught) {
      setListenFehler(formatApiError(caught, 'Der Schlüssel konnte nicht gelöscht werden.'))
    }
  }

  return (
    <section className="ls-panel ki-zugriff" data-audit="settings-ki-zugriff" aria-labelledby="ki-zugriff-titel">
      <div className="ls-panel-head">
        <span className="ls-label" id="ki-zugriff-titel">Schlüssel &amp; Freigaben</span>
        {!laedt && !ladeFehler && <V1Badge tone={gespeichertAktiv ? 'warn' : 'neutral'}>{gespeichertAktiv ? 'an' : 'aus'}</V1Badge>}
      </div>

      <div className="ki-inhalt">
        <p className="ki-erklaerung">
          Mit einem Schlüssel kann ein KI-Assistent wie Claude selbst eintragen, was du ihm diktierst — etwa eine Messung
          oder eine Notiz. Ab Werk ist das aus, und jeder Schlüssel kann nur, was du bei ihm freigibst.
        </p>

        {laedt ? <V1Skeleton rows={3} label="Lade Zugriff für KI-Assistenten" /> : ladeFehler ? (
          <V1Alert title="Fehler" message={ladeFehler} tone="warn" />
        ) : (
          <>
            <form className="ki-block" onSubmit={(event) => void einstellungenSpeichern(event)} data-audit="ki-zugriff-form" noValidate>
              <V1Switch
                label="Zugriff erlauben"
                checked={aktiv}
                onChange={setAktiv}
                hint="Ohne Haken wird jeder Schlüssel abgewiesen, auch ein gültiger."
              />

              <div className="ki-felder">
                <V1Field label="Höchstens ml je Dosierbefehl" hint="Die Grenze der Pumpe gilt zusätzlich.">
                  <input
                    inputMode="decimal"
                    value={hoechst.maxDosisMl}
                    aria-label="Höchstens ml je Dosierbefehl"
                    aria-invalid={einstFelder[FELD.maxDosis] ? true : undefined}
                    onChange={(event) => setHoechst((h) => ({ ...h, maxDosisMl: event.target.value }))}
                    placeholder="10"
                  />
                  {einstFelder[FELD.maxDosis] && <span className="ki-fehler">{einstFelder[FELD.maxDosis]}</span>}
                </V1Field>

                <V1Field label="Höchstens Befehle je Stunde" hint="Schalt- und Dosierbefehle, über alle Schlüssel zusammen.">
                  <input
                    inputMode="numeric"
                    value={hoechst.maxBefehleJeStunde}
                    aria-label="Höchstens Befehle je Stunde"
                    aria-invalid={einstFelder[FELD.maxBefehle] ? true : undefined}
                    onChange={(event) => setHoechst((h) => ({ ...h, maxBefehleJeStunde: event.target.value }))}
                    placeholder="20"
                  />
                  {einstFelder[FELD.maxBefehle] && <span className="ki-fehler">{einstFelder[FELD.maxBefehle]}</span>}
                </V1Field>
              </div>

              {einstFehler && <V1Alert message={einstFehler} tone="warn" />}
              {einstMeldung && <V1Alert message={einstMeldung} tone="ok" />}

              <div className="ki-knoepfe">
                <button type="submit" className="ls-btn is-primary" disabled={speichert} data-audit="ki-zugriff-speichern">
                  {speichert ? 'Speichert…' : 'Speichern'}
                </button>
              </div>
            </form>

            <div className="ki-block" data-audit="ki-schluessel-liste">
              <div className="ki-block-kopf">
                <h3>Schlüssel</h3>
                {!neuOffen && (
                  <button type="button" className="ls-btn is-small" onClick={neuOeffnen} data-audit="ki-schluessel-neu">Neuer Schlüssel</button>
                )}
              </div>

              {angelegt && (
                // key: ein zweiter Schlüssel ist ein neuer Kasten — sonst stünde dort noch „Kopiert.“ vom ersten.
                <KlartextAnzeige key={angelegt.id} name={angelegt.name} klartext={angelegt.klartext} onAusblenden={() => setAngelegt(null)} />
              )}

              {neuOffen && (
                <form className="ki-unterformular" onSubmit={(event) => void schluesselAnlegen(event)} data-audit="ki-schluessel-form" noValidate>
                  <V1Field label="Name" hint="Woran du den Schlüssel später erkennst.">
                    <input
                      value={neuName}
                      maxLength={80}
                      aria-label="Name des Schlüssels"
                      aria-invalid={neuFelder[FELD.name] ? true : undefined}
                      onChange={(event) => setNeuName(event.target.value)}
                      placeholder="z. B. Claude am Telefon"
                    />
                    {neuFelder[FELD.name] && <span className="ki-fehler">{neuFelder[FELD.name]}</span>}
                  </V1Field>
                  <StufenAuswahl wahl={neuWahl} onWahl={setNeuWahl} fehler={neuFelder[FELD.stufen] ?? neuFelder[FELD.rueckfrage]} />
                  {neuFehler && <V1Alert message={neuFehler} tone="warn" />}
                  <div className="ki-knoepfe">
                    <button type="submit" className="ls-btn is-primary" disabled={legtAn} data-audit="ki-schluessel-anlegen">
                      {legtAn ? 'Legt an…' : 'Schlüssel anlegen'}
                    </button>
                    <button type="button" className="ls-btn" onClick={() => setNeuOffen(false)}>Abbrechen</button>
                  </div>
                </form>
              )}

              {listenFehler && <V1Alert message={listenFehler} tone="warn" />}
              {listenMeldung && <V1Alert message={listenMeldung} tone="ok" />}

              {schluessel.length === 0 ? (
                <V1Empty title="Noch kein Schlüssel" text="Ohne Schlüssel kann kein Assistent etwas eintragen." />
              ) : (
                <ul className="ki-liste">
                  {schluessel.map((eintrag) => (
                    <SchluesselZeile
                      key={eintrag.id}
                      eintrag={eintrag}
                      bearbeitet={bearbeitet?.id === eintrag.id}
                      onStufenAendern={() => { setBearbeitFehler(null); setBearbeitet({ id: eintrag.id, wahl: stufenWahl(eintrag.stufen, eintrag.rueckfrageBei ?? []) }) }}
                      onSperren={() => void sperren(eintrag)}
                      onLoeschen={() => void loeschen(eintrag)}
                      nurDieser={protokollFilter === eintrag.id}
                      onNurDiesenZeigen={() => nurDiesenZeigen(eintrag.id)}
                    >
                      {bearbeitet?.id === eintrag.id && (
                        <form className="ki-unterformular" onSubmit={(event) => void stufenSpeichern(event)} data-audit="ki-stufen-form" noValidate>
                          <StufenAuswahl wahl={bearbeitet.wahl} onWahl={(wahl) => setBearbeitet({ id: eintrag.id, wahl })} />
                          {bearbeitFehler && <V1Alert message={bearbeitFehler} tone="warn" />}
                          <div className="ki-knoepfe">
                            <button type="submit" className="ls-btn is-primary" disabled={aendert} data-audit="ki-stufen-speichern">
                              {aendert ? 'Speichert…' : 'Stufen speichern'}
                            </button>
                            <button type="button" className="ls-btn" onClick={() => setBearbeitet(null)}>Abbrechen</button>
                          </div>
                        </form>
                      )}
                    </SchluesselZeile>
                  ))}
                </ul>
              )}
            </div>

            <KiProtokoll
              key={gefilterterSchluessel?.id ?? 'alle'}
              filter={gefilterterSchluessel ? { id: gefilterterSchluessel.id, name: gefilterterSchluessel.name } : null}
              onFilter={setProtokollFilter}
            />
          </>
        )}
      </div>
    </section>
  )
}

/**
 * Je Stufe eine Zeile: Name, kurze Beschreibung und ein dreiteiliger
 * Umschalter Gesperrt · Mit Rückfrage · Frei (Fork AI, A-005, 03.10.2026).
 *
 * Der Umschalter sind echte Radioknöpfe mit gemeinsamem Namen je Stufe: eine
 * Tabulator-Station je Stufe, die Pfeiltasten wechseln den Zustand, und der
 * Bildschirmleser liest „Grow planen, Optionsgruppe, Mit Rückfrage".
 *
 * Riskante Stufen (Geräte schalten, Verwaltung) wechseln von Gesperrt erst,
 * wenn der Warnhinweis darunter bestätigt ist — die Regel steht in
 * `zustandWaehlen`, hier wird sie nur gezeigt.
 */
export function StufenAuswahl({ wahl, onWahl, fehler }: { wahl: StufenWahl; onWahl: (wahl: StufenWahl) => void; fehler?: string }) {
  const id = useId()
  return (
    <fieldset className="ki-stufen" aria-invalid={fehler ? true : undefined}>
      <legend>Stufen</legend>
      {KI_STUFEN.map((stufe) => {
        const titel = `${id}-${stufe}-titel`
        const erklaerung = `${id}-${stufe}-text`
        const offen = wahl.offeneWarnung?.stufe === stufe ? wahl.offeneWarnung : null
        return (
          <div key={stufe} className="ki-stufe">
            <div className="ki-stufe-zeile">
              <div className="ki-stufe-text">
                <strong id={titel}>{kiStufeName(stufe)}</strong>
                <small id={erklaerung}>{STUFEN_ERKLAERUNG[stufe]}{istRiskant(stufe) ? ' — freigeben nur nach Warnhinweis' : ''}</small>
              </div>
              <div className="ki-dreier" role="radiogroup" aria-labelledby={titel} aria-describedby={erklaerung} data-audit="ki-stufe-umschalter" data-stufe={stufe}>
                {ZUSTAENDE.map(({ wert, text }) => (
                  <label key={wert} className={`ki-dreier-wahl is-${wert}`}>
                    <input
                      type="radio"
                      name={`${id}-${stufe}`}
                      value={wert}
                      checked={wahl.zustaende[stufe] === wert}
                      data-stufe={stufe}
                      data-zustand={wert}
                      onChange={() => onWahl(zustandWaehlen(wahl, stufe, wert))}
                    />
                    <span>{text}</span>
                  </label>
                ))}
              </div>
            </div>
            {offen && (
              <div className="ki-warnung" role="alert" data-audit="ki-stufen-warnung">
                <strong>Achtung: {kiStufeName(stufe)} auf „{ZUSTAENDE.find((z) => z.wert === offen.ziel)?.text}"</strong>
                <p>{STUFEN_WARNUNG[stufe]}</p>
                <div className="ki-knoepfe">
                  <button type="button" className="ls-btn is-small ki-warnung-ja" onClick={() => onWahl(warnungBestaetigen(wahl))} data-audit="ki-warnung-bestaetigen">
                    Freigeben
                  </button>
                  <button type="button" className="ls-btn is-small" onClick={() => onWahl(warnungAblehnen(wahl))} data-audit="ki-warnung-ablehnen">
                    Gesperrt lassen
                  </button>
                </div>
              </div>
            )}
          </div>
        )
      })}
      {fehler && <span className="ki-fehler">{fehler}</span>}
    </fieldset>
  )
}

/** Eine Zeile der Schlüsselliste — zeigt nie mehr als den Anfang des Schlüssels. */
export function SchluesselZeile({ eintrag, bearbeitet, onStufenAendern, onSperren, onLoeschen, nurDieser = false, onNurDiesenZeigen, children }: {
  eintrag: KiSchluesselDto
  bearbeitet: boolean
  onStufenAendern: () => void
  onSperren: () => void
  onLoeschen: () => void
  /** Fork AI (A-003, 03.10.2026): „Was die KI zuletzt getan hat" zeigt gerade nur diesen Schlüssel. */
  nurDieser?: boolean
  onNurDiesenZeigen?: () => void
  children?: ReactNode
}) {
  const gesperrt = eintrag.gesperrtAmUtc != null
  return (
    <li className={`ki-schluessel${gesperrt ? ' is-gesperrt' : ''}`} data-audit="ki-schluessel">
      <div className="ki-schluessel-kopf">
        <strong className="ki-schluessel-name">{eintrag.name}</strong>
        {gesperrt && <V1Badge tone="critical">gesperrt</V1Badge>}
      </div>
      <code className="ki-praefix">{praefixAnzeige(eintrag.praefix)}</code>
      {/* Fork AI (A-005): je freigegebener Stufe „Name · frei" bzw. „Name · mit Rückfrage"; gesperrte fehlen. */}
      <div className="ki-stufen-marken" role="group" aria-label="Freigegebene Stufen">
        {eintrag.stufen.length === 0
          ? <span className="ki-zeit">keine Stufe — nur lesen</span>
          : stufenSchilder(eintrag.stufen, eintrag.rueckfrageBei ?? []).map(({ stufe, zustand, text }) => (
            <V1Badge key={stufe} tone={istRiskant(stufe) ? 'warn' : 'neutral'}>
              <span data-stufe={stufe} data-zustand={zustand}>{text}</span>
            </V1Badge>
          ))}
      </div>
      <div className="ki-zeit">
        erstellt {formatDate(eintrag.erstelltAmUtc)} · zuletzt genutzt {eintrag.zuletztGenutztAmUtc ? formatDateTime(eintrag.zuletztGenutztAmUtc) : 'noch nie'}
        {gesperrt && <> · gesperrt {formatDateTime(eintrag.gesperrtAmUtc)}</>}
      </div>
      {!bearbeitet && (
        <div className="ki-knoepfe">
          {!gesperrt && <button type="button" className="ls-btn is-small" onClick={onStufenAendern} data-audit="ki-schluessel-stufen-aendern">Stufen ändern</button>}
          {!gesperrt && <button type="button" className="ls-btn is-small" onClick={onSperren} data-audit="ki-schluessel-sperren">Sperren</button>}
          <button type="button" className="ls-btn is-small is-gefahr" onClick={onLoeschen} data-audit="ki-schluessel-loeschen">Löschen</button>
          {onNurDiesenZeigen && (
            <button type="button" className="ls-btn is-small" aria-pressed={nurDieser} onClick={onNurDiesenZeigen} data-audit="ki-schluessel-nur-diesen">
              {nurDieser ? 'Alle zeigen' : 'Nur diesen zeigen'}
            </button>
          )}
        </div>
      )}
      {children}
    </li>
  )
}

/**
 * Der Klartext eines eben angelegten Schlüssels — der einzige Ort, an dem er
 * je zu sehen ist.
 */
export function KlartextAnzeige({ name, klartext, onAusblenden }: { name: string; klartext: string; onAusblenden: () => void }) {
  const [kopiert, setKopiert] = useState<'ja' | 'nein' | null>(null)
  const kasten = useRef<HTMLDivElement>(null)
  const titel = useId()
  const satz = useId()

  // Am Handy liegt der Kasten nach dem Anlegen über der Bildkante (Fork AI,
  // A-003, 04.10.2026) — bei jedem neuen Klartext hinrollen und den Fokus
  // hineinsetzen, auch wenn der vorige Kasten noch offen war.
  useEffect(() => {
    insBildHolen(kasten.current)
  }, [klartext])

  async function kopieren() {
    try {
      if (navigator.clipboard?.writeText) {
        await navigator.clipboard.writeText(klartext)
      } else {
        altesKopieren(klartext)
      }
      setKopiert('ja')
    } catch {
      // Im Home-Assistant-Rahmen ohne HTTPS gibt es die Zwischenablage oft
      // nicht — dann der alte Weg, und wenn auch der nicht geht, sagen wir es.
      try { altesKopieren(klartext); setKopiert('ja') } catch { setKopiert('nein') }
    }
  }

  return (
    <div
      ref={kasten}
      className="ki-klartext scroll-ziel"
      data-audit="ki-klartext"
      role="region"
      aria-labelledby={titel}
      aria-describedby={satz}
      tabIndex={-1}
    >
      <strong id={titel}>Neuer Schlüssel „{name}"</strong>
      <code className="ki-klartext-wert" data-audit="ki-klartext-wert">{klartext}</code>
      <p className="ki-klartext-satz" id={satz}>Wird nur jetzt angezeigt — danach nicht mehr.</p>
      <p className="ki-klartext-hinweis">Kopiere ihn jetzt in deinen Assistenten. Geht er verloren, lösche den Schlüssel und lege einen neuen an.</p>
      {kopiert === 'ja' && <span className="ki-klartext-kopiert">Kopiert.</span>}
      {kopiert === 'nein' && <span className="ki-fehler">Kopieren hat nicht geklappt — bitte den Schlüssel markieren und von Hand kopieren.</span>}
      <div className="ki-knoepfe">
        <button type="button" className="ls-btn is-primary" onClick={() => void kopieren()} data-audit="ki-klartext-kopieren">Kopieren</button>
        <button type="button" className="ls-btn" onClick={onAusblenden} data-audit="ki-klartext-ausblenden">Erledigt, ausblenden</button>
      </div>
    </div>
  )
}
