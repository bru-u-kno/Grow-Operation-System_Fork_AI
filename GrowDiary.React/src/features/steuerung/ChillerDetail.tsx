import { useCallback, useEffect, useMemo, useState } from 'react'
import { apiFetch, formatApiError } from '../../api'
import { V1Alert, V1Button, V1Card, V1LinkButton, V1Page, V1Section, V1Skeleton, V1Stat, V1Switch, V1Tabs } from '../../components/v1'
import { CHILLER_REITER } from './steuerung-typen'
import type { ChillerEinstellungen, ChillerReiter, ChillerSeite, SteuerungModul } from './steuerung-typen'
import './steuerung.css'
import { rollenPfad } from '../geraete/rollenPfad'

/**
 * Fork AI: Steuerung › Water Chiller — der Wasserkühler, der bisher als Kachel
 * im HA-Dashboard lag.
 *
 * <b>Was hier NICHT geschaltet wird.</b> Wie bei der Zuluft läuft die Regelung
 * in Home Assistant. Beim Kühler wiegt das schwerer als anderswo: an der
 * Mindestpause hängt die Lebensdauer des Kompressors, und die darf nicht davon
 * abhängen, ob dieses Add-on gerade aktualisiert wird.
 *
 * <b>Warum das Ziel hier nur steht.</b> Tag- und Nachtziel gehören dem Plan.
 * Diese Seite sagt, WOHER der Wert kommt, und verlinkt dorthin — zwei Seiten,
 * die denselben Helfer schreiben, wären genau der Zustand, den der
 * Wochenplan-Abgleich verhindern soll.
 *
 * <b>Ansteuerung (forkai.137, Mockup-Variante B).</b> Ob der Kühler über eine
 * Steckdose geschaltet wird oder einen eigenen Thermostat hat, ergibt sich aus
 * den Rollen — hier gibt es keinen eigenen Umschalter. Die Seite zeigt nur, was
 * daraus folgt, und blendet aus, was für die Ansteuerung nicht gilt.
 */
export default function ChillerDetail({ module, aktiv, onWechsel }: {
  module: SteuerungModul[]
  aktiv: string
  onWechsel: (kennung: string) => void
}) {
  const [seite, setSeite] = useState<ChillerSeite | null>(null)
  const [entwurf, setEntwurf] = useState<ChillerEinstellungen | null>(null)
  const [reiter, setReiter] = useState<ChillerReiter>('betrieb')
  const [fehler, setFehler] = useState<string | null>(null)
  const [feldFehler, setFeldFehler] = useState<Record<string, string>>({})
  const [meldung, setMeldung] = useState<string | null>(null)
  const [laedt, setLaedt] = useState(true)
  const [arbeitet, setArbeitet] = useState(false)

  const auffrischen = useCallback(async () => {
    try {
      const geladen = await apiFetch<ChillerSeite>('/api/steuerung/chiller')
      setSeite(geladen)
      setEntwurf((vorher) => vorher ?? geladen.einstellungen)
    } catch {
      // Ein misslungenes Auffrischen ist kein Grund, die Seite rot zu färben.
    }
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    const laden = async () => {
      setLaedt(true)
      try {
        const geladen = await apiFetch<ChillerSeite>('/api/steuerung/chiller', { signal: controller.signal })
        if (!controller.signal.aborted) { setSeite(geladen); setEntwurf(geladen.einstellungen); setFehler(null) }
      } catch (caught) {
        if (!controller.signal.aborted) setFehler(formatApiError(caught, 'Die Kühler-Steuerung konnte nicht geladen werden.'))
      } finally {
        if (!controller.signal.aborted) setLaedt(false)
      }
    }
    void laden()
    return () => controller.abort()
  }, [])

  // Eine Minute Takt — die Regelung selbst prüft im Minutentakt, und
  // Wassertemperatur bewegt sich träge.
  useEffect(() => {
    const uhr = window.setInterval(() => { void auffrischen() }, 60000)
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
      const zurueck = await apiFetch<ChillerSeite>('/api/steuerung/chiller', { method: 'PUT', body: JSON.stringify(entwurf) })
      setSeite(zurueck); setEntwurf(zurueck.einstellungen); setFehler(null)
      setMeldung(zurueck.haAngenommen === false
        ? 'Gespeichert — aber nicht alle Helfer haben den Wert angenommen.'
        : 'Gespeichert.')
    } catch (caught) {
      const felder = (caught as { fields?: Record<string, string> })?.fields
      if (felder) { setFeldFehler(felder); setFehler('Bitte die markierten Felder prüfen.') }
      else setFehler(formatApiError(caught, 'Speichern fehlgeschlagen.'))
    } finally {
      setArbeitet(false)
    }
  }

  if (laedt && !seite) return <V1Page eyebrow="Steuerung" title="Water Chiller"><V1Skeleton rows={4} tiles={4} label="Wird geladen" /></V1Page>
  if (!seite || !entwurf) {
    return <V1Page eyebrow="Steuerung" title="Water Chiller">{fehler && <V1Alert tone="critical" message={fehler} />}</V1Page>
  }

  const live = seite.live
  const setz = <K extends keyof ChillerEinstellungen>(feld: K, wert: ChillerEinstellungen[K]) =>
    setEntwurf({ ...entwurf, [feld]: wert })

  const ansteuerung = live.ansteuerung ?? 'steckdose'
  const perSteckdose = ansteuerung === 'steckdose'
  const mitGeraet = ansteuerung === 'regelbar' || ansteuerung === 'beides'
  const kuehlerAus = live.kuehlerZustand == null || ['off', 'unavailable', 'unknown'].includes(live.kuehlerZustand)
  const zustand = mitGeraet
    ? (kuehlerAus ? 'aus' : 'bereit')
    : live.steckdoseAn === true ? 'kühlt' : live.kuehlbedarf === true ? 'wartet' : 'bereit'
  const sollStimmt = live.kuehlerSollC != null && live.zielAktivC != null
    && Math.abs(live.kuehlerSollC - live.zielAktivC) <= 0.05
  const ANSTEUERUNG_TEXT: Record<string, { titel: string; text: string }> = {
    steckdose: { titel: 'Steckdose', text: 'Home Assistant schaltet den Kühler ein und aus.' },
    regelbar: { titel: 'Regelbarer Kühler', text: 'Der Kühler regelt selbst — Home Assistant schreibt nur das Ziel ins Gerät.' },
    beides: { titel: 'Regelbarer Kühler + Not-Aus', text: 'Das Ziel geht ins Gerät, die Steckdose schaltet nur der Wächter ab.' },
    keine: { titel: 'Nicht eingerichtet', text: 'Weder Steckdose noch Sollwert-Gerät zugeordnet.' },
  }
  const art = ANSTEUERUNG_TEXT[ansteuerung] ?? ANSTEUERUNG_TEXT.steckdose
  const grad1 = (wert: number | null | undefined) =>
    wert == null ? '–' : wert.toLocaleString('de-DE', { minimumFractionDigits: 1, maximumFractionDigits: 1 })
  const zahl = (wert: number | null | undefined) => (wert == null ? '–' : wert.toLocaleString('de-DE', { maximumFractionDigits: 1 }))

  return (
    <V1Page
      eyebrow="Steuerung"
      title="Water Chiller"
      subtitle="Hält das Nährwasser auf dem Ziel der laufenden Woche — geschaltet in Home Assistant"
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

      {fehler && <V1Alert tone="critical" message={fehler} />}
      {meldung && <V1Alert tone={meldung === 'Gespeichert.' ? 'ok' : 'warn'} message={meldung} />}
      {!live.haErreichbar && <V1Alert title="Home Assistant antwortet nicht" message="Die Werte sind der letzte bekannte Stand. Die Regelung läuft dort weiter." />}
      {live.automatikAn === false && (
        <V1Alert
          tone="warn"
          title="Automatik aus"
          message="Die Regelung ist angehalten. Der Kühler bleibt, wie er gerade steht — er wird weder ein- noch ausgeschaltet."
        />
      )}
      {ansteuerung === 'keine' && (
        <V1Alert
          tone="warn"
          title="Kein Kühler zugeordnet"
          message="Ordne unter Rollen eine Steckdose oder ein Sollwert-Gerät (climate/number) zu — vorher kann nichts geregelt werden."
        />
      )}
      {live.waechterAn === false && (
        <V1Alert
          tone="warn"
          title="Wächter aus"
          message="Fällt der Wasserfühler aus, schaltet niemand mehr ab. Der Wächter gehört eingeschaltet."
        />
      )}
      {seite.ausHomeAssistantUebernommen && (
        <V1Alert
          title="Werte aus Home Assistant übernommen"
          message="Hier steht, was in den Helfern steht — noch nichts davon ist im Fork gespeichert. Mit dem ersten Speichern übernimmt der Fork die Führung."
        />
      )}

      <section className="v1-kpi-grid">
        <V1Stat
          label="Wasser"
          value={live.wasserC == null ? '–' : live.wasserC.toFixed(1)}
          unit=" °C"
          tone={live.kuehlbedarf === true ? 'warn' : 'ok'}
        />
        <V1Stat
          label="Ziel jetzt"
          value={live.zielAktivC == null ? '–' : live.zielAktivC.toFixed(1)}
          unit=" °C"
          hint={live.tagPhase == null ? null : live.tagPhase ? 'Tag' : 'Nacht'}
        />
        <V1Stat
          label="Gerät"
          value={zustand}
          wortwert
          tone={(mitGeraet ? !kuehlerAus : live.steckdoseAn === true) ? 'ok' : 'neutral'}
          hint={mitGeraet ? 'regelt selbst' : perSteckdose ? 'Steckdose' : null}
        />
        {perSteckdose ? (
          <V1Stat
            label="Schaltsperre"
            value={live.sperreRestMin == null ? '–' : live.sperreRestMin === 0 ? 'frei' : live.sperreRestMin}
            unit={live.sperreRestMin ? ' min' : null}
            wortwert={live.sperreRestMin === 0}
            hint={live.letzterWechsel ? `zuletzt ${new Date(live.letzterWechsel).toLocaleString('de-DE', { dateStyle: 'short', timeStyle: 'short' })}` : 'noch nie geschaltet'}
          />
        ) : (
          <V1Stat
            label="Soll im Gerät"
            value={live.kuehlerSollC == null ? '–' : live.kuehlerSollC.toFixed(1)}
            unit={live.kuehlerSollC == null ? null : ' °C'}
            tone={live.kuehlerSollC == null ? 'neutral' : sollStimmt ? 'ok' : 'warn'}
            hint={live.kuehlerSollC == null ? 'kein Wert vom Gerät' : sollStimmt ? 'stimmt mit dem Ziel' : 'weicht vom Ziel ab'}
          />
        )}
      </section>

      <V1Card>
        <div className="st-feldzeile">
          <span className="st-etikett">
            Ziel Tag {live.zielTagC?.toLocaleString('de-DE') ?? '–'} °C · Nacht {live.zielNachtC?.toLocaleString('de-DE') ?? '–'} °C
            <small>
              {live.zielQuelle === 'hand'
                ? 'Von dir gesetzt — der Plan überschreibt es nicht.'
                : 'Aus dem Plan des Grows, nach Phase und Woche.'}
            </small>
          </span>
          <V1LinkButton to="/plan" variant="ghost">Plan ›</V1LinkButton>
        </div>
        <p className="st-hinweis">
          Welches der beiden Ziele gilt, entscheidet der Lichtzustand und nicht die Uhr. Verschiebt sich die
          Lichtphase, verschiebt sich das Ziel mit.
        </p>
      </V1Card>

      <V1Tabs items={CHILLER_REITER} active={reiter} onChange={setReiter} label="Bereich" />

      {reiter === 'betrieb' && (
        <V1Section title="Betrieb">
          <V1Card>
            <V1Switch
              label="Automatik aktiv"
              checked={entwurf.automatikAktiv}
              onChange={(an) => setz('automatikAktiv', an)}
              hint="Aus hält die Regelung an. Der Kühler bleibt, wie er gerade steht — es ist der einzige Aus-Knopf."
            />
            <div className="st-feldzeile">
              <span className="st-etikett">
                Ansteuerung · {art.titel}
                <small>{art.text} Ergibt sich aus den Rollen.</small>
              </span>
              <V1LinkButton to={rollenPfad('chiller')} variant="ghost">Rollen ›</V1LinkButton>
            </div>
            {(perSteckdose || ansteuerung === 'beides') && (
              <div className="st-feldzeile">
                <span className="st-etikett">
                  Steckdose
                  <small>
                    {ansteuerung === 'beides'
                      ? 'nur Not-Aus für den Wächter'
                      : live.steckdoseAn === true
                        ? live.leistungW == null ? 'läuft' : `läuft · ${live.leistungW.toLocaleString('de-DE', { maximumFractionDigits: 0 })} W`
                        : 'steht'}
                  </small>
                </span>
                <span className="st-nurlesen">{live.steckdoseAn === true ? 'AN' : live.steckdoseAn === false ? 'AUS' : '–'}</span>
              </div>
            )}
            {perSteckdose && (
              <div className="st-feldzeile">
                <span className="st-etikett">
                  Schaltpunkte
                  <small>Dazwischen passiert nichts.</small>
                </span>
                <span className="st-nurlesen">
                  {live.einschaltenAbC == null ? '–' : `ein ${grad1(live.einschaltenAbC)} · aus ${grad1(live.ausschaltenBeiC)} °C`}
                </span>
              </div>
            )}
            {mitGeraet && (
              <>
                <div className="st-feldzeile">
                  <span className="st-etikett">
                    Kühler
                    <small>Gerät mit eigenem Thermostat · {live.kuehlerZustand ?? 'kein Zustand'}</small>
                  </span>
                  <span className="st-nurlesen">{live.kuehlerEntity ?? '–'}</span>
                </div>
                <div className="st-feldzeile">
                  <span className="st-etikett">
                    Soll im Gerät
                    <small>{sollStimmt ? 'stimmt mit dem Ziel' : 'wird beim nächsten Abgleich nachgezogen'}</small>
                  </span>
                  <span className="st-nurlesen">{zahl(live.kuehlerSollC)} °C</span>
                </div>
              </>
            )}
            <p className="st-hinweis">
              {mitGeraet
                ? 'Beim Wechsel Tag/Nacht, bei neuer Plan-Woche und alle 15 Minuten schreibt Home Assistant das Ziel ins Gerät. Den Rest regelt der Kühler selbst.'
                : 'Geschaltet wird in Home Assistant, nicht hier. Das ist Absicht: an der Mindestpause hängt die Lebensdauer des Kompressors, und sie soll weiterlaufen, wenn dieses Add-on gerade neu startet.'}
            </p>
          </V1Card>
        </V1Section>
      )}

      {reiter === 'schutz' && (
        <V1Section title="Schutz">
          <V1Card>
            {perSteckdose ? (
              <>
              <Zahl
                label="Einschalten ab Ziel +"
                hinweis="Um so viel muss das Wasser über dem Ziel liegen, bevor der Kühler startet. Aus geht er beim Ziel. Zu eng, und der Kompressor taktet im Messrauschen."
                einheit="K"
                wert={entwurf.hystereseK}
                min={0.1}
                max={3}
                schritt={0.1}
                onChange={(v) => setz('hystereseK', v)}
                fehler={feldFehler.HystereseK}
              />
              <Zahl
                label="Mindestlaufzeit"
                hinweis="Wie lange der Kühler mindestens läuft, bevor er wieder aus darf."
                einheit="min"
                wert={entwurf.mindestlaufzeitMin}
                min={0}
                max={120}
                schritt={1}
                onChange={(v) => setz('mindestlaufzeitMin', Math.round(v))}
                fehler={feldFehler.MindestlaufzeitMin}
              />
              <Zahl
                label="Mindestpause"
                hinweis="Wie lange er mindestens aus bleibt. Zu kurze Pausen kosten den Kompressor das Leben."
                einheit="min"
                wert={entwurf.mindestpauseMin}
                min={0}
                max={120}
                schritt={1}
                onChange={(v) => setz('mindestpauseMin', Math.round(v))}
                fehler={feldFehler.MindestpauseMin}
              />
              </>
            ) : (
              <div className="st-feldzeile">
                <span className="st-etikett">
                  Schaltabstand und Sperren
                  <small>Regelt der Kühler selbst — hier gibt es nichts einzustellen.</small>
                </span>
                <span className="st-nurlesen">–</span>
              </div>
            )}
            <div className="st-feldzeile">
              <span className="st-etikett">
                Wächter
                <small>
                  {mitGeraet
                    ? ansteuerung === 'beides'
                      ? 'Bleibt der Wasserfühler fünf Minuten stumm, schaltet er Kühler und Steckdose ab. Warnt bei zu warmem Wasser.'
                      : 'Bleibt der Wasserfühler fünf Minuten stumm, schaltet er den Kühler ab. Warnt bei zu warmem Wasser.'
                    : 'Bleibt der Wasserfühler fünf Minuten stumm, schaltet er die Steckdose ab. Warnt bei zu warmem Wasser.'}
                </small>
              </span>
              <span className="st-nurlesen">{live.waechterAn === true ? 'AN' : live.waechterAn === false ? 'AUS' : '–'}</span>
            </div>

          </V1Card>
        </V1Section>
      )}

      <div className="st-geraete-zeile">
        <span>
          Geräte{' '}
          <b className={seite.geraeteZugeordnet < seite.geraeteGesamt ? 'is-offen' : undefined}>
            {seite.geraeteZugeordnet} / {seite.geraeteGesamt}
          </b>{' '}
          zugeordnet
        </span>
        <V1LinkButton to={rollenPfad('chiller')} variant="ghost">Rollen bearbeiten ›</V1LinkButton>
      </div>
    </V1Page>
  )
}

/**
 * Ein Zahlenfeld im Box-Modus. Bewusst kein Schieberegler: am Telefon trifft man
 * damit keinen bestimmten Wert.
 */
function Zahl({ label, hinweis, einheit, wert, min, max, schritt, onChange, fehler }: {
  label: string
  hinweis: string
  einheit?: string
  wert: number
  min: number
  max: number
  schritt: number
  onChange: (wert: number) => void
  fehler?: string
}) {
  return (
    <div className="st-feldzeile">
      <span className="st-etikett">
        {label}
        <small>{hinweis}</small>
        {fehler && <span className="st-fehler">{fehler}</span>}
      </span>
      <span className="st-eingaben">
        <input
          type="number"
          inputMode="decimal"
          min={min}
          max={max}
          step={schritt}
          aria-label={label}
          value={wert}
          onChange={(e) => {
            const neu = Number(e.target.value)
            if (Number.isFinite(neu)) onChange(neu)
          }}
        />
        {einheit && <span className="st-einheit">{einheit}</span>}
      </span>
    </div>
  )
}
