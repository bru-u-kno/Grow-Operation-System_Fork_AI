import { useCallback, useEffect, useMemo, useState } from 'react'
import { apiFetch, formatApiError } from '../../api'
import { V1Alert, V1Button, V1Card, V1LinkButton, V1Page, V1Skeleton, V1Switch, V1Tabs } from '../../components/v1'
import { ENTFEUCHTER_REITER } from './steuerung-typen'
import type { EntfeuchterEinstellungen, EntfeuchterReiter, EntfeuchterSeite, SteuerungModul } from './steuerung-typen'
import { HYSTERESE_STUFEN, hystereseStufe, tempMax, zahl } from './entfeuchter-band'
import './steuerung.css'
import { rollenPfad } from '../geraete/rollenPfad'
import { feldFehlerAus, leereZahlenfelder, ohneLuecken } from './feld-fehler'
import { TempMaxBlock, Zahl } from './SteuerungsFelder'
import { Klappkachel } from './Klappkachel'
import { Lage, MessKopf, Warum, ZonenLegende, ZonenSkala, Zusammenspiel } from './EntfeuchterUeberblick'
import type { WarumZeile } from './EntfeuchterUeberblick'
import { ZONEN_WORT, feuchteZone, schlechtereZone, temperaturZone, RF_KNAPP_PUNKTE } from './entfeuchter-zonen'
import { useFehlerZeigen } from './fehler-reiter'

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
  const [reiter, setReiter] = useState<EntfeuchterReiter>('ueberblick')
  const [eigeneHysterese, setEigeneHysterese] = useState(false)
  const [fehler, setFehler] = useState<string | null>(null)
  const [feldFehler, setFeldFehler] = useState<Record<string, string>>({})
  // Gesperrtes Speichern: auf den Reiter des markierten Felds wechseln und hinrollen.
  const fehlerZeigen = useFehlerZeigen(reiter, setReiter, ENTFEUCHTER_REITER)
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
    const leer = leereZahlenfelder(entwurf)
    if (leer) { setFeldFehler(leer); setMeldung(null); setFehler('Bitte die markierten Felder prüfen.'); fehlerZeigen(); return }
    setArbeitet(true); setMeldung(null); setFeldFehler({})
    try {
      const zurueck = await apiFetch<EntfeuchterSeite>('/api/steuerung/entfeuchter', { method: 'PUT', body: JSON.stringify(entwurf) })
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

  if (laedt && !seite) return <V1Page eyebrow="Steuerung" title="Entfeuchter"><V1Skeleton rows={4} tiles={1} label="Wird geladen" /></V1Page>
  if (!seite || !entwurf) {
    return <V1Page eyebrow="Steuerung" title="Entfeuchter">{fehler && <V1Alert tone="critical" message={fehler} />}</V1Page>
  }

  const live = seite.live
  const anzeige = ohneLuecken(entwurf, seite.einstellungen)
  const setz = <K extends keyof EntfeuchterEinstellungen>(feld: K, wert: EntfeuchterEinstellungen[K]) => setEntwurf({ ...entwurf, [feld]: wert })

  const phase = live.tagPhase === true ? 'Tag' : live.tagPhase === false ? 'Nacht' : null
  const ausSpaetestens = live.einAktivProzent == null ? null : live.einAktivProzent - anzeige.hystereseProzent

  const tagMax = tempMax(anzeige.tempMaxTagModus, anzeige.tempMaxTagAbstandK, anzeige.tempMaxTagFestC, live.planLuftTagC)
  const nachtMax = tempMax(anzeige.tempMaxNachtModus, anzeige.tempMaxNachtAbstandK, anzeige.tempMaxNachtFestC, live.planLuftNachtC)
  const grenze = live.co2CanopyGrenzeC
  const ueberCo2 = grenze != null && (tagMax > grenze || nachtMax > grenze)

  // --- Zonen (A-014): Ziel der Luftfeuchte ist die Plan-Obergrenze, ohne Plan die EIN-Schwelle.
  const aktivMax = phase === 'Nacht' ? nachtMax : tagMax
  const feuchteZiel = live.rhObergrenzeProzent ?? live.einAktivProzent
  const fZone = feuchteZone(live.feuchteProzent, feuchteZiel)
  const tZone = temperaturZone(live.tempC, aktivMax)
  const lage = schlechtereZone(fZone, tZone)
  const lageText = [
    fZone && `Luftfeuchte ${zahl(live.feuchteProzent)} % (${ZONEN_WORT[fZone]}${feuchteZiel == null ? '' : `, Ziel ${zahl(feuchteZiel, 0)} %`})`,
    tZone && `Temperatur ${zahl(live.tempC)} °C (${ZONEN_WORT[tZone]}, Höchsttemperatur ${zahl(aktivMax)} °C)`,
  ].filter(Boolean).join(' · ')
  const lageTitel = live.portAn === true ? 'Entfeuchter läuft' : 'Entfeuchter bereit'

  const feuchteWerte = [live.feuchteProzent, feuchteZiel, live.einAktivProzent, live.ausAktivProzent].filter((w): w is number => w != null && Number.isFinite(w))
  const fVon = feuchteWerte.length ? Math.floor(Math.min(...feuchteWerte) - 3) : 40
  const fBis = feuchteWerte.length ? Math.ceil(Math.max(...feuchteWerte, (feuchteZiel ?? 0) + RF_KNAPP_PUNKTE) + 3) : 70
  const tVon = Math.floor(Math.min(live.tempC ?? aktivMax, aktivMax) - 4)
  const tBis = Math.ceil(Math.max(live.tempC ?? aktivMax, aktivMax) + 2)

  const warum: WarumZeile[] = [
    { frage: 'Feuchte über der EIN-Schwelle', antwort: `${zahl(live.feuchteProzent)} % · EIN ab ${zahl(live.einAktivProzent)} %`, ok: live.feuchteProzent == null || live.einAktivProzent == null ? null : live.feuchteProzent > live.einAktivProzent },
    { frage: `Zelt unter der Höchsttemperatur (${zahl(aktivMax)} °C)`, antwort: `${zahl(live.tempC)} °C`, ok: live.tempC == null ? null : live.tempC < aktivMax },
    ...(phase === 'Tag' ? [{ frage: 'Tagbetrieb erlaubt', antwort: anzeige.tagbetriebErlauben ? 'ja — er darf auch bei Licht an laufen' : 'nein — nur in der Dunkelphase', ok: anzeige.tagbetriebErlauben }] : []),
    { frage: 'Automatik an', antwort: live.automatikAn === false ? 'Die Regelung ist angehalten.' : 'Die Regelung läuft in Home Assistant.', ok: live.automatikAn == null ? null : live.automatikAn },
  ]

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

      <V1Tabs items={ENTFEUCHTER_REITER} active={reiter} onChange={setReiter} label="Bereich" insBild />

      {/* ---------------------------------------------------------- Überblick */}
      {reiter === 'ueberblick' && (
        <>
          {lage && <Lage zone={lage} titel={lageTitel} text={lageText} />}

          <Klappkachel
            titel="Messwerte & Zonen"
            zusammenfassung={`${zahl(live.feuchteProzent)} % rF · ${zahl(live.tempC)} °C${phase ? ` · ${phase}` : ''}`}
          >
            <V1Card className="ef-band">
              <div className="ef-block">
                <MessKopf
                  wert={zahl(live.feuchteProzent)}
                  einheit="% rF"
                  zone={fZone}
                  zustand={live.portAn === true ? 'entfeuchtet' : 'bereit'}
                  ton={live.portAn === true ? 'an' : undefined}
                  beiwerk={`VPD ${zahl(live.vpd, 2)}${phase ? ` · ${phase}` : ''}`}
                />
                {feuchteZiel != null && (
                  <ZonenSkala
                    von={fVon}
                    bis={fBis}
                    zielBis={feuchteZiel}
                    knappBis={feuchteZiel + RF_KNAPP_PUNKTE}
                    marken={[
                      ...(live.ausAktivProzent == null ? [] : [{ wert: live.ausAktivProzent, label: 'AUS' }]),
                      ...(live.einAktivProzent == null ? [] : [{ wert: live.einAktivProzent, label: 'EIN' }]),
                      { wert: feuchteZiel, label: 'Ziel' },
                    ]}
                    ist={live.feuchteProzent}
                    zone={fZone}
                    einheit="%"
                  />
                )}
              </div>
              <div className="ef-block">
                <MessKopf
                  wert={zahl(live.tempC)}
                  einheit="°C"
                  zone={tZone}
                  zustand={tZone === 'kritisch' ? 'zu warm — er ist aus' : tZone ? ZONEN_WORT[tZone] : 'keine Aussage'}
                  ton={tZone === 'kritisch' ? 'kritisch' : tZone === 'knapp' ? 'warn' : undefined}
                  beiwerk={`Höchsttemperatur ${zahl(aktivMax)} °C${phase ? ` · ${phase}` : ''}`}
                />
                <ZonenSkala
                  von={tVon}
                  bis={tBis}
                  zielBis={aktivMax - 1}
                  knappBis={aktivMax}
                  marken={[{ wert: aktivMax - 1, label: 'Ziel bis' }, { wert: aktivMax, label: 'Höchst' }]}
                  ist={live.tempC}
                  zone={tZone}
                  einheit="°C"
                />
              </div>
              <ZonenLegende />
              <p className="st-hinweis">
                {live.portAn === true
                  ? `Läuft, bis die Feuchte unter ${zahl(live.ausAktivProzent)} % fällt — frühestens nach ${anzeige.mindestlaufzeitMin} min Laufzeit.`
                  : `Springt an, wenn die Feuchte über ${zahl(live.einAktivProzent)} % steigt.`}
                {entwurf.vpdRegelung && live.vpdUnten != null && live.vpdOben != null
                  ? ` Schwellen aus dem VPD-Band ${zahl(live.vpdUnten, 2)}–${zahl(live.vpdOben, 2)}, EIN gedeckelt von der Plan-Feuchte.`
                  : ' Feste Schwellen (Rückfallebene).'}
              </p>
            </V1Card>
          </Klappkachel>

          <Warum an={live.portAn} zeilen={warum} />
          <Zusammenspiel haupt="Haupt-Entfeuchter" zusatz="Zusatz-Entfeuchter" tempMax={aktivMax} />
        </>
      )}

      {/* -------------------------------------------------------------- Regel */}
      {reiter === 'regel' && (
        <>
          <Klappkachel
            titel={live.planWoche ? `Aus dem Plan · ${live.planWoche}` : 'Aus dem Plan'}
            zusammenfassung={`VPD ${live.vpdUnten == null ? '–' : `${zahl(live.vpdUnten, 2)}–${zahl(live.vpdOben, 2)} kPa`} · Feuchte max. ${live.rhObergrenzeProzent == null ? '–' : `${zahl(live.rhObergrenzeProzent, 0)} %`}`}
            offen={false}
          >
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
          </Klappkachel>

          <Klappkachel titel="Regelart" zusammenfassung={entwurf.vpdRegelung ? 'nach VPD' : 'feste Schwellen'}>
            <V1Card>
              <V1Switch
                className="ef-schalter"
                label="Nach VPD regeln"
                checked={entwurf.vpdRegelung}
                onChange={(an) => setz('vpdRegelung', an)}
                hint={
                  <>
                    <span className="ef-zeile"><b>An:</b> Die Schwellen wandern selbst mit Temperatur und VPD-Band aus dem Plan.</span>
                    <span className="ef-zeile"><b>Aus:</b> Es gelten die festen Schwellen weiter unten.</span>
                  </>
                }
              />
            </V1Card>
          </Klappkachel>

          <Klappkachel titel="Wie ruhig schaltet er?" zusammenfassung={`Abstand EIN → AUS ${zahl(entwurf.hystereseProzent, 0)} %`}>
            <V1Card>
              <div className="ef-ruhig">
                <p className="st-hinweis">
                  So weit muss die Feuchte unter die Einschaltschwelle fallen, bevor er ausgeht. Gilt in beiden Regelarten.
                  {feldFehler.HystereseProzent && <span className="st-fehler">{feldFehler.HystereseProzent}</span>}
                </p>
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
                <p className="ef-folge">
                  Heißt jetzt: EIN ab {zahl(live.einAktivProzent)} %, AUS spätestens bei {zahl(ausSpaetestens)} %
                  {live.ausAktivProzent != null && ausSpaetestens != null && live.ausAktivProzent < ausSpaetestens - 0.05
                    ? ` (liegt das VPD-Ziel tiefer, gilt das: gerade ${zahl(live.ausAktivProzent)} %)`
                    : ''}
                  . Knapp hält die Feuchte enger, schaltet aber öfter. Ruhig schont den Kompressor.
                </p>
              </div>
            </V1Card>
          </Klappkachel>

          <Klappkachel
            titel="Feste Schwellen"
            zusammenfassung={entwurf.vpdRegelung ? 'ruhen, solange „Nach VPD regeln" an ist' : `Tag EIN ${zahl(entwurf.feuchteEinTag, 0)} % · AUS ${zahl(entwurf.feuchteAusTag, 0)} %`}
            offen={!entwurf.vpdRegelung}
          >
            <V1Card>
              {entwurf.vpdRegelung && (
                <p className="st-hinweis st-ruht-hinweis">Gerade ohne Wirkung: „Nach VPD regeln" ist an. Diese Werte gelten nur als Rückfallebene — wenn du es ausschaltest oder der Plan keine VPD-Werte liefert. Auch dann deckelt die Plan-Feuchte die EIN-Schwelle.</p>
              )}
              <Zahl ruht={entwurf.vpdRegelung} label="Tag · EIN ab" hinweis="Licht an." einheit="%" wert={entwurf.feuchteEinTag} min={30} max={90} schritt={1} onChange={(v) => setz('feuchteEinTag', v)} fehler={feldFehler.FeuchteEinTag} />
              <Zahl ruht={entwurf.vpdRegelung} label="Tag · AUS unter" hinweis="Muss unter EIN liegen." einheit="%" wert={entwurf.feuchteAusTag} min={30} max={90} schritt={1} onChange={(v) => setz('feuchteAusTag', v)} fehler={feldFehler.FeuchteAusTag} />
              <Zahl ruht={entwurf.vpdRegelung} label="Nacht · EIN ab" hinweis="Licht aus." einheit="%" wert={entwurf.feuchteEinNacht} min={30} max={90} schritt={1} onChange={(v) => setz('feuchteEinNacht', v)} fehler={feldFehler.FeuchteEinNacht} />
              <Zahl ruht={entwurf.vpdRegelung} label="Nacht · AUS unter" hinweis="Muss unter EIN liegen." einheit="%" wert={entwurf.feuchteAusNacht} min={30} max={90} schritt={1} onChange={(v) => setz('feuchteAusNacht', v)} fehler={feldFehler.FeuchteAusNacht} />
            </V1Card>
          </Klappkachel>
        </>
      )}

      {/* ------------------------------------------------------------- Schutz */}
      {reiter === 'schutz' && (
        <>
          <Klappkachel titel="Temperatur max. · darüber geht er aus" zusammenfassung={`Tag ${zahl(tagMax)} °C · Nacht ${zahl(nachtMax)} °C`}>
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
                fehlerAbstand={feldFehler.TempMaxTagAbstandK}
                fehlerFest={feldFehler.TempMaxTagFestC}
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
                fehlerAbstand={feldFehler.TempMaxNachtAbstandK}
                fehlerFest={feldFehler.TempMaxNachtFestC}
              />
              {ueberCo2 && (
                <V1Alert tone="warn" message={`Liegt über der CO₂-Grenze von ${zahl(grenze)} °C — dann steigt der Entfeuchter erst nach der CO₂-Klimasperre aus.`} />
              )}
              <p className="st-hinweis">Kein Pflanzenziel, sondern Geräteschutz: der Entfeuchter gibt selbst Wärme ab. Die Höchsttemperatur gilt für beide Entfeuchter.</p>
            </V1Card>
          </Klappkachel>

          <Klappkachel titel="Laufverhalten" zusammenfassung={`Mindestlaufzeit ${anzeige.mindestlaufzeitMin} min`} offen={false}>
            <V1Card>
              <Zahl label="Mindestlaufzeit" hinweis="Vorher schaltet ihn erreichte Feuchte nicht ab. Übertemperatur schon." einheit="min" wert={entwurf.mindestlaufzeitMin} min={0} max={60} schritt={1} onChange={(v) => setz('mindestlaufzeitMin', Math.round(v))} fehler={feldFehler.MindestlaufzeitMin} />
              <V1Switch
                label="Auch tagsüber entfeuchten"
                checked={entwurf.tagbetriebErlauben}
                onChange={(an) => setz('tagbetriebErlauben', an)}
                hint="Aus: nur in der Dunkelphase."
              />
            </V1Card>
          </Klappkachel>
        </>
      )}

      {/* ------------------------------------------------------------ Betrieb */}
      {reiter === 'betrieb' && (
        <>
          <Klappkachel titel="Betrieb" zusammenfassung={`Automatik ${entwurf.automatikAktiv ? 'an' : 'aus'} · Einschaltverzögerung ${anzeige.einschaltverzoegerungMin} min`}>
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
                  ? `Gerade: Zuluft trocknet → es gelten ${anzeige.wartezeitAussenluftMin} min.`
                  : live.zuluftVorrang === false
                    ? `Gerade: Außenluft bringt nichts → es gelten ${anzeige.einschaltverzoegerungMin} min.`
                    : 'Ob die Zuluft gerade trocknet, ist nicht bekannt.'}
                {' '}Ob die Zuluft trocknet, entscheidet die Zuluft-Steuerung.
              </p>
            </V1Card>
          </Klappkachel>

          <Klappkachel titel="Gerät" zusammenfassung={live.portOnline === false ? 'Port offline' : live.portAn === true ? 'läuft' : 'steht'} offen={false}>
            <V1Card>
              <div className="st-feldzeile">
                <span className="st-etikett">
                  Entfeuchter
                  <small>{live.portOnline === false ? 'Port offline' : live.portAn === true ? 'läuft' : 'steht'}</small>
                </span>
                <span className="st-nurlesen">{live.portAn === true ? 'AN' : live.portAn === false ? 'AUS' : '–'}</span>
              </div>
              <p className="st-hinweis">Geschaltet wird in Home Assistant, nicht hier — die Regelung läuft weiter, wenn der Fork neu startet.</p>
            </V1Card>
          </Klappkachel>
        </>
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
