import { SteuerungWechsel } from './SteuerungWechsel'
import { HaStummHinweis } from './HaStummHinweis'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { apiFetch, formatApiError } from '../../api'
import { V1Alert, V1Button, V1Card, V1LinkButton, V1Page, V1Section, V1Skeleton, V1Stat, V1Switch, V1Tabs } from '../../components/v1'
import { V1Sheet } from '../../components/V1Sheet'
import { ZULUFT_REITER } from './steuerung-typen'
import type { SteuerungModul, ZuluftEinstellungen, ZuluftReiter, ZuluftSeite } from './steuerung-typen'
import './steuerung.css'
import { rollenPfad } from '../geraete/rollenPfad'
import { feldFehlerAus, leereZahlenfelder, ohneLuecken, zahlAusFeld } from './feld-fehler'
import { useFehlerZeigen } from './fehler-reiter'
import { bandStellen, haeltGerade, haltebereich } from './zuluft-haltebereich'
import type { Haltebereich } from './zuluft-haltebereich'

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
  // Gesperrtes Speichern: auf den Reiter des markierten Felds wechseln und hinrollen.
  const fehlerZeigen = useFehlerZeigen(reiter, setReiter, ZULUFT_REITER)
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
    const leer = leereZahlenfelder(entwurf)
    if (leer) { setFeldFehler(leer); setMeldung(null); setFehler('Bitte die markierten Felder prüfen.'); fehlerZeigen(); return }
    setArbeitet(true); setMeldung(null); setFeldFehler({})
    try {
      const zurueck = await apiFetch<ZuluftSeite>('/api/steuerung/zuluft', { method: 'PUT', body: JSON.stringify(entwurf) })
      setSeite(zurueck); setEntwurf(zurueck.einstellungen); setFehler(null)
      setMeldung(zurueck.haAngenommen === false
        ? 'Gespeichert — aber nicht alle Helfer haben den Wert angenommen.'
        : 'Gespeichert.')
    } catch (caught) {
      const felder = feldFehlerAus(caught)
      if (felder) { setFeldFehler(felder); setFehler('Bitte die markierten Felder prüfen.'); fehlerZeigen() }
      else setFehler(formatApiError(caught, 'Speichern fehlgeschlagen.'))
    } finally {
      setArbeitet(false)
    }
  }

  if (laedt && !seite) return <V1Page eyebrow="Steuerung" title="Zuluft"><V1Skeleton rows={4} tiles={4} label="Wird geladen" /></V1Page>
  if (!seite || !entwurf) {
    return <V1Page eyebrow="Steuerung" title="Zuluft">{fehler && <V1Alert tone="critical" message={fehler} />}</V1Page>
  }

  const live = seite.live
  const anzeige = ohneLuecken(entwurf, seite.einstellungen)
  const setz = <K extends keyof ZuluftEinstellungen>(feld: K, wert: ZuluftEinstellungen[K]) => setEntwurf({ ...entwurf, [feld]: wert })

  // Die Regelung entscheidet nach der geglätteten Differenz und nach den GESPEICHERTEN Werten —
  // nicht nach dem, was gerade im Feld getippt wird. Das Band im Reiter Regel zeigt dagegen den Entwurf.
  const regelDiff = live.differenzRegelGm3 ?? live.differenzGm3
  const hbLive = haltebereich(seite.einstellungen.mindestDifferenzGm3, seite.einstellungen.abschaltAbstandGm3)
  const hbEntwurf = haltebereich(anzeige.mindestDifferenzGm3, anzeige.abschaltAbstandGm3)
  const haelt = live.portAn === true && haeltGerade(live.bedarf, regelDiff, hbLive)

  const zustand = live.portAn === true ? (haelt ? 'saugt · hält' : 'saugt') : live.bedarf === true ? 'wartet' : live.pauseZeltKalt === true ? 'pausiert' : 'bereit'

  return (
    <V1Page
      eyebrow="Steuerung"
      title="Zuluft"
      subtitle="Außenluft ansaugen, solange sie trockener ist — geregelt in Home Assistant"
      action={geaendert ? <V1Button variant="primary" onClick={speichern} disabled={arbeitet}>{arbeitet ? 'Speichert …' : 'Speichern'}</V1Button> : undefined}
      vorKopf={<SteuerungWechsel module={module} aktiv={aktiv} onWechsel={onWechsel} />}
    >

      {fehler && <V1Alert tone="critical" message={fehler} />}
      {meldung && <V1Alert tone={meldung === 'Gespeichert.' ? 'ok' : 'warn'} message={meldung} />}
      <HaStummHinweis haErreichbar={live.haErreichbar} eingerichtet={seite.geraeteZugeordnet > 0} />
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
          message={`Die Außenluft würde trocknen, aber das Zelt hat ${Zeig(live.zeltTempC, ' °C', 1)}. Die Zuluft läuft wieder ab ${Zeig(anzeige.zeltTemperaturMinC == null ? null : anzeige.zeltTemperaturMinC + 1, ' °C', 1)}; bis dahin entfeuchtet der Trotec allein.`}
        />
      )}

      <section className="v1-kpi-grid">
        <button type="button" className="zl-kachel" onClick={() => setRechenweg(true)}>
          <V1Stat
            label="Differenz"
            value={Zeig(regelDiff, '', 2)}
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
        <V1Stat
          label="Zustand"
          value={zustand}
          wortwert
          tone={live.portAn === true ? 'ok' : 'neutral'}
          hint={haelt ? `Aus erst unter ${Zeig(hbLive.ausUnter, '', 1)} g/m³` : null}
        />
        <V1Stat
          label="Schaltsperre"
          value={live.sperreRestMin == null ? '–' : live.sperreRestMin === 0 ? 'frei' : live.sperreRestMin}
          unit={live.sperreRestMin ? ' min' : null}
          wortwert={live.sperreRestMin === 0}
          hint={live.letzterWechsel ? `zuletzt ${new Date(live.letzterWechsel).toLocaleString('de-DE', { dateStyle: 'short', timeStyle: 'short' })}` : 'noch nie geschaltet'}
        />
      </section>

      <V1Tabs items={ZULUFT_REITER} active={reiter} onChange={setReiter} label="Bereich" insBild />

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
              label="Abschalt-Abstand"
              hinweis="Wie weit die Differenz unter die Mindest-Differenz fallen muss, bevor er aufhört (bei 0: an und aus an derselben Grenze)."
              einheit="g/m³"
              wert={entwurf.abschaltAbstandGm3}
              min={0}
              max={3}
              schritt={0.1}
              onChange={(v) => setz('abschaltAbstandGm3', v)}
              fehler={feldFehler.AbschaltAbstandGm3}
            />
            <HaltBand hb={hbEntwurf} differenz={regelDiff} />
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
              Zwischen „aus unter" und „an ab" bleibt der Lüfter, wie er ist. Das hält ihn davon ab, an
              der Grenze zu flattern; Mindestlaufzeit und -pause sichern zusätzlich.
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
        <V1LinkButton to={rollenPfad('zuluft')} variant="ghost">Rollen bearbeiten ›</V1LinkButton>
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
            wert={`${Zeig(regelDiff, ' g/m³', 2)}${live.differenzRegelGm3 != null && live.differenzRegelGm3 !== live.differenzGm3 ? ` · roh ${Zeig(live.differenzGm3, '', 2)}` : ''}`}
          />
          <p className="zl-pfeil" aria-hidden="true">↓</p>
          <Glied
            marke="Schwellen"
            wert={`an ab ${Zeig(hbLive.anAb, '', 1)} · aus unter ${Zeig(hbLive.ausUnter, '', 1)}${haelt ? ' · hält' : ''}`}
          />
          <p className="zl-pfeil" aria-hidden="true">↓</p>
          <Glied
            marke="Zelt"
            wert={`${Zeig(live.zeltTempC, ' °C', 1)} · Minimum ${Zeig(anzeige.zeltTemperaturMinC, ' °C', 1)}${live.pauseZeltKalt === true ? ' · Pause' : ''}`}
          />
          <p className="zl-pfeil" aria-hidden="true">↓</p>
                    <Glied ziel marke="Zielstufe" wert={live.zielstufe == null ? '–' : `${live.zielstufe} von ${anzeige.stufeMax}`} />
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
  return wert == null || !Number.isFinite(wert) ? '–' : wert.toLocaleString('de-DE', { minimumFractionDigits: stellen, maximumFractionDigits: stellen }) + einheit
}

/**
 * Fork AI (forkai.225): Die Hysterese als Band — wo der Lüfter angeht, wo er aufhört und was dazwischen
 * gilt. Der Punkt ist die Differenz, nach der die Regelung entscheidet. Farbe allein trägt nichts: die
 * Zonen haben Namen, und das Ganze liest sich auch als Satz (aria-label).
 */
function HaltBand({ hb, differenz }: { hb: Haltebereich; differenz: number | null }) {
  const s = bandStellen(hb, differenz)
  const gleich = hb.ausUnter === hb.anAb
  const satz = gleich
    ? `An und aus bei ${Zeig(hb.anAb, '', 1)} g/m³`
    : `Aus unter ${Zeig(hb.ausUnter, '', 1)}, an ab ${Zeig(hb.anAb, '', 1)} g/m³, dazwischen bleibt er, wie er ist`
  return (
    <div className="zl-band" role="img" aria-label={`${satz}${differenz == null ? '' : `; jetzt ${Zeig(differenz, '', 2)}`}`}>
      <div className="ef-skala">
        <div className="ef-bahn" />
        {!gleich && <div className="zl-halt" style={{ left: `${s.ausPos}%`, width: `${s.anPos - s.ausPos}%` }} />}
        <div className="zl-an" style={{ left: `${s.anPos}%` }} />
        {!gleich && <span className="ef-strich" style={{ left: `${s.ausPos}%` }} />}
        <span className="ef-strich" style={{ left: `${s.anPos}%` }} />
        {s.istPos != null && <span className="ef-ist" style={{ left: `${s.istPos}%` }}><em>{Zeig(differenz, '', 2)}</em></span>}
      </div>
      <div className="ef-marken">
        {gleich ? (
          <span style={{ left: `${s.anPos}%` }}>{Zeig(hb.anAb, '', 1)}<b>an und aus</b></span>
        ) : (
          <>
            <span style={{ left: `${s.ausPos}%`, transform: 'translateX(-85%)' }}>{Zeig(hb.ausUnter, '', 1)}<b>aus unter</b></span>
            <span style={{ left: `${s.anPos}%`, transform: 'translateX(-15%)' }}>{Zeig(hb.anAb, '', 1)}<b>an ab</b></span>
          </>
        )}
      </div>
      <p className="zl-legende">
        {gleich ? 'Ohne Haltebereich.' : <><span className="zl-punkt halt" aria-hidden="true" />hält, wie er ist <span className="zl-punkt an" aria-hidden="true" />saugt</>}
      </p>
    </div>
  )
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
          value={Number.isFinite(wert) ? wert : ''}
          onChange={(e) => {
            const neu = zahlAusFeld(e.target.value)
            if (neu != null) onChange(neu)
          }}
        />
        {einheit && <span className="st-einheit">{einheit}</span>}
      </span>
    </div>
  )
}
