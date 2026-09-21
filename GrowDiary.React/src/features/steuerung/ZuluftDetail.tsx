import { useCallback, useEffect, useMemo, useState } from 'react'
import { apiFetch, formatApiError } from '../../api'
import { V1Alert, V1Button, V1Card, V1LinkButton, V1Page, V1Section, V1Skeleton, V1Stat, V1Switch, V1Tabs } from '../../components/v1'
import { V1Sheet } from '../../components/V1Sheet'
import { ZULUFT_REITER } from './steuerung-typen'
import type { SteuerungModul, ZuluftEinstellungen, ZuluftReiter, ZuluftSeite } from './steuerung-typen'
import './steuerung.css'

/**
 * Fork AI (forkai.76): Steuerung › Zuluft — die Kellerzuluft, die bisher als
 * Kachel im HA-Dashboard lag.
 *
 * <b>Warum die Zahlen oben stehen.</b> Am Telefon soll die Antwort auf „läuft
 * der Lüfter, und warum" ohne Scrollen dastehen. Die Einstellungen liegen
 * darunter in Reitern.
 *
 * <b>Warum der Rechenweg ein Blatt ist.</b> Draußen 88 % und der Lüfter saugt
 * trotzdem — das ergibt erst Sinn, wenn man sieht, dass 12 °C bei 88 % weniger
 * Wasser tragen als 22 °C bei 57 %. Als eigener Abschnitt kostete die Kette auf
 * jeder Ansicht Platz, obwohl man sie nur ab und zu braucht. Ein Tipp auf die
 * Differenz-Kachel öffnet sie — dasselbe Muster wie das Herkunftsblatt der
 * Zielwerte.
 *
 * <b>Was hier NICHT geschaltet wird.</b> Die Regelung läuft in Home Assistant.
 * Diese Seite hält die Sollwerte und den Aus-Knopf; sie legt keinen Port um.
 */
export default function ZuluftDetail({ module, aktiv, onWechsel }: {
  module: SteuerungModul[]
  aktiv: string
  onWechsel: (kennung: string) => void
}) {
  const [seite, setSeite] = useState<ZuluftSeite | null>(null)
  const [entwurf, setEntwurf] = useState<ZuluftEinstellungen | null>(null)
  const [reiter, setReiter] = useState<ZuluftReiter>('regel')
  const [rechenweg, setRechenweg] = useState(false)
  const [fehler, setFehler] = useState<string | null>(null)
  const [feldFehler, setFeldFehler] = useState<Record<string, string>>({})
  const [meldung, setMeldung] = useState<string | null>(null)
  const [laedt, setLaedt] = useState(true)
  const [arbeitet, setArbeitet] = useState(false)

  const auffrischen = useCallback(async () => {
    try {
      const geladen = await apiFetch<ZuluftSeite>('/api/steuerung/zuluft')
      setSeite(geladen)
      // Einen angefangenen Entwurf nicht überschreiben, sonst springt beim
      // Nachladen die Zahl zurück, die gerade getippt wird.
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
        const geladen = await apiFetch<ZuluftSeite>('/api/steuerung/zuluft', { signal: controller.signal })
        if (!controller.signal.aborted) { setSeite(geladen); setEntwurf(geladen.einstellungen); setFehler(null) }
      } catch (caught) {
        if (!controller.signal.aborted) setFehler(formatApiError(caught, 'Die Zuluft-Steuerung konnte nicht geladen werden.'))
      } finally {
        if (!controller.signal.aborted) setLaedt(false)
      }
    }
    void laden()
    return () => controller.abort()
  }, [])

  // Eine Minute Takt: schneller ändert sich nichts — die Regelung selbst prüft
  // im Minutentakt, und die Außenfeuchte springt nicht in Sekunden.
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
      const zurueck = await apiFetch<ZuluftSeite>('/api/steuerung/zuluft', { method: 'PUT', body: JSON.stringify(entwurf) })
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

  if (laedt && !seite) return <V1Page eyebrow="Steuerung" title="Zuluft Keller"><V1Skeleton rows={4} tiles={4} label="Wird geladen" /></V1Page>
  if (!seite || !entwurf) {
    return <V1Page eyebrow="Steuerung" title="Zuluft Keller">{fehler && <V1Alert tone="critical" message={fehler} />}</V1Page>
  }

  const live = seite.live
  const setz = <K extends keyof ZuluftEinstellungen>(feld: K, wert: ZuluftEinstellungen[K]) => setEntwurf({ ...entwurf, [feld]: wert })

  const zustand = live.portAn === true ? 'saugt' : live.bedarf === true ? 'wartet' : live.pauseZeltKalt === true ? 'pausiert' : 'bereit'

  return (
    <V1Page
      eyebrow="Steuerung"
      title="Zuluft Keller"
      subtitle="Außenluft ansaugen, solange sie trockener ist — geregelt in Home Assistant"
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
      {live.portOnline === false && <V1Alert tone="warn" title="Controller offline" message="Der Lüfter-Port meldet sich nicht. Geschaltet wird erst, wenn er wieder da ist." />}
      {live.automatikAn === false && (
        <V1Alert
          tone="warn"
          title="Automatik aus"
          message="Die Regelung ist angehalten. Der Lüfter bleibt, wie er gerade steht — er wird weder ein- noch ausgeschaltet."
        />
      )}
      {live.pauseZeltKalt === true && (
        <V1Alert
          tone="warn"
          title="Pausiert — Zelt zu kalt"
          message={`Die Außenluft würde trocknen, aber das Zelt hat ${Zeig(live.zeltTempC, ' °C', 1)}. Die Zuluft läuft wieder ab ${Zeig(entwurf.zeltTemperaturMinC == null ? null : entwurf.zeltTemperaturMinC + 1, ' °C', 1)}; bis dahin entfeuchtet der Trotec allein.`}
        />
      )}
      {seite.ausHomeAssistantUebernommen && (
        <V1Alert
          title="Werte aus Home Assistant übernommen"
          message="Hier steht, was in den Helfern steht — noch nichts davon ist im Fork gespeichert. Mit dem ersten Speichern übernimmt der Fork die Führung."
        />
      )}

      <section className="v1-kpi-grid">
        <button type="button" className="zl-kachel" onClick={() => setRechenweg(true)}>
          <V1Stat
            label="Differenz"
            value={live.differenzGm3 == null ? '–' : live.differenzGm3.toFixed(2)}
            unit=" g/m³"
            hint="Rechenweg ansehen ›"
            tone={live.bedarf === true ? 'ok' : 'neutral'}
          />
        </button>
        <V1Stat
          label="Stufe"
          value={live.istStufe ?? '–'}
          hint={live.zielstufe == null ? null : `Ziel ${live.zielstufe}`}
        />
        <V1Stat label="Zustand" value={zustand} wortwert tone={live.portAn === true ? 'ok' : 'neutral'} />
        <V1Stat
          label="Schaltsperre"
          value={live.sperreRestMin == null ? '–' : live.sperreRestMin === 0 ? 'frei' : live.sperreRestMin}
          unit={live.sperreRestMin ? ' min' : null}
          wortwert={live.sperreRestMin === 0}
          hint={live.letzterWechsel ? `zuletzt ${new Date(live.letzterWechsel).toLocaleString('de-DE', { dateStyle: 'short', timeStyle: 'short' })}` : 'noch nie geschaltet'}
        />
      </section>

      <V1Tabs items={ZULUFT_REITER} active={reiter} onChange={setReiter} label="Bereich" />

      {reiter === 'regel' && (
        <V1Section title="Regel">
          <V1Card>
            <Zahl
              label="Mindest-Differenz"
              hinweis="Ab wie viel Unterschied das Ansaugen lohnt. Gemeint ist absolute Feuchte, nicht Prozent."
              einheit="g/m³"
              wert={entwurf.mindestDifferenzGm3}
              min={0.2}
              max={10}
              schritt={0.1}
              onChange={(v) => setz('mindestDifferenzGm3', v)}
              fehler={feldFehler.MindestDifferenzGm3}
            />
            <Zahl
              label="Zelttemperatur min."
              hinweis="Fällt das Zelt darunter, pausiert die Zuluft — sie läuft wieder ab diesem Wert + 1 °C. Schützt vor Auskühlen in kalten Nächten."
              einheit="°C"
              wert={entwurf.zeltTemperaturMinC ?? 21}
              min={10}
              max={30}
              schritt={0.5}
              onChange={(v) => setz('zeltTemperaturMinC', v)}
              fehler={feldFehler.ZeltTemperaturMinC}
            />
            <Zahl
              label="Außentemperatur min. (Frostschutz)"
              hinweis="Darunter bleibt der Lüfter aus, egal wie trocken es draußen ist. Vor dem Auskühlen schützt die Zelttemperatur."
              einheit="°C"
              wert={entwurf.aussentemperaturMinC}
              min={-10}
              max={25}
              schritt={0.5}
              onChange={(v) => setz('aussentemperaturMinC', v)}
              fehler={feldFehler.AussentemperaturMinC}
            />
            <p className="st-hinweis">
              Abgeschaltet wird erst 0,5 g/m³ unter der Schwelle. Diese Hysterese steckt im Rechenwert
              und hält den Lüfter davon ab, an der Schwelle zu flattern.
            </p>
          </V1Card>
        </V1Section>
      )}

      {reiter === 'luefter' && (
        <V1Section title="Lüfter">
          <V1Card>
            <Zahl
              label="Stufe min."
              hinweis="Stufe bei knapper Differenz."
              wert={entwurf.stufeMin}
              min={1}
              max={10}
              schritt={1}
              onChange={(v) => setz('stufeMin', Math.round(v))}
              fehler={feldFehler.StufeMin}
            />
            <Zahl
              label="Stufe max."
              hinweis="Stufe bei großer Differenz. Gleich wie min. heißt: feste Stufe."
              wert={entwurf.stufeMax}
              min={1}
              max={10}
              schritt={1}
              onChange={(v) => setz('stufeMax', Math.round(v))}
              fehler={feldFehler.StufeMax}
            />
            <Zahl
              label="Mindestlaufzeit"
              hinweis="Wie lange der Lüfter mindestens läuft, bevor er wieder aus darf."
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
              hinweis="Wie lange er mindestens aus bleibt."
              einheit="min"
              wert={entwurf.mindestpauseMin}
              min={0}
              max={120}
              schritt={1}
              onChange={(v) => setz('mindestpauseMin', Math.round(v))}
              fehler={feldFehler.MindestpauseMin}
            />
          </V1Card>
        </V1Section>
      )}

      {reiter === 'betrieb' && (
        <V1Section title="Betrieb">
          <V1Card>
            <V1Switch
              label="Automatik aktiv"
              checked={entwurf.automatikAktiv}
              onChange={(an) => setz('automatikAktiv', an)}
              hint="Aus hält die Regelung an. Der Lüfter bleibt, wie er gerade steht — es ist der einzige Aus-Knopf."
            />
            <div className="st-feldzeile">
              <span className="st-etikett">
                Lüfter
                <small>{live.portOnline === false ? 'Port offline' : live.portAn === true ? `läuft auf Stufe ${live.istStufe ?? '–'}` : 'steht'}</small>
              </span>
              <span className="st-nurlesen">{live.portAn === true ? 'AN' : live.portAn === false ? 'AUS' : '–'}</span>
            </div>
            <p className="st-hinweis">
              Geschaltet wird in Home Assistant, nicht hier. Das ist Absicht: die Regelung soll weiterlaufen,
              wenn dieses Add-on gerade neu startet.
            </p>
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
        <V1LinkButton to="/steuerung/geraete" variant="ghost">Geräte &amp; Entitäten ›</V1LinkButton>
      </div>

      <V1Sheet open={rechenweg} onClose={() => setRechenweg(false)} title="Rechenweg" subtitle="Wie aus vier Messwerten eine Stufe wird">
        <div className="zl-kette">
          <Glied
            marke="Draußen"
            wert={`${Zeig(live.aussenTempC, ' °C', 1)} · ${Zeig(live.aussenRhProzent, ' %', 0)} → ${Zeig(live.aussenAbsolutGm3, ' g/m³', 2)}`}
          />
          <p className="zl-pfeil" aria-hidden="true">↓</p>
          <Glied
            marke="Keller"
            wert={`${Zeig(live.kellerTempC, ' °C', 1)} · ${Zeig(live.kellerRhProzent, ' %', 0)} → ${Zeig(live.kellerAbsolutGm3, ' g/m³', 2)}`}
          />
          <p className="zl-pfeil" aria-hidden="true">↓</p>
          <Glied
            marke="Differenz"
            wert={`${Zeig(live.differenzGm3, ' g/m³', 2)} · Schwelle ${entwurf.mindestDifferenzGm3.toLocaleString('de-DE')}`}
          />
          <p className="zl-pfeil" aria-hidden="true">↓</p>
          <Glied
            marke="Zelt"
            wert={`${Zeig(live.zeltTempC, ' °C', 1)} · Minimum ${Zeig(entwurf.zeltTemperaturMinC, ' °C', 1)}${live.pauseZeltKalt === true ? ' · Pause' : ''}`}
          />
          <p className="zl-pfeil" aria-hidden="true">↓</p>
                    <Glied ziel marke="Zielstufe" wert={live.zielstufe == null ? '–' : `${live.zielstufe} von ${entwurf.stufeMax}`} />
        </div>
        <p className="st-hinweis">
          Gerechnet wird mit absoluter Feuchte nach Magnus. Relative Prozente allein sagen nichts darüber,
          ob Außenluft trocknet: 88 % bei 12 °C tragen weniger Wasser als 57 % bei 22 °C.
        </p>
      </V1Sheet>
    </V1Page>
  )
}

/** Ein Messwert, wie er im Rechenweg steht — mit „–" statt einer erfundenen Null. */
function Zeig(wert: number | null, einheit: string, stellen: number) {
  return wert == null ? '–' : wert.toLocaleString('de-DE', { minimumFractionDigits: stellen, maximumFractionDigits: stellen }) + einheit
}

function Glied({ marke, wert, ziel }: { marke: string; wert: string; ziel?: boolean }) {
  return (
    <div className={ziel ? 'zl-glied ziel' : 'zl-glied'}>
      <span className="l">{marke}</span>
      <span className="r">{wert}</span>
    </div>
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
