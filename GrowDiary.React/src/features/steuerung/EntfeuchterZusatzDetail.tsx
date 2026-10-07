import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { apiFetch, formatApiError } from '../../api'
import { V1Alert, V1Button, V1Card, V1LinkButton, V1Page, V1Skeleton, V1Switch, V1Tabs } from '../../components/v1'
import { rollenPfad } from '../geraete/rollenPfad'
import { tempMax, zahl } from './entfeuchter-band'
import {
  HILFE_STUFEN,
  aenderungBilden,
  aenderungsListe,
  ablaufText,
  entwurfNachfuehren,
  ersteBand,
  fehlerZu,
  gleich,
  hilfeNachEinzelwert,
  hilfeText,
  hilfeWaehlen,
  hoechstEmpfehlung,
  istGeaendert,
  leereFelder,
  ohneLueckenZusatz,
  statusHinweis,
  temperaturBand,
  tempGrenzen,
  unbekannteFehler,
  planHinweisZeigen,
  zustandWort,
} from './entfeuchter-zusatz'
import type { ZusatzBand } from './entfeuchter-zusatz'
import { feldFehlerAus } from './feld-fehler'
import { useFehlerZeigen } from './fehler-reiter'
import { TempMaxBlock, Zahl } from './SteuerungsFelder'
import type { Empfehlung } from './SteuerungsFelder'
import { ENTFEUCHTER_REITER } from './steuerung-typen'
import type { EntfeuchterReiter, EntfeuchterZusatzEinstellungen, EntfeuchterZusatzSeite, SteuerungModul, ZusatzAblauf, ZusatzMeldung } from './steuerung-typen'
import { Klappkachel } from './Klappkachel'
import { Lage, MessKopf, Warum, ZonenLegende, ZonenSkala, Zusammenspiel } from './EntfeuchterUeberblick'
import type { WarumZeile } from './EntfeuchterUeberblick'
import { RF_KNAPP_PUNKTE, ZONEN_WORT, feuchteZone, schlechtereZone, temperaturZone } from './entfeuchter-zonen'
import './steuerung.css'

type Einstellungen = EntfeuchterZusatzEinstellungen

/** Die drei Stufen für „Wie ruhig soll er schalten?" (VPD-Abstand, kPa). */
const VPD_STUFEN: ReadonlyArray<{ wert: number; label: string }> = [
  { wert: 0.1, label: 'knapp' },
  { wert: 0.15, label: 'normal' },
  { wert: 0.25, label: 'ruhig' },
]

/**
 * A-009: Steuerung › Zusatz-Entfeuchter — Mockup freigegeben von Bru am
 * 06.10.2026 (`archiv/a009/mockup-quelle.html`).
 *
 * <b>Führen und Folgen.</b> Der Zusatz (Shelly-Steckdose) hilft dem führenden
 * Gerät von der Seite „Entfeuchter": er geht früher aus und schaltet erst zu,
 * wenn die Führung eine Weile läuft. Geregelt wird in Home Assistant, nicht hier.
 *
 * <b>Einfach oben, Einzelwerte unten.</b> Sichtbar sind nur Höchsttemperatur,
 * Hilfsstärke und Automatik; alles Einzelne steht unter „Erweitert".
 *
 * <b>Speichern schreibt nur, was geändert wurde.</b> Der gelbe Kasten kündigt
 * vorher genau diese Felder an; `aenderungBilden` baut aus demselben Vergleich
 * den PUT-Körper (Anlass: Speichern hat am 06.10.2026 unbemerkt die Tag-Grenze
 * von 26,5 auf 29 °C zurückgesetzt).
 */
export default function EntfeuchterZusatzDetail({ module, aktiv, onWechsel }: {
  module: SteuerungModul[]
  aktiv: string
  onWechsel: (kennung: string) => void
}) {
  const [seite, setSeite] = useState<EntfeuchterZusatzSeite | null>(null)
  const [entwurf, setEntwurf] = useState<Einstellungen | null>(null)
  const [reiter, setReiter] = useState<EntfeuchterReiter>('ueberblick')
  const [eigeneHysterese, setEigeneHysterese] = useState(false)
  const [fehler, setFehler] = useState<string | null>(null)
  const [feldFehler, setFeldFehler] = useState<Record<string, string>>({})
  const [meldung, setMeldung] = useState<string | null>(null)
  const [laedt, setLaedt] = useState(true)
  const [arbeitet, setArbeitet] = useState(false)
  // Der zuletzt geladene Stand: Bezug für „was hat sich geändert". Als Ref,
  // damit das Auffrischen ihn lesen kann, ohne selbst davon abzuhängen.
  const geladenRef = useRef<Einstellungen | null>(null)
  const fehlerZeigen = useFehlerZeigen(reiter, setReiter, ENTFEUCHTER_REITER)

  const auffrischen = useCallback(async () => {
    try {
      const neu = await apiFetch<EntfeuchterZusatzSeite>('/api/steuerung/entfeuchter-zusatz')
      const alt = geladenRef.current
      geladenRef.current = neu.einstellungen
      setSeite(neu)
      // Einen angefangenen Entwurf nicht überschreiben — aber was der Nutzer
      // nicht angefasst hat, folgt dem frischen Stand.
      setEntwurf((vorher) => (vorher && alt ? entwurfNachfuehren(alt, neu.einstellungen, vorher) : neu.einstellungen))
    } catch {
      // Ein misslungenes Auffrischen ist kein Grund, die Seite rot zu färben.
    }
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    const laden = async () => {
      setLaedt(true)
      try {
        const geladen = await apiFetch<EntfeuchterZusatzSeite>('/api/steuerung/entfeuchter-zusatz', { signal: controller.signal })
        if (!controller.signal.aborted) {
          geladenRef.current = geladen.einstellungen
          setSeite(geladen)
          setEntwurf(geladen.einstellungen)
          setEigeneHysterese(!VPD_STUFEN.some((s) => gleich(s.wert, geladen.einstellungen.vpdHystereseKpa)))
          setFehler(null)
        }
      } catch (caught) {
        if (!controller.signal.aborted) setFehler(formatApiError(caught, 'Der Zusatz-Entfeuchter konnte nicht geladen werden.'))
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

  const geladen = seite?.einstellungen ?? null
  const geaendert = useMemo(() => Boolean(geladen && entwurf) && istGeaendert(geladen!, entwurf!), [geladen, entwurf])

  const speichern = async () => {
    if (!entwurf || !geladen) return
    const leer = leereFelder(entwurf)
    if (leer) {
      setFeldFehler(leer)
      setMeldung(null); setFehler('Bitte die markierten Felder prüfen.'); fehlerZeigen()
      return
    }
    const koerper = aenderungBilden(geladen, entwurf)
    if (Object.keys(koerper).length === 0) return
    setArbeitet(true); setMeldung(null); setFeldFehler({})
    try {
      const zurueck = await apiFetch<EntfeuchterZusatzSeite>('/api/steuerung/entfeuchter-zusatz', { method: 'PUT', body: JSON.stringify(koerper) })
      geladenRef.current = zurueck.einstellungen
      setSeite(zurueck); setEntwurf(zurueck.einstellungen); setFehler(null)
      setEigeneHysterese(!VPD_STUFEN.some((s) => gleich(s.wert, zurueck.einstellungen.vpdHystereseKpa)))
      setMeldung(zurueck.haAngenommen === false
        ? 'Gespeichert — aber nicht alle Helfer haben den Wert angenommen.'
        : 'Gespeichert.')
    } catch (caught) {
      const felder = feldFehlerAus(caught)
      if (felder) {
        setFeldFehler(felder)
        const rest = unbekannteFehler(felder)
        setFehler(rest.length > 0 ? `Bitte die markierten Felder prüfen. Außerdem: ${rest.join(' ')}` : 'Bitte die markierten Felder prüfen.')
        fehlerZeigen()
      } else {
        setFehler(formatApiError(caught, 'Speichern fehlgeschlagen.'))
      }
    } finally {
      setArbeitet(false)
    }
  }

  if (laedt && !seite) return <V1Page eyebrow="Steuerung" title="Zusatz-Entfeuchter"><V1Skeleton rows={4} tiles={1} label="Wird geladen" /></V1Page>
  if (!seite || !entwurf || !geladen) {
    return <V1Page eyebrow="Steuerung" title="Zusatz-Entfeuchter">{fehler && <V1Alert tone="critical" message={fehler} />}</V1Page>
  }

  const live = seite.live
  const fuehrung = live.fuehrungName
  const zusatz = live.zusatzName
  const anzeige = ohneLueckenZusatz(entwurf, geladen)
  const setz = <K extends keyof Einstellungen>(feld: K, wert: Einstellungen[K]) => setEntwurf({ ...entwurf, [feld]: wert })
  const setzMeldung = <K extends keyof ZusatzMeldung>(feld: K, wert: ZusatzMeldung[K]) => setEntwurf({ ...entwurf, meldung: { ...entwurf.meldung, [feld]: wert } })
  /** Einen Einzelwert ändern: die Hilfsstärke folgt (passt er zu einer Stufe, heißt sie so, sonst „eigene Werte"). */
  const setzEinzel = <K extends 'folgeAbstandK' | 'wiederEinAbstandK' | 'vpdHystereseKpa' | 'zuschaltVerzoegerungMin' | 'mindestpauseMin'>(feld: K, wert: number) =>
    setEntwurf(hilfeNachEinzelwert({ ...entwurf, [feld]: wert }))

  // --- Rechnung aus dem Entwurf: so sieht man vor dem Speichern, was sich ändert.
  const tagMax = tempMax(anzeige.tempMaxTagModus, anzeige.tempMaxTagAbstandK, anzeige.tempMaxTagFestC, live.planLuftTagC)
  const nachtMax = tempMax(anzeige.tempMaxNachtModus, anzeige.tempMaxNachtAbstandK, anzeige.tempMaxNachtFestC, live.planLuftNachtC)
  const tagPhase = live.tagPhase !== false
  const grenzen = tempGrenzen(anzeige, tagPhase ? tagMax : nachtMax)
  const tempBand = temperaturBand(grenzen, live.tempC, fuehrung, zusatz)
  const eins = ersteBand(live, anzeige)
  const zustand = zustandWort(live, geladen, tempBand.istWarm)
  const hinweis = statusHinweis(live, anzeige, grenzen, eins)
  const zeilen = aenderungsListe(geladen, anzeige, { tag: live.planLuftTagC, nacht: live.planLuftNachtC })
  const phaseText = live.tagPhase === true ? 'Tag' : live.tagPhase === false ? 'Nacht' : null
  const pausiert = tempBand.istWarm && geladen.hilfe !== 'aus' && geladen.automatikAktiv && live.zusatzAn !== true

  const empfehlung = (art: 'tag' | 'nacht'): Empfehlung | null => {
    const e = hoechstEmpfehlung(art, anzeige, art === 'tag' ? live.planLuftTagC : live.planLuftNachtC)
    return e && { text: e.text, gleich: e.gleich, onZuruecksetzen: () => setEntwurf({ ...entwurf, [e.feld]: e.wert }) }
  }
  const planTexte = {
    tag: `${live.planLuftTagC == null ? 'Kein Plan-Wert — es gilt der feste Wert.' : `Plan-Luft: ${zahl(live.planLuftTagC)} °C`} · gilt für beide Geräte`,
    nacht: `${live.planLuftNachtC == null ? 'Kein Plan-Wert — es gilt der feste Wert.' : `Plan-Luft Nacht: ${zahl(live.planLuftNachtC)} °C`} · gilt für beide Geräte`,
  }

  // --- Zonen (A-014): Ziel der Luftfeuchte ist die Plan-Schwelle EIN; die Temperatur ist im Ziel,
  // solange der Zusatz läuft darf (bis „Zusatz aus"), knapp bis zur Höchsttemperatur, darüber sind beide aus.
  const feuchteZiel = live.feuchteEinProzent
  const fZone = feuchteZone(live.feuchteProzent, feuchteZiel)
  const tZone = temperaturZone(live.tempC, grenzen.max, grenzen.folgeAus)
  const lage = schlechtereZone(fZone, tZone)
  const lageText = [
    fZone && `Luftfeuchte ${zahl(live.feuchteProzent)} % (${ZONEN_WORT[fZone]}${feuchteZiel == null ? '' : `, Ziel ${zahl(feuchteZiel, 0)} %`})`,
    tZone && `Temperatur ${zahl(live.tempC)} °C (${ZONEN_WORT[tZone]}, Höchsttemperatur ${zahl(grenzen.max)} °C)`,
  ].filter(Boolean).join(' · ')
  const zusatzLageTitel = pausiert ? `${zusatz} wartet` : live.zusatzAn === true ? `${zusatz} läuft` : `${zusatz} bereit`
  const feuchteWerte = [live.feuchteProzent, feuchteZiel].filter((w): w is number => w != null && Number.isFinite(w))
  const fVon = feuchteWerte.length ? Math.floor(Math.min(...feuchteWerte) - 3) : 40
  const fBis = feuchteWerte.length ? Math.ceil(Math.max(...feuchteWerte, (feuchteZiel ?? 0) + RF_KNAPP_PUNKTE) + 3) : 70
  const warum: WarumZeile[] = [
    { frage: `${fuehrung} läuft`, antwort: live.fuehrungAn == null ? 'unbekannt' : live.fuehrungAn ? 'ja — der Zusatz darf mithelfen' : 'nein — der Zusatz startet sofort, wenn nötig', ok: live.fuehrungAn },
    { frage: `Zelt nicht zu warm (${zusatz} geht ab ${zahl(grenzen.folgeAus)} °C aus)`, antwort: `${zahl(live.tempC)} °C · wieder an unter ${zahl(grenzen.wiederEin)} °C`, ok: live.tempC == null ? null : live.tempC <= grenzen.folgeAus },
    { frage: 'Hilfe und Automatik an', antwort: geladen.hilfe === 'aus' ? 'Hilfe steht auf „aus".' : geladen.automatikAktiv ? 'ja' : 'Automatik ist aus.', ok: geladen.hilfe !== 'aus' && geladen.automatikAktiv },
  ]

  return (
    <V1Page
      eyebrow="Steuerung"
      title="Zusatz-Entfeuchter"
      subtitle={`Hilft ${fuehrung} — geregelt in Home Assistant`}
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
            {m.kennung === aktiv ? zusatz : m.titel}
          </button>
        ))}
      </div>

      {fehler && <V1Alert tone="critical" message={fehler} />}
      {meldung && <V1Alert tone={meldung === 'Gespeichert.' ? 'ok' : 'warn'} message={meldung} />}
      {!live.haErreichbar && <V1Alert title="Home Assistant antwortet nicht" message="Die Werte sind der letzte bekannte Stand. Die Regelung läuft dort weiter." />}
      {live.zusatzOnline === false && <V1Alert tone="warn" title={`${zusatz} ist offline`} message="Die Steckdose meldet sich nicht. Geschaltet wird erst, wenn sie wieder da ist." />}
      {live.automatikAn === false && (
        <V1Alert tone="warn" title="Automatik aus" message={`Die Regelung ist angehalten. ${zusatz} bleibt, wie er gerade steht.`} />
      )}
      {planHinweisZeigen(live) && (
        <V1Alert tone="warn" title="Plan unvollständig" message="Der Plan liefert weder ein VPD-Ziel noch eine Luftfeuchte — der Fork findet keine Größe, nach der er schalten könnte. Bitte den Plan ergänzen." />
      )}
      {live.ziehtNichts === true && (
        <V1Alert
          tone="warn"
          title={`${zusatz} zieht nichts`}
          message={`Der Shelly ist an, das Gerät nimmt aber ${live.leistungW == null ? 'kaum etwas' : `nur ${zahl(live.leistungW, 0)} W`} auf. Tank voll oder Gerät ausgeschaltet?`}
        />
      )}
      {pausiert && (
        <V1Alert tone="warn" title={`${zusatz} pausiert: Zelt zu warm`} message={`Er bleibt aus, bis das Zelt wieder unter ${zahl(grenzen.wiederEin)} °C liegt.`} />
      )}

      {zeilen.length > 0 && (
        <div className="ez-aender" data-audit="zusatz-aenderungen">
          <b>Wird gespeichert — nur das:</b>
          <ul>
            {zeilen.map((z) => (
              <li key={z.feld}>
                {z.label}: {z.von} → <b>{z.nach}</b>
                {z.folge && <small> ({z.folge})</small>}
              </li>
            ))}
          </ul>
          Alles andere bleibt, wie es ist.
        </div>
      )}

      <V1Tabs items={ENTFEUCHTER_REITER} active={reiter} onChange={setReiter} label="Bereich" insBild />

      {/* ---------------------------------------------------------- Überblick */}
      {reiter === 'ueberblick' && (
        <>
          {lage && <Lage zone={lage} titel={zusatzLageTitel} text={lageText} />}

          <Klappkachel
            titel="Messwerte & Zonen"
            zusammenfassung={`${zahl(live.feuchteProzent)} % rF · ${zahl(live.tempC)} °C · VPD ${zahl(live.vpd, 2)}${phaseText ? ` · ${phaseText}` : ''}`}
          >
            <V1Card className="ef-band">
              {eins.band && (
                <div className="ef-block">
                  <MessKopf
                    wert={tagPhase ? zahl(live.vpd, 2) : zahl(live.feuchteProzent, 0)}
                    einheit={tagPhase ? 'kPa VPD' : '% rF'}
                    zone={null}
                    zustand={zustand.text}
                    ton={zustand.ton === 'an' ? 'an' : zustand.ton === 'warn' ? 'warn' : undefined}
                    beiwerk={`${zahl(live.feuchteProzent)} % rF · ${zahl(live.tempC)} °C${phaseText ? ` · ${phaseText}` : ''}`}
                  />
                  <BandAnzeige band={eins.band} />
                </div>
              )}
              {feuchteZiel != null && (
                <div className="ef-block">
                  <MessKopf
                    wert={zahl(live.feuchteProzent)}
                    einheit="% rF"
                    zone={fZone}
                    zustand={fZone ? ZONEN_WORT[fZone] : 'keine Aussage'}
                    ton={fZone === 'kritisch' ? 'kritisch' : fZone === 'knapp' ? 'warn' : undefined}
                    beiwerk={`Ziel ${zahl(feuchteZiel, 0)} %`}
                  />
                  <ZonenSkala
                    von={fVon}
                    bis={fBis}
                    zielBis={feuchteZiel}
                    knappBis={feuchteZiel + RF_KNAPP_PUNKTE}
                    marken={[{ wert: feuchteZiel, label: 'Ziel' }, { wert: feuchteZiel + RF_KNAPP_PUNKTE, label: 'Grenze' }]}
                    ist={live.feuchteProzent}
                    zone={fZone}
                    einheit="%"
                  />
                </div>
              )}
              <div className="ef-block">
                <MessKopf
                  wert={zahl(live.tempC)}
                  einheit="°C"
                  zone={tZone}
                  zustand={tZone === 'kritisch' ? 'zu warm — beide aus' : pausiert ? `${zusatz} wartet auf ${zahl(grenzen.wiederEin)} °C` : tZone ? ZONEN_WORT[tZone] : 'keine Aussage'}
                  ton={tZone === 'kritisch' ? 'kritisch' : tZone === 'knapp' ? 'warn' : undefined}
                  beiwerk={`Höchsttemperatur ${zahl(grenzen.max)} °C${phaseText ? ` · ${phaseText}` : ''}`}
                />
                <ZonenSkala
                  von={tempBand.von}
                  bis={tempBand.bis}
                  zielBis={grenzen.folgeAus}
                  knappBis={grenzen.max}
                  marken={[
                    { wert: grenzen.wiederEin, label: 'an' },
                    { wert: grenzen.folgeAus, label: 'Zusatz' },
                    { wert: grenzen.max, label: 'Haupt' },
                  ]}
                  ist={live.tempC}
                  zone={tZone}
                  einheit="°C"
                />
              </div>
              <ZonenLegende />
              {hinweis && <p className="st-hinweis">{hinweis}</p>}
            </V1Card>
          </Klappkachel>

          <Warum an={live.zusatzAn} zeilen={warum} />
          <Zusammenspiel
            haupt={fuehrung}
            zusatz={zusatz}
            tempMax={grenzen.max}
            abstaende={{ folgeAus: grenzen.folgeAus, wiederEin: grenzen.wiederEin, mindestpauseMin: anzeige.mindestpauseMin }}
          />
        </>
      )}

      {/* -------------------------------------------------------------- Regel */}
      {reiter === 'regel' && (
        <>
          <Klappkachel titel="Hilfe" zusammenfassung={`${HILFE_STUFEN.find((s) => s.wert === entwurf.hilfe)?.label ?? 'eigene Werte'} · Automatik ${entwurf.automatikAktiv ? 'an' : 'aus'}`}>
            <V1Card>
              <div className="st-feldzeile is-gestapelt">
                <span className="st-etikett">
                  Wie stark soll {zusatz} helfen?
                  <small>{fuehrung} führt. Der Zusatz hilft nur dazu.</small>
                </span>
                <div className="ef-stufen" role="radiogroup" aria-label="Hilfsstärke">
                  {HILFE_STUFEN.map((s) => (
                    <button
                      key={s.wert}
                      type="button"
                      role="radio"
                      className="st-chip"
                      aria-checked={entwurf.hilfe === s.wert}
                      aria-current={entwurf.hilfe === s.wert}
                      onClick={() => setEntwurf(hilfeWaehlen(entwurf, geladen, s.wert))}
                    >
                      {s.label}
                    </button>
                  ))}
                  {entwurf.hilfe === 'eigene' && (
                    <button type="button" role="radio" className="st-chip" aria-checked="true" aria-current="true">eigene Werte</button>
                  )}
                </div>
                <p className="st-hinweis">{hilfeText(entwurf.hilfe, fuehrung)}</p>
                <p className={entwurf.hilfe === 'normal' ? 'ez-empf' : 'ez-empf is-abweichend'}>
                  {entwurf.hilfe === 'normal' ? 'Empfohlen: normal ✓' : 'Empfohlen: normal'}
                  {entwurf.hilfe !== 'normal' && (
                    <button type="button" onClick={() => setEntwurf(hilfeWaehlen(entwurf, geladen, 'normal'))}>zurücksetzen</button>
                  )}
                </p>
              </div>
            </V1Card>
          </Klappkachel>

          <Klappkachel
            titel="Einschalten & Ausschalten"
            zusammenfassung={live.vpdZiel == null ? 'Plan-Ziel unbekannt' : `VPD-Abstand ${zahl(anzeige.vpdHystereseKpa, 2)} kPa → EIN ${zahl(live.vpdZiel - anzeige.vpdHystereseKpa, 2)} · AUS ${zahl(live.vpdZiel + anzeige.vpdHystereseKpa, 2)}`}
          >
            <V1Card>
              <Lesen
                label="Schaltgröße"
                hinweis={'VPD-Ziel aus dem Plan. Fehlt es, nimmt der Fork die Plan-Luftfeuchte. Fehlt beides, steht oben „Plan unvollständig".'}
                wert={live.schaltgroesse === 'vpd' ? 'VPD' : live.schaltgroesse === 'feuchte' ? 'Luftfeuchte' : '–'}
              />
              <Lesen label="VPD-Ziel" hinweis={live.planWoche ?? undefined} wert={live.vpdZiel == null ? '–' : `${zahl(live.vpdZiel, 2)} kPa`} />
              <div className="st-feldzeile is-gestapelt">
                <span className="st-etikett">
                  Wie ruhig soll er schalten?
                  <small>Abstand rechts und links vom Plan-Ziel. Weiter auseinander = seltener schalten, größere Schwankung.</small>
                  {fehlerZu(feldFehler, 'vpdHystereseKpa') && <span className="st-fehler">{fehlerZu(feldFehler, 'vpdHystereseKpa')}</span>}
                </span>
                <div className="ef-stufen" role="radiogroup" aria-label="VPD-Abstand">
                  {VPD_STUFEN.map((s) => {
                    const an = !eigeneHysterese && gleich(entwurf.vpdHystereseKpa, s.wert)
                    return (
                      <button key={s.wert} type="button" role="radio" className="st-chip" aria-checked={an} aria-current={an}
                        onClick={() => { setEigeneHysterese(false); setzEinzel('vpdHystereseKpa', s.wert) }}>
                        {s.label} · {zahl(s.wert, 2)}
                      </button>
                    )
                  })}
                  <button type="button" role="radio" className="st-chip" aria-checked={eigeneHysterese} aria-current={eigeneHysterese} onClick={() => setEigeneHysterese(true)}>
                    eigener Wert
                  </button>
                </div>
                {eigeneHysterese && (
                  <div className="ef-unterfeld">
                    <Zahl label="Eigener VPD-Abstand" hinweis="0,05 bis 0,60 kPa." einheit="kPa" wert={entwurf.vpdHystereseKpa} min={0.05} max={0.6} schritt={0.05} onChange={(v) => setzEinzel('vpdHystereseKpa', v)} />
                  </div>
                )}
                <p className="ef-folge">
                  {live.vpdZiel == null
                    ? 'Das Plan-Ziel ist noch nicht bekannt.'
                    : `EIN bei VPD unter ${zahl(live.vpdZiel - anzeige.vpdHystereseKpa, 2)}, AUS über ${zahl(live.vpdZiel + anzeige.vpdHystereseKpa, 2)} kPa.`}
                  {' '}Knapp hält das VPD enger, schaltet aber öfter.
                </p>
              </div>
              <Lesen
                label="Luftfeuchte EIN / AUS"
                hinweis={'Schwellen aus dem Plan; gelten nachts mit „Nachts durchlaufen" und wenn der Plan kein VPD liefert.'}
                wert={live.feuchteEinProzent == null || live.feuchteAusProzent == null ? '–' : `${zahl(live.feuchteEinProzent, 0)} % / ${zahl(live.feuchteAusProzent, 0)} %`}
              />
              <div className="st-feldzeile">
                <span className="st-etikett">Ändern im Plan</span>
                <V1LinkButton to="/plan" variant="ghost">Plan ›</V1LinkButton>
              </div>
            </V1Card>
          </Klappkachel>

          <Klappkachel titel="Tag & Nacht" zusammenfassung={`Nachts ${entwurf.nachtDurchlaufen ? 'durchlaufen' : 'nach VPD'} · tagsüber ${entwurf.tagbetriebErlauben ? 'erlaubt' : 'aus'}`} offen={false}>
            <V1Card>
              <V1Switch
                label="Nachts durchlaufen"
                checked={entwurf.nachtDurchlaufen}
                onChange={(an) => setz('nachtDurchlaufen', an)}
                hint="An: nachts läuft er, bis das Zelt zu warm wird (keine Feuchtespitzen). Aus: auch nachts nach dem VPD-Band takten. Wirkt mit der vom Fork angelegten Regelung."
              />
              <V1Switch
                label="Auch tagsüber entfeuchten"
                checked={entwurf.tagbetriebErlauben}
                onChange={(an) => setz('tagbetriebErlauben', an)}
                hint="Aus: nur in der Dunkelphase. Wirkt mit der vom Fork angelegten Regelung."
              />
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
                titel="Höchsttemperatur tagsüber"
                ariaLabel="Höchsttemperatur tagsüber"
                planText={planTexte.tag}
                modus={entwurf.tempMaxTagModus}
                abstand={entwurf.tempMaxTagAbstandK}
                fest={entwurf.tempMaxTagFestC}
                plan={live.planLuftTagC}
                ergebnis={tagMax}
                onModus={(m) => setz('tempMaxTagModus', m)}
                onAbstand={(v) => setz('tempMaxTagAbstandK', v)}
                onFest={(v) => setz('tempMaxTagFestC', v)}
                fehlerAbstand={fehlerZu(feldFehler, 'tempMaxTagAbstandK')}
                fehlerFest={fehlerZu(feldFehler, 'tempMaxTagFestC')}
                empfohlen={empfehlung('tag')}
              />
              <TempMaxBlock
                titel="Höchsttemperatur nachts"
                ariaLabel="Höchsttemperatur nachts"
                planText={planTexte.nacht}
                modus={entwurf.tempMaxNachtModus}
                abstand={entwurf.tempMaxNachtAbstandK}
                fest={entwurf.tempMaxNachtFestC}
                plan={live.planLuftNachtC}
                ergebnis={nachtMax}
                onModus={(m) => setz('tempMaxNachtModus', m)}
                onAbstand={(v) => setz('tempMaxNachtAbstandK', v)}
                onFest={(v) => setz('tempMaxNachtFestC', v)}
                fehlerAbstand={fehlerZu(feldFehler, 'tempMaxNachtAbstandK')}
                fehlerFest={fehlerZu(feldFehler, 'tempMaxNachtFestC')}
                empfohlen={empfehlung('nacht')}
              />
              <p className="st-hinweis">Darüber geht {fuehrung} aus. {zusatz} geht {zahl(anzeige.folgeAbstandK)} K früher aus und kommt {zahl(anzeige.wiederEinAbstandK)} K darunter wieder.</p>
            </V1Card>
          </Klappkachel>

          <Klappkachel titel="Früher aus, später wieder an" zusammenfassung={`−${zahl(anzeige.folgeAbstandK)} K · Wieder-EIN −${zahl(anzeige.wiederEinAbstandK)} K`}>
            <V1Card>
              <Zahl label="Zusatz geht früher aus" hinweis={`Bei ${zahl(grenzen.max)} °C geht ${fuehrung} aus, der Zusatz schon bei ${zahl(grenzen.folgeAus)}.`} einheit="K" wert={entwurf.folgeAbstandK} min={0.5} max={3} schritt={0.5} onChange={(v) => setzEinzel('folgeAbstandK', v)} fehler={fehlerZu(feldFehler, 'folgeAbstandK')} />
              <Zahl label="Wieder einschalten erst, wenn es kühler ist" hinweis={`Weiterer Abstand unter dem Abschaltwert (jetzt ${zahl(grenzen.wiederEin)} °C). Verhindert das Takten an der Grenze.`} einheit="K" wert={entwurf.wiederEinAbstandK} min={0.5} max={3} schritt={0.5} onChange={(v) => setzEinzel('wiederEinAbstandK', v)} fehler={fehlerZu(feldFehler, 'wiederEinAbstandK')} />
            </V1Card>
          </Klappkachel>

          <Klappkachel titel="Laufverhalten" zusammenfassung={`Lauf ${anzeige.mindestlaufzeitMin} min · Pause ${anzeige.mindestpauseMin} min`} offen={false}>
            <V1Card>
              <Zahl label="Mindestlaufzeit" hinweis="Vorher schaltet ihn erreichtes VPD nicht ab. Übertemperatur schon." einheit="min" wert={entwurf.mindestlaufzeitMin} min={0} max={60} schritt={1} onChange={(v) => setz('mindestlaufzeitMin', Math.round(v))} fehler={fehlerZu(feldFehler, 'mindestlaufzeitMin')} />
              <Zahl label="Mindestpause" hinweis="Kompressorschutz." einheit="min" wert={entwurf.mindestpauseMin} min={1} max={120} schritt={1} onChange={(v) => setzEinzel('mindestpauseMin', Math.round(v))} fehler={fehlerZu(feldFehler, 'mindestpauseMin')} />
              <p className="st-hinweis" data-audit="zusatz-fuehrung-regel">Der Zusatz geht wegen VPD oder Feuchte nur aus, wenn das Hauptgerät {fuehrung} läuft.</p>
            </V1Card>
          </Klappkachel>

          <Klappkachel titel="Meldung „zieht nichts" zusammenfassung={entwurf.meldung.aktiv ? `an · unter ${entwurf.meldung.grenzeW} W nach ${entwurf.meldung.dauerMin} min` : 'aus'} offen={false}>
            <V1Card>
              <V1Switch
                label="Melden, wenn der Shelly an ist, das Gerät aber nichts zieht"
                checked={entwurf.meldung.aktiv}
                onChange={(an) => setzMeldung('aktiv', an)}
                hint={live.leistungW == null ? 'Tank voll oder Gerät ausgeschaltet?' : `Tank voll oder Gerät ausgeschaltet? Gerade nimmt ${zusatz} ${zahl(live.leistungW, 0)} W auf.`}
              />
              <Zahl label="Meldung unter" hinweis={'Leistung, ab der es als „zieht nichts" gilt.'} einheit="W" wert={entwurf.meldung.grenzeW} min={5} max={200} schritt={5} onChange={(v) => setzMeldung('grenzeW', Math.round(v))} fehler={fehlerZu(feldFehler, 'meldung.grenzeW')} />
              <Zahl label="Meldung nach" hinweis="So lange muss es anhalten, bevor gemeldet wird." einheit="min" wert={entwurf.meldung.dauerMin} min={1} max={60} schritt={1} onChange={(v) => setzMeldung('dauerMin', Math.round(v))} fehler={fehlerZu(feldFehler, 'meldung.dauerMin')} />
              <Zahl label="Meldung wiederholen alle" hinweis="Solange das Problem besteht. Als Meldung in Home Assistant und als Push an die in den Meldungs-Einstellungen gewählte Adresse. Nie bei ausgeschaltetem Shelly." einheit="h" wert={entwurf.meldung.wiederholungH} min={1} max={24} schritt={1} onChange={(v) => setzMeldung('wiederholungH', Math.round(v))} fehler={fehlerZu(feldFehler, 'meldung.wiederholungH')} />
            </V1Card>
          </Klappkachel>
        </>
      )}

      {/* ------------------------------------------------------------ Betrieb */}
      {reiter === 'betrieb' && (
        <>
          <Klappkachel titel="Betrieb" zusammenfassung={`Automatik ${entwurf.automatikAktiv ? 'an' : 'aus'} · zuschalten nach ${anzeige.zuschaltVerzoegerungMin} min`}>
            <V1Card>
              <V1Switch
                label="Automatik"
                checked={entwurf.automatikAktiv}
                onChange={(an) => setz('automatikAktiv', an)}
                hint={`Aus hält die Regelung an. ${zusatz} bleibt, wie er gerade steht.`}
              />
              <Zahl label="Zuschalten erst nach" hinweis={`So lange muss ${fuehrung} laufen, bevor ${zusatz} mithilft. Ist ${fuehrung} aus, startet der Zusatz sofort.`} einheit="min" wert={entwurf.zuschaltVerzoegerungMin} min={0} max={60} schritt={1} onChange={(v) => setzEinzel('zuschaltVerzoegerungMin', Math.round(v))} fehler={fehlerZu(feldFehler, 'zuschaltVerzoegerungMin')} />
              <div className="st-feldzeile">
                <span className="st-etikett">
                  Ablauf des Kondenswassers
                  <small>Tank oder Ablaufschlauch</small>
                </span>
                <span className="ef-stufen" role="radiogroup" aria-label="Ablauf des Kondenswassers">
                  {(['tank', 'schlauch'] as ZusatzAblauf[]).map((a) => (
                    <button key={a} type="button" role="radio" className="st-chip" aria-checked={entwurf.ablauf === a} aria-current={entwurf.ablauf === a} onClick={() => setz('ablauf', a)}>
                      {ablaufText(a)}
                    </button>
                  ))}
                </span>
              </div>
            </V1Card>
          </Klappkachel>

          {(live.energieHeuteKwh != null || live.leistungW != null) && (
            <Klappkachel
              titel="Heute"
              zusammenfassung={`${live.energieHeuteKwh == null ? '' : `${zahl(live.energieHeuteKwh)} kWh`}${live.energieHeuteKwh != null && live.leistungW != null ? ' · ' : ''}${live.leistungW == null ? '' : `${zahl(live.leistungW, 0)} W`}`}
              offen={false}
            >
              <V1Card>
                {live.energieHeuteKwh != null && <Lesen label="Energie heute" wert={`${zahl(live.energieHeuteKwh)} kWh`} />}
                {live.leistungW != null && <Lesen label="Leistung jetzt" wert={`${zahl(live.leistungW, 0)} W`} />}
              </V1Card>
            </Klappkachel>
          )}
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
        <V1LinkButton to={rollenPfad('entfeuchter-zusatz')} variant="ghost">Namen &amp; Rollen ›</V1LinkButton>
      </div>
    </V1Page>
  )
}

/** Eine Zeile zum Lesen — kein Eingabefeld. */
function Lesen({ label, hinweis, wert }: { label: string; hinweis?: ReactNode; wert: string }) {
  return (
    <div className="st-feldzeile">
      <span className="st-etikett">
        {label}
        {hinweis && <small>{hinweis}</small>}
      </span>
      <span className="st-nurlesen">{wert}</span>
    </div>
  )
}

/** Ein Band: Skala mit Zonen, Marken und Istwert. Rechnung in `entfeuchter-zusatz.ts`. */
function BandAnzeige({ band }: { band: ZusatzBand }) {
  return (
    <>
      <div className="ez-bandtitel"><span>{band.titel}</span><span>{band.kurz}</span></div>
      <div className="ef-skala" aria-hidden="true">
        <div className="ef-bahn" />
        {band.zonen.map((z) => (
          <div
            key={`${z.art}-${z.ab}`}
            className={`ez-zone is-${z.art}`}
            style={{ left: `${zonenPos(band, z.ab)}%`, width: `${Math.max(0, zonenPos(band, z.bis) - zonenPos(band, z.ab))}%` }}
          />
        ))}
        {band.marken.map((m) => (
          <div key={m.label + m.wert} className={`ef-strich is-${m.art}`} style={{ left: `${m.pos}%` }} />
        ))}
        {band.ist != null && (
          <div className={band.istWarm ? 'ef-ist is-warm' : 'ef-ist'} style={{ left: `${band.ist}%` }}><em>{zahl(band.istWert, band.stellen)}</em></div>
        )}
      </div>
      <div className="ef-marken">
        {band.marken.map((m) => (
          <span key={m.label + m.wert} className={m.art === 'max' ? 'is-deckel' : undefined} style={{ left: `${m.pos}%` }}>
            {zahl(m.wert, band.stellen)}<b>{m.label}</b>
          </span>
        ))}
      </div>
      <div className="ef-rand"><span>{zahl(band.von, band.stellen)} {band.einheit}</span><span>{zahl(band.bis, band.stellen)} {band.einheit}</span></div>
    </>
  )
}

const zonenPos = (band: ZusatzBand, wert: number) =>
  Math.round(Math.max(0, Math.min(100, ((wert - band.von) / (band.bis - band.von)) * 100)) * 10) / 10
