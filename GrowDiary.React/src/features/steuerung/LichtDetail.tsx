import { useCallback, useEffect, useMemo, useState } from 'react'
import { apiFetch, formatApiError } from '../../api'
import { V1Alert, V1Button, V1Card, V1LinkButton, V1Page, V1Section, V1Skeleton, V1Switch, V1Tabs } from '../../components/v1'
import { LICHT_MODI, LICHT_REITER } from './steuerung-typen'
import type { LichtEinstellungen, LichtReiter, LichtSeite, SteuerungModul } from './steuerung-typen'
import './steuerung.css'

/**
 * Fork AI: Steuerung › Licht — die Bedienung der LED, die bisher als eigene
 * Karte im HA-Dashboard lag.
 *
 * <b>Warum die Presets nur Vorlagen sind.</b> Der AC-Infinity-Controller kennt
 * genau EINE Ein- und eine Aus-Zeit. „Veggie" und „Blüte" sind zwei
 * gespeicherte Paare, die diese eine Zeit überschreiben; welches gilt, erkennt
 * man daran, dass die Controller-Zeiten dazu passen. Deshalb gibt es keinen
 * Schalter „aktives Preset", sondern einen Vergleich.
 *
 * <b>Warum die Seite von selbst nachlädt.</b> Die AC-Infinity-Cloud nimmt
 * Befehle an, ohne sie auszuführen. Das Backend prüft seine offenen Sollwerte
 * beim Laden des Livebilds und schreibt notfalls nach — ohne regelmäßiges
 * Nachladen fände diese Prüfung nie statt.
 */
export default function LichtDetail({ module, aktiv, onWechsel }: {
  module: SteuerungModul[]
  aktiv: string
  onWechsel: (kennung: string) => void
}) {
  const [seite, setSeite] = useState<LichtSeite | null>(null)
  const [entwurf, setEntwurf] = useState<LichtEinstellungen | null>(null)
  const [reiter, setReiter] = useState<LichtReiter>('betrieb')
  const [fehler, setFehler] = useState<string | null>(null)
  const [feldFehler, setFeldFehler] = useState<Record<string, string>>({})
  const [meldung, setMeldung] = useState<string | null>(null)
  const [laedt, setLaedt] = useState(true)
  const [arbeitet, setArbeitet] = useState(false)

  /** Nachladen im Takt — hier steht nur das Auffrischen, nicht der erste Abruf. */
  const auffrischen = useCallback(async () => {
    try {
      const geladen = await apiFetch<LichtSeite>('/api/steuerung/licht')
      setSeite(geladen)
      // Einen angefangenen Entwurf nicht ueberschreiben, sonst springt beim
      // Nachladen die Zeit zurueck, die gerade getippt wird.
      setEntwurf((vorher) => vorher ?? geladen.einstellungen)
    } catch {
      // Ein misslungenes Auffrischen ist kein Grund, die Seite rot zu faerben —
      // beim naechsten Takt in zehn Sekunden steht es wieder.
    }
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    const laden = async () => {
      setLaedt(true)
      try {
        const geladen = await apiFetch<LichtSeite>('/api/steuerung/licht', { signal: controller.signal })
        if (!controller.signal.aborted) { setSeite(geladen); setEntwurf(geladen.einstellungen); setFehler(null) }
      } catch (caught) {
        if (!controller.signal.aborted) setFehler(formatApiError(caught, 'Die Licht-Steuerung konnte nicht geladen werden.'))
      } finally {
        if (!controller.signal.aborted) setLaedt(false)
      }
    }
    void laden()
    return () => controller.abort()
  }, [])

  // Alle zehn Sekunden nachsehen: uebernommen, noch offen, oder endgueltig
  // nicht angekommen. Das ist zugleich der Takt, in dem das Backend seine
  // Wiederholungen ausloest.
  useEffect(() => {
    const uhr = window.setInterval(() => { void auffrischen() }, 10000)
    return () => window.clearInterval(uhr)
  }, [auffrischen])

  const geaendert = useMemo(
    () => Boolean(seite && entwurf) && JSON.stringify(seite?.einstellungen) !== JSON.stringify(entwurf),
    [seite, entwurf],
  )

  const speichern = async () => {
    if (!entwurf) return
    setArbeitet(true); setMeldung(null); setFeldFehler({})
    try {
      const zurueck = await apiFetch<LichtSeite>('/api/steuerung/licht', { method: 'PUT', body: JSON.stringify(entwurf) })
      setSeite(zurueck); setEntwurf(zurueck.einstellungen); setFehler(null)
      setMeldung(zurueck.haAngenommen === false
        ? 'Gespeichert — aber nicht alles kam beim Controller an. Die Seite prüft es weiter nach.'
        : 'Gespeichert.')
    } catch (caught) {
      const felder = (caught as { fields?: Record<string, string> })?.fields
      if (felder) { setFeldFehler(felder); setFehler('Bitte die markierten Felder prüfen.') }
      else setFehler(formatApiError(caught, 'Speichern fehlgeschlagen.'))
    } finally {
      setArbeitet(false)
    }
  }

  const befehl = async (art: string, daten?: { preset?: string; stufe?: number }) => {
    setArbeitet(true); setMeldung(null)
    try {
      const zurueck = await apiFetch<LichtSeite>('/api/steuerung/licht/befehl', {
        method: 'POST',
        body: JSON.stringify({ art, preset: daten?.preset ?? null, stufe: daten?.stufe ?? null }),
      })
      setSeite(zurueck); setEntwurf(zurueck.einstellungen); setFehler(null)
    } catch (caught) {
      setFehler(formatApiError(caught, 'Der Befehl konnte nicht gesendet werden.'))
    } finally {
      setArbeitet(false)
    }
  }

  if (laedt && !seite) return <V1Page eyebrow="Steuerung" title="Licht LED Top"><V1Skeleton rows={5} label="Wird geladen" /></V1Page>
  if (!seite || !entwurf) {
    return <V1Page eyebrow="Steuerung" title="Licht LED Top">{fehler && <V1Alert tone="critical" message={fehler} />}</V1Page>
  }

  const live = seite.live
  const setz = <K extends keyof LichtEinstellungen>(feld: K, wert: LichtEinstellungen[K]) => setEntwurf({ ...entwurf, [feld]: wert })
  const offen = live.unbestaetigt.length > 0
  const gescheitert = live.fehlgeschlagen.length > 0

  const istZeitplan = live.modus === LICHT_MODI.zeitplan
  const modusText = live.modus === LICHT_MODI.aus ? 'AUS'
    : live.modus === LICHT_MODI.an ? 'DAUERLICHT'
    : istZeitplan ? `ZEITPLAN${live.aktivesPreset === 'veggie' ? ' (VEGGIE)' : live.aktivesPreset === 'bluete' ? ' (BLÜTE)' : ' (EIGENE ZEITEN)'}`
    : (live.modus ?? 'UNBEKANNT').toUpperCase()

  return (
    <V1Page
      eyebrow="Steuerung"
      title="Licht LED Top"
      subtitle="AC-Infinity-Controller · Zeitplan läuft im Gerät"
      action={geaendert ? <V1Button variant="primary" onClick={speichern} disabled={arbeitet}>{arbeitet ? 'Speichert …' : 'Speichern'}</V1Button> : undefined}
    >
      <div className="st-wechsel" role="tablist" aria-label="Steuerung wechseln">
        {module.map((m) => (
          <button
            key={m.kennung}
            type="button"
            role="tab"
            className="st-chip"
            aria-current={m.kennung === aktiv}
            disabled={!m.hatDetail}
            onClick={() => onWechsel(m.kennung)}
          >
            <i className={m.status === 'an' ? 'is-an' : m.status === 'warn' ? 'is-warn' : ''} aria-hidden="true" />
            {m.titel}
          </button>
        ))}
      </div>

      <div className="st-geraete-zeile">
        <span>
          Geräte{' '}
          <b className={seite.geraeteZugeordnet < seite.geraeteGesamt ? 'is-offen' : undefined}>
            {seite.geraeteZugeordnet} / {seite.geraeteGesamt}
          </b>{' '}
          zugeordnet
        </span>
        <V1LinkButton to="/steuerung/geraete" variant="ghost">Geräte &amp; Entitäten ›</V1LinkButton>
      </div>

      {fehler && <V1Alert tone="critical" message={fehler} />}
      {meldung && <V1Alert tone={meldung === 'Gespeichert.' ? 'ok' : 'warn'} message={meldung} />}
      {!live.haErreichbar && <V1Alert title="Home Assistant antwortet nicht" message="Die Werte sind der letzte bekannte Stand. Der Zeitplan läuft im Controller weiter." />}
      {live.controllerOnline === false && <V1Alert tone="warn" title="Controller offline" message="Der Port meldet sich nicht. Befehle kommen erst an, wenn er wieder da ist." />}

      {gescheitert && (
        <>
          <V1Alert
            tone="critical"
            title="Befehl vom Controller nicht bestätigt"
            message="Die AC-Infinity-Cloud hat den Befehl angenommen, aber nicht ausgeführt — auch nach den Wiederholungen nicht. Unten steht der echte Zustand des Geräts."
          />
          <div className="st-geraete-zeile">
            <span>Erneut versuchen oder die Meldung wegräumen.</span>
            <V1Button variant="ghost" onClick={() => void befehl('quittieren')} disabled={arbeitet}>Zur Kenntnis</V1Button>
          </div>
        </>
      )}

      <V1Card>
        <div className="st-jetzt">
          <span className="st-gross">{live.lichtAn == null ? '–' : live.lichtAn ? 'AN' : 'AUS'}<span>Stufe {live.stufe ?? '–'}</span></span>
          <span className="st-neben">
            {modusText}
            {istZeitplan && <> · {live.einZeit ?? '–'} – {live.ausZeit ?? '–'}</>}
            <br />
            {live.naechsterWechsel ? `Nächster Wechsel: ${live.naechsterWechsel}` : 'Kein Zeitplan aktiv'}
          </span>
        </div>
        <div className="st-marken">
          {offen && <span className="st-marke is-warn">wird übernommen …</span>}
          {live.controllerOnline === true && <span className="st-marke is-ok">Controller online</span>}
          {live.aktivesPreset == null && istZeitplan && <span className="st-marke is-warn">Zeiten gehören zu keinem Zeitplan</span>}
        </div>
      </V1Card>

      <V1Tabs items={LICHT_REITER} active={reiter} onChange={setReiter} label="Bereich" />

      {reiter === 'betrieb' && (
        <V1Section title="Betrieb">
          <V1Card>
            <div className="st-feldzeile">
              <span className="st-etikett">
                Betriebsart
                <small>Der Controller führt sie selbst aus — auch wenn Home Assistant aus ist.</small>
              </span>
              <div className="st-eingaben">
                <V1Button variant={live.modus === LICHT_MODI.aus ? 'primary' : 'ghost'} onClick={() => void befehl('aus')} disabled={arbeitet}>Aus</V1Button>
                <V1Button variant={live.modus === LICHT_MODI.an ? 'primary' : 'ghost'} onClick={() => void befehl('an')} disabled={arbeitet}>An</V1Button>
              </div>
            </div>

            <div className="st-feldzeile">
              <span className="st-etikett">
                Zeitplan anwenden
                <small>Schreibt die gespeicherten Zeiten in den Controller und schaltet auf Zeitplan.</small>
              </span>
              <div className="st-eingaben">
                <V1Button
                  variant={istZeitplan && live.aktivesPreset === 'veggie' ? 'primary' : 'ghost'}
                  onClick={() => void befehl('preset', { preset: 'veggie' })}
                  disabled={arbeitet}
                >
                  Veggie {entwurf.veggieEin}–{entwurf.veggieAus}
                </V1Button>
                <V1Button
                  variant={istZeitplan && live.aktivesPreset === 'bluete' ? 'primary' : 'ghost'}
                  onClick={() => void befehl('preset', { preset: 'bluete' })}
                  disabled={arbeitet}
                >
                  Blüte {entwurf.blueteEin}–{entwurf.blueteAus}
                </V1Button>
              </div>
            </div>

            <div className="st-feldzeile">
              <span className="st-etikett">
                Leistungsstufe
                <small>1 bis 10. Wirkt sofort, unabhängig von der Betriebsart.</small>
              </span>
              <span className="st-eingaben">
                <V1Button variant="ghost" onClick={() => void befehl('stufe', { stufe: Math.max(1, (live.stufe ?? entwurf.stufe) - 1) })} disabled={arbeitet}>−</V1Button>
                <input
                  type="number"
                  inputMode="numeric"
                  min={1}
                  max={10}
                  step={1}
                  aria-label="Leistungsstufe"
                  value={live.stufe ?? entwurf.stufe}
                  onChange={(e) => {
                    const wert = Number(e.target.value)
                    if (Number.isFinite(wert) && wert >= 1 && wert <= 10) void befehl('stufe', { stufe: wert })
                  }}
                />
                <V1Button variant="ghost" onClick={() => void befehl('stufe', { stufe: Math.min(10, (live.stufe ?? entwurf.stufe) + 1) })} disabled={arbeitet}>+</V1Button>
              </span>
            </div>
          </V1Card>
        </V1Section>
      )}

      {reiter === 'zeitplan' && (
        <V1Section title="Zeitpläne">
          <V1Card>
            <Zeit label="Veggie · an" wert={entwurf.veggieEin} onChange={(v) => setz('veggieEin', v)} fehler={feldFehler.VeggieEin} />
            <Zeit label="Veggie · aus" wert={entwurf.veggieAus} onChange={(v) => setz('veggieAus', v)} fehler={feldFehler.VeggieAus} />
            <Zeit label="Blüte · an" wert={entwurf.blueteEin} onChange={(v) => setz('blueteEin', v)} fehler={feldFehler.BlueteEin} />
            <Zeit label="Blüte · aus" wert={entwurf.blueteAus} onChange={(v) => setz('blueteAus', v)} fehler={feldFehler.BlueteAus} />
            <p className="st-hinweis">
              Gespeichert wird im Fork. Der Controller bekommt die Zeiten, wenn du den Zeitplan anwendest —
              und sofort, wenn du den gerade aktiven Zeitplan bearbeitest.
            </p>
          </V1Card>
        </V1Section>
      )}

      {reiter === 'erweitert' && (
        <V1Section title="Erweitert">
          <V1Card>
            <Zahl
              label="Abstand zwischen Befehlen"
              hinweis="Die AC-Infinity-Cloud verwirft Befehle, die gleichzeitig kommen."
              einheit="ms"
              schritt={500}
              wert={entwurf.schreibAbstandMs}
              onChange={(v) => setz('schreibAbstandMs', v)}
              fehler={feldFehler.SchreibAbstandMs}
            />
            <Zahl
              label="Prüfen nach"
              hinweis="So lange bekommt der Controller Zeit, den Befehl zu übernehmen."
              einheit="s"
              wert={entwurf.verifySekunden}
              onChange={(v) => setz('verifySekunden', v)}
              fehler={feldFehler.VerifySekunden}
            />
            <Zahl
              label="Wiederholungen"
              hinweis="Danach steht die Warnung statt eines stillen Fehlschlags."
              wert={entwurf.maxWiederholungen}
              onChange={(v) => setz('maxWiederholungen', v)}
              fehler={feldFehler.MaxWiederholungen}
            />
            <V1Switch
              label="Helfer in Home Assistant mitschreiben"
              checked={entwurf.helferSpiegeln}
              onChange={(v) => setz('helferSpiegeln', v)}
              hint="Hält die vier led_top_*-Helfer aktuell, die die alte Karte im Grow-Dashboard anzeigt. Aus, sobald die Karte weg ist."
            />
          </V1Card>
        </V1Section>
      )}

      <p className="st-fuss">Der Zeitplan läuft im Controller — der Fork schickt nur Änderungen</p>
    </V1Page>
  )
}

/** Eine Uhrzeit-Zeile — Etikett links, Feld rechts. */
function Zeit({ label, wert, onChange, fehler }: {
  label: string
  wert: string
  onChange: (wert: string) => void
  fehler?: string
}) {
  return (
    <div className="st-feldzeile">
      <span className="st-etikett">
        {label}
        {fehler && <span className="st-fehler">{fehler}</span>}
      </span>
      <span className="st-eingaben">
        <input
          type="time"
          value={wert}
          aria-label={label}
          aria-invalid={fehler ? true : undefined}
          onChange={(e) => onChange(e.target.value)}
        />
      </span>
    </div>
  )
}

/** Wie auf der CO₂-Seite: Zahl rechts, Fehler unter dem Etikett. */
function Zahl({ label, hinweis, einheit, wert, onChange, fehler, schritt = 1 }: {
  label: string
  hinweis?: string
  einheit?: string
  wert: number
  onChange: (wert: number) => void
  fehler?: string
  schritt?: number
}) {
  return (
    <div className="st-feldzeile">
      <span className="st-etikett">
        {label}
        {hinweis && <small>{hinweis}</small>}
        {fehler && <span className="st-fehler">{fehler}</span>}
      </span>
      <span className="st-eingaben">
        <input
          type="number"
          inputMode="numeric"
          step={schritt}
          value={Number.isFinite(wert) ? wert : ''}
          aria-label={label}
          aria-invalid={fehler ? true : undefined}
          onChange={(e) => onChange(e.target.value === '' ? Number.NaN : Number(e.target.value))}
        />
        {einheit && <span className="st-einheit">{einheit}</span>}
      </span>
    </div>
  )
}
