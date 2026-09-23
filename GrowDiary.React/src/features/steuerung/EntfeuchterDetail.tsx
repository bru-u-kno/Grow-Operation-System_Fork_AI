import { useCallback, useEffect, useMemo, useState } from 'react'
import { apiFetch, formatApiError } from '../../api'
import { V1Alert, V1Button, V1Card, V1LinkButton, V1Page, V1Section, V1Skeleton, V1Switch, V1Tabs } from '../../components/v1'
import { ENTFEUCHTER_REITER } from './steuerung-typen'
import type { EntfeuchterEinstellungen, EntfeuchterReiter, EntfeuchterSeite, SteuerungModul, TempMaxModus } from './steuerung-typen'
import { HYSTERESE_STUFEN, bandBerechnen, hystereseStufe, tempMax, zahl } from './entfeuchter-band'
import './steuerung.css'
import { rollenPfad } from '../geraete/rollenPfad'

/**
 * Fork AI (forkai.129, F-023): Steuerung › Entfeuchter — Mockup Stand 3,
 * freigegeben von Bru am 21.09.2026.
 *
 * <b>Pflanze oder Maschine.</b> Was die Pflanze braucht (VPD-Band, Feuchte
 * max., Blatt-Offset) steht hier nur zum Lesen und wird unter „Ziele &
 * Meldungen" gepflegt. Einstellbar ist, wie das Gerät arbeitet.
 *
 * <b>Warum oben ein Band statt Kacheln.</b> Man soll auf einen Blick sehen,
 * WARUM er läuft: wo die Feuchte gerade zu EIN, AUS und dem Plan-Deckel steht.
 * Das Band zeigt nur den Ausschnitt, auf den es ankommt (entfeuchter-band.ts).
 *
 * <b>Was hier NICHT geschaltet wird.</b> Die Regelung läuft in Home Assistant.
 */
export default function EntfeuchterDetail({ module, aktiv, onWechsel }: {
  module: SteuerungModul[]
  aktiv: string
  onWechsel: (kennung: string) => void
}) {
  const [seite, setSeite] = useState<EntfeuchterSeite | null>(null)
  const [entwurf, setEntwurf] = useState<EntfeuchterEinstellungen | null>(null)
  const [reiter, setReiter] = useState<EntfeuchterReiter>('regel')
  const [rueckfallOffen, setRueckfallOffen] = useState(false)
  const [eigeneHysterese, setEigeneHysterese] = useState(false)
  const [fehler, setFehler] = useState<string | null>(null)
  const [feldFehler, setFeldFehler] = useState<Record<string, string>>({})
  const [meldung, setMeldung] = useState<string | null>(null)
  const [laedt, setLaedt] = useState(true)
  const [arbeitet, setArbeitet] = useState(false)

  const auffrischen = useCallback(async () => {
    try {
      const geladen = await apiFetch<EntfeuchterSeite>('/api/steuerung/entfeuchter')
      setSeite(geladen)
      // Einen angefangenen Entwurf nicht überschreiben.
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
        const geladen = await apiFetch<EntfeuchterSeite>('/api/steuerung/entfeuchter', { signal: controller.signal })
        if (!controller.signal.aborted) {
          setSeite(geladen)
          setEntwurf(geladen.einstellungen)
          setEigeneHysterese(hystereseStufe(geladen.einstellungen.hystereseProzent) == null)
          setFehler(null)
        }
      } catch (caught) {
        if (!controller.signal.aborted) setFehler(formatApiError(caught, 'Die Entfeuchter-Steuerung konnte nicht geladen werden.'))
      } finally {
        if (!controller.signal.aborted) setLaedt(false)
      }
    }
    void laden()
    return () => controller.abort()
  }, [])

  // Eine Minute Takt: die Regelung selbst prüft alle fünf Minuten.
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
      const zurueck = await apiFetch<EntfeuchterSeite>('/api/steuerung/entfeuchter', { method: 'PUT', body: JSON.stringify(entwurf) })
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

  if (laedt && !seite) return <V1Page eyebrow="Steuerung" title="Entfeuchter"><V1Skeleton rows={4} tiles={1} label="Wird geladen" /></V1Page>
  if (!seite || !entwurf) {
    return <V1Page eyebrow="Steuerung" title="Entfeuchter">{fehler && <V1Alert tone="critical" message={fehler} />}</V1Page>
  }

  const live = seite.live
  const setz = <K extends keyof EntfeuchterEinstellungen>(feld: K, wert: EntfeuchterEinstellungen[K]) => setEntwurf({ ...entwurf, [feld]: wert })

  const band = bandBerechnen({ aus: live.ausAktivProzent, ein: live.einAktivProzent, deckel: live.rhObergrenzeProzent, ist: live.feuchteProzent })
  const phase = live.tagPhase === true ? 'Tag' : live.tagPhase === false ? 'Nacht' : null
  const ausSpaetestens = live.einAktivProzent == null ? null : live.einAktivProzent - entwurf.hystereseProzent

  const tagMax = tempMax(entwurf.tempMaxTagModus, entwurf.tempMaxTagAbstandK, entwurf.tempMaxTagFestC, live.planLuftTagC)
  const nachtMax = tempMax(entwurf.tempMaxNachtModus, entwurf.tempMaxNachtAbstandK, entwurf.tempMaxNachtFestC, live.planLuftNachtC)
  const grenze = live.co2CanopyGrenzeC
  const ueberCo2 = grenze != null && (tagMax > grenze || nachtMax > grenze)

  return (
    <V1Page
      eyebrow="Steuerung"
      title="Entfeuchter"
      subtitle="Trotec am RDWC-Zelt — geregelt in Home Assistant"
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
      {live.portOnline === false && <V1Alert tone="warn" title="Controller offline" message="Der Port meldet sich nicht. Geschaltet wird erst, wenn er wieder da ist." />}
      {live.automatikAn === false && (
        <V1Alert tone="warn" title="Automatik aus" message="Die Regelung ist angehalten. Der Entfeuchter bleibt, wie er gerade steht." />
      )}

      {/* ---------------------------------------------------- Schwellen-Band */}
      <V1Card className="ef-band">
        <div className="ef-kopf">
          <span className="ef-gross">{zahl(live.feuchteProzent)}<small> % rF</small></span>
          <span className="ef-zustand">
            <b className={live.portAn === true ? 'is-an' : undefined}>{live.portAn === true ? 'entfeuchtet' : 'bereit'}</b>
            VPD {zahl(live.vpd, 2)} · {zahl(live.tempC)} °C{phase ? ` · ${phase}` : ''}
          </span>
        </div>
        {band && (
          <>
            <div className="ef-skala" aria-hidden="true">
              <div className="ef-bahn" />
              {band.zoneAb != null && <div className="ef-zone" style={{ left: `${band.zoneAb}%` }} />}
              {band.marken.map((m) => (
                <div key={m.art} className={m.art === 'deckel' ? 'ef-strich is-deckel' : 'ef-strich'} style={{ left: `${m.pos}%` }} />
              ))}
              {band.ist != null && (
                <div className="ef-ist" style={{ left: `${band.ist}%` }}><em>{zahl(live.feuchteProzent)}</em></div>
              )}
            </div>
            <div className="ef-marken">
              {band.marken.map((m) => (
                <span key={m.art} className={m.art === 'deckel' ? 'is-deckel' : undefined} style={{ left: `${m.pos}%` }}>
                  {zahl(m.wert, 0)}<b>{m.art === 'aus' ? 'AUS' : m.art === 'ein' ? 'EIN' : 'Plan'}</b>
                </span>
              ))}
            </div>
            <div className="ef-rand"><span>{band.von} %</span><span>{band.bis} %</span></div>
          </>
        )}
        <p className="st-hinweis">
          {live.portAn === true
            ? `Läuft, bis die Feuchte unter ${zahl(live.ausAktivProzent)} % fällt — frühestens nach ${entwurf.mindestlaufzeitMin} min Laufzeit.`
            : `Springt an, wenn die Feuchte über ${zahl(live.einAktivProzent)} % steigt.`}
          {entwurf.vpdRegelung && live.vpdUnten != null && live.vpdOben != null
            ? ` Schwellen aus dem VPD-Band ${zahl(live.vpdUnten, 2)}–${zahl(live.vpdOben, 2)}, EIN gedeckelt von der Plan-Feuchte.`
            : ' Feste Schwellen (Rückfallebene).'}
        </p>
      </V1Card>

      <V1Tabs items={ENTFEUCHTER_REITER} active={reiter} onChange={setReiter} label="Bereich" />

      {/* -------------------------------------------------------------- Regel */}
      {reiter === 'regel' && (
        <>
          <V1Section title={live.planWoche ? `Aus dem Plan · ${live.planWoche}` : 'Aus dem Plan'}>
            <V1Card>
              <Lesen label="VPD-Band" herkunft="aus dem Plan" wert={live.vpdUnten == null ? '–' : `${zahl(live.vpdUnten, 2)} – ${zahl(live.vpdOben, 2)} kPa`} />
              <Lesen
                label="Luftfeuchte max."
                hinweis={live.deckelProzent == null ? undefined : `Deckel für EIN: ${zahl(live.rhObergrenzeProzent, 0)} % − Klima-Abstand = ${zahl(live.deckelProzent, 0)} %`}
                herkunft="aus dem Plan"
                wert={live.rhObergrenzeProzent == null ? '–' : `${zahl(live.rhObergrenzeProzent, 0)} %`}
              />
              <Lesen label="Blatt-Offset" herkunft="vom Zelt" wert={live.blattOffsetC == null ? '–' : `${zahl(live.blattOffsetC)} °C`} />
              <div className="st-feldzeile">
                <span className="st-etikett">Ändern im Plan</span>
                <V1LinkButton to="/plan" variant="ghost">Plan ›</V1LinkButton>
              </div>
            </V1Card>
          </V1Section>

          <V1Section title="Gerät">
            <V1Card>
              <V1Switch
                label="Nach VPD regeln"
                checked={entwurf.vpdRegelung}
                onChange={(an) => setz('vpdRegelung', an)}
                hint="An: die Schwellen wandern mit Temperatur und VPD-Band. Aus: die festen Schwellen unten gelten."
              />
              <div className="st-feldzeile is-gestapelt">
                <span className="st-etikett">
                  Wie ruhig soll er schalten?
                  <small>Wie weit die Feuchte unter die Einschaltschwelle fallen muss, bevor er ausgeht.</small>
                  {feldFehler.HystereseProzent && <span className="st-fehler">{feldFehler.HystereseProzent}</span>}
                </span>
                <div className="ef-stufen" role="radiogroup" aria-label="Abstand EIN → AUS">
                  {HYSTERESE_STUFEN.map((s) => (
                    <button
                      key={s.wert}
                      type="button"
                      role="radio"
                      className="st-chip"
                      aria-checked={!eigeneHysterese && entwurf.hystereseProzent === s.wert}
                      aria-current={!eigeneHysterese && entwurf.hystereseProzent === s.wert}
                      onClick={() => { setEigeneHysterese(false); setz('hystereseProzent', s.wert) }}
                    >
                      {s.label} · {s.wert} %
                    </button>
                  ))}
                  <button
                    type="button"
                    role="radio"
                    className="st-chip"
                    aria-checked={eigeneHysterese}
                    aria-current={eigeneHysterese}
                    onClick={() => setEigeneHysterese(true)}
                  >
                    eigener Wert
                  </button>
                </div>
                {eigeneHysterese && (
                  <div className="ef-unterfeld">
                    <Zahl label="Eigener Abstand" hinweis="1 bis 10 %." einheit="%" wert={entwurf.hystereseProzent} min={1} max={10} schritt={0.5} onChange={(v) => setz('hystereseProzent', v)} />
                  </div>
                )}
                <p className="st-hinweis">
                  Heißt jetzt: EIN ab {zahl(live.einAktivProzent)} %, AUS spätestens bei {zahl(ausSpaetestens)} %
                  {live.ausAktivProzent != null && ausSpaetestens != null && live.ausAktivProzent < ausSpaetestens - 0.05
                    ? ` (liegt das VPD-Ziel tiefer, gilt das: gerade ${zahl(live.ausAktivProzent)} %)`
                    : ''}
                  . Knapp hält die Feuchte enger, schaltet aber öfter. Ruhig schont den Kompressor.
                </p>
              </div>

              <button type="button" className="ef-klapp" aria-expanded={rueckfallOffen} onClick={() => setRueckfallOffen(!rueckfallOffen)}>
                <span><b>Feste Schwellen</b> · Rückfallebene{entwurf.vpdRegelung ? ', ruht' : ', gilt gerade'}</span>
                <span className="ef-klapp-zeichen">{rueckfallOffen ? 'zuklappen ▴' : 'aufklappen ▾'}</span>
              </button>
              {rueckfallOffen && (
                <>
                  <p className="st-hinweis">Gilt nur, wenn „Nach VPD regeln" aus ist oder der Plan keine VPD-Werte liefert. Auch hier deckelt die Plan-Feuchte die EIN-Schwelle.</p>
                  <Zahl label="Tag · EIN ab" hinweis="Licht an." einheit="%" wert={entwurf.feuchteEinTag} min={30} max={90} schritt={1} onChange={(v) => setz('feuchteEinTag', v)} fehler={feldFehler.FeuchteEinTag} />
                  <Zahl label="Tag · AUS unter" hinweis="Muss unter EIN liegen." einheit="%" wert={entwurf.feuchteAusTag} min={30} max={90} schritt={1} onChange={(v) => setz('feuchteAusTag', v)} fehler={feldFehler.FeuchteAusTag} />
                  <Zahl label="Nacht · EIN ab" hinweis="Licht aus." einheit="%" wert={entwurf.feuchteEinNacht} min={30} max={90} schritt={1} onChange={(v) => setz('feuchteEinNacht', v)} fehler={feldFehler.FeuchteEinNacht} />
                  <Zahl label="Nacht · AUS unter" hinweis="Muss unter EIN liegen." einheit="%" wert={entwurf.feuchteAusNacht} min={30} max={90} schritt={1} onChange={(v) => setz('feuchteAusNacht', v)} fehler={feldFehler.FeuchteAusNacht} />
                </>
              )}
            </V1Card>
          </V1Section>
        </>
      )}

      {/* ------------------------------------------------------------- Schutz */}
      {reiter === 'schutz' && (
        <>
          <V1Section title="Temperatur max. · darüber geht er aus">
            <V1Card>
              <TempMaxBlock
                titel="Tag"
                planText={live.planLuftTagC == null ? 'Kein Plan-Wert — es gilt der feste Wert.' : `Plan-Luft: ${zahl(live.planLuftTagC)} °C`}
                modus={entwurf.tempMaxTagModus}
                abstand={entwurf.tempMaxTagAbstandK}
                fest={entwurf.tempMaxTagFestC}
                plan={live.planLuftTagC}
                ergebnis={tagMax}
                onModus={(m) => setz('tempMaxTagModus', m)}
                onAbstand={(v) => setz('tempMaxTagAbstandK', v)}
                onFest={(v) => setz('tempMaxTagFestC', v)}
                fehler={feldFehler.TempMaxTagAbstandK ?? feldFehler.TempMaxTagFestC}
              />
              <TempMaxBlock
                titel="Nacht"
                planText={live.planLuftNachtC == null ? 'Kein Plan-Wert — es gilt der feste Wert.' : `Plan-Luft Nacht: ${zahl(live.planLuftNachtC)} °C`}
                modus={entwurf.tempMaxNachtModus}
                abstand={entwurf.tempMaxNachtAbstandK}
                fest={entwurf.tempMaxNachtFestC}
                plan={live.planLuftNachtC}
                ergebnis={nachtMax}
                onModus={(m) => setz('tempMaxNachtModus', m)}
                onAbstand={(v) => setz('tempMaxNachtAbstandK', v)}
                onFest={(v) => setz('tempMaxNachtFestC', v)}
                fehler={feldFehler.TempMaxNachtAbstandK ?? feldFehler.TempMaxNachtFestC}
              />
              {ueberCo2 && (
                <V1Alert tone="warn" message={`Liegt über der CO₂-Grenze von ${zahl(grenze)} °C — dann steigt der Entfeuchter erst nach der CO₂-Klimasperre aus.`} />
              )}
              <p className="st-hinweis">Kein Pflanzenziel, sondern Geräteschutz: der Entfeuchter gibt selbst Wärme ab.</p>
            </V1Card>
          </V1Section>

          <V1Section title="Laufverhalten">
            <V1Card>
              <Zahl label="Mindestlaufzeit" hinweis="Vorher schaltet ihn erreichte Feuchte nicht ab. Übertemperatur schon." einheit="min" wert={entwurf.mindestlaufzeitMin} min={0} max={60} schritt={1} onChange={(v) => setz('mindestlaufzeitMin', Math.round(v))} fehler={feldFehler.MindestlaufzeitMin} />
              <V1Switch
                label="Auch tagsüber entfeuchten"
                checked={entwurf.tagbetriebErlauben}
                onChange={(an) => setz('tagbetriebErlauben', an)}
                hint="Aus: nur in der Dunkelphase."
              />
            </V1Card>
          </V1Section>
        </>
      )}

      {/* ------------------------------------------------------------ Betrieb */}
      {reiter === 'betrieb' && (
        <V1Section title="Betrieb">
          <V1Card>
            <V1Switch
              label="Automatik aktiv"
              checked={entwurf.automatikAktiv}
              onChange={(an) => setz('automatikAktiv', an)}
              hint="Aus hält die Regelung an. Der Entfeuchter bleibt, wie er gerade steht."
            />
            <Zahl label="Einschaltverzögerung" hinweis="So lange muss die Feuchte über EIN liegen, bevor er anspringt — fängt kurze Spitzen ab (Zelt offen, Gießen)." einheit="min" wert={entwurf.einschaltverzoegerungMin} min={0} max={60} schritt={1} onChange={(v) => setz('einschaltverzoegerungMin', Math.round(v))} fehler={feldFehler.EinschaltverzoegerungMin} />
            <Zahl label="Außenluft zuerst" hinweis="Trocknet die Zuluft gerade, wartet er stattdessen so lange — die Außenluft bekommt ihre Chance." einheit="min" wert={entwurf.wartezeitAussenluftMin} min={0} max={120} schritt={1} onChange={(v) => setz('wartezeitAussenluftMin', Math.round(v))} fehler={feldFehler.WartezeitAussenluftMin} />
            <p className="st-hinweis">
              {live.zuluftVorrang === true
                ? `Gerade: Zuluft trocknet → es gelten ${entwurf.wartezeitAussenluftMin} min.`
                : live.zuluftVorrang === false
                  ? `Gerade: Außenluft bringt nichts → es gelten ${entwurf.einschaltverzoegerungMin} min.`
                  : 'Ob die Zuluft gerade trocknet, ist nicht bekannt.'}
              {' '}Ob die Zuluft trocknet, entscheidet die Zuluft-Steuerung.
            </p>
            <div className="st-feldzeile">
              <span className="st-etikett">
                Entfeuchter
                <small>{live.portOnline === false ? 'Port offline' : live.portAn === true ? 'läuft' : 'steht'}</small>
              </span>
              <span className="st-nurlesen">{live.portAn === true ? 'AN' : live.portAn === false ? 'AUS' : '–'}</span>
            </div>
            <p className="st-hinweis">Geschaltet wird in Home Assistant, nicht hier — die Regelung läuft weiter, wenn der Fork neu startet.</p>
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
        <V1LinkButton to={rollenPfad('entfeuchter')} variant="ghost">Rollen bearbeiten ›</V1LinkButton>
      </div>
    </V1Page>
  )
}

/** Ein Wert aus dem Plan — nur lesen, mit Herkunft. */
function Lesen({ label, hinweis, herkunft, wert }: { label: string; hinweis?: string; herkunft: string; wert: string }) {
  return (
    <div className="st-feldzeile">
      <span className="st-etikett">
        {label}
        {hinweis && <small>{hinweis}</small>}
        <small className="ef-herkunft">{herkunft}</small>
      </span>
      <span className="st-nurlesen">{wert}</span>
    </div>
  )
}

/** Temperatur max. für Tag oder Nacht: „Plan +" Abstand oder „Fest". */
function TempMaxBlock({ titel, planText, modus, abstand, fest, plan, ergebnis, onModus, onAbstand, onFest, fehler }: {
  titel: string
  planText: string
  modus: TempMaxModus
  abstand: number
  fest: number
  plan: number | null
  ergebnis: number
  onModus: (m: TempMaxModus) => void
  onAbstand: (v: number) => void
  onFest: (v: number) => void
  fehler?: string
}) {
  return (
    <div className="ef-tempmax">
      <div className="st-feldzeile">
        <span className="st-etikett">
          {titel}
          <small>{planText}</small>
          {fehler && <span className="st-fehler">{fehler}</span>}
        </span>
        <span className="ef-stufen" role="radiogroup" aria-label={`Temperatur max. ${titel}`}>
          <button type="button" role="radio" className="st-chip" aria-checked={modus === 'plan'} aria-current={modus === 'plan'} onClick={() => onModus('plan')}>Plan +</button>
          <button type="button" role="radio" className="st-chip" aria-checked={modus === 'fest'} aria-current={modus === 'fest'} onClick={() => onModus('fest')}>Fest</button>
        </span>
      </div>
      {modus === 'plan' ? (
        <>
          <Zahl label="Abstand zum Plan" hinweis="Wandert mit der Planwoche." einheit="K" wert={abstand} min={0} max={15} schritt={0.5} onChange={onAbstand} />
          <p className="ef-formel">
            {plan == null ? `Ohne Plan: fester Wert ${zahl(fest)} °C` : `${zahl(plan)} + ${zahl(abstand)} = `}
            {plan != null && <b>{zahl(ergebnis)} °C</b>}
          </p>
        </>
      ) : (
        <Zahl label="Fester Wert" hinweis="Bleibt, egal welche Woche." einheit="°C" wert={fest} min={15} max={35} schritt={0.5} onChange={onFest} />
      )}
    </div>
  )
}

/** Ein Zahlenfeld im Box-Modus — kein Schieberegler, am Telefon trifft man damit keinen Wert. */
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
