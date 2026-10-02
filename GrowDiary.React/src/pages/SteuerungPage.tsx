import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { apiFetch, formatApiError } from '../api'
import { V1Alert, V1Button, V1Card, V1Empty, V1LinkButton, V1Page, V1Section, V1Skeleton, V1Switch, V1Tabs } from '../components/v1'
import LichtDetail from '../features/steuerung/LichtDetail'
import ZuluftDetail from '../features/steuerung/ZuluftDetail'
import ChillerDetail from '../features/steuerung/ChillerDetail'
import EntfeuchterDetail from '../features/steuerung/EntfeuchterDetail'
import { CO2_REITER, minuten, probeWerte, tagKurz, wirksameZiele } from '../features/steuerung/steuerung-typen'
import type { Co2Einstellungen, Co2Reiter, Co2Seite, GrenzModus, SteuerungModul, SteuerungUebersicht } from '../features/steuerung/steuerung-typen'
import { formatNumber } from '../utils'
import '../features/steuerung/steuerung.css'
import { rollenPfad } from '../features/geraete/rollenPfad'
import { feldFehlerAus, leereZahlenfelder, modusFehler, zahlAusFeld } from '../features/steuerung/feld-fehler'
import { useFehlerZeigen } from '../features/steuerung/fehler-reiter'
import { BestandAbschnitt, BestandHinweis } from '../features/steuerung/Bestand'
import { useBestand } from '../features/steuerung/bestand-laden'

/**
 * Fork AI: Was die Prüfung der vorhandenen CO₂-Automationen meldet.
 *
 * Gilt auch für von Hand gebaute Automationen — die Vorlagen erreichen sie
 * nicht, die Schwachstellen haben sie trotzdem.
 */
type AbsicherungsLage = {
  erreichbar: boolean
  dosiertGerade: boolean
  behebbar: number
  hinweis: string | null
  automationen: Array<{
    entityId: string
    name: string
    befunde: Array<{ art: string; titel: string; erklaerung: string; behebbar: boolean; hinweis: string | null }>
  }>
  /** Die CO₂-Rechenwerte (Formeln der Template-Helfer). */
  rechenwerte: Array<{
    entityId: string
    name: string
    /** Aktuell · Veraltet · Angepasst · AndererFuehler · OhneFuehler · NichtLesbar */
    stand: string
    heute: string | null
    danach: string | null
    alteFormel: string | null
    neueFormel: string | null
    hinweis: string | null
  }>
}

type AbsicherungsBilanz = {
  abgelehnt: string | null
  einzeln: Array<{ entityId: string; name: string; geschrieben: boolean; fehler: string | null }>
  nachher: AbsicherungsLage
}

/**
 * Fork AI (forkai.20): Steuerung — der Leitstand der Regelungen.
 *
 * <b>Was hier liegt und was nicht.</b> Geregelt wird in Home Assistant: Impuls,
 * Wächter, Licht-aus-Sicherung, Abluft-Drosselung. Ein Ventil an einer
 * Gasflasche darf nicht davon abhängen, ob ein Web-Add-on gerade neu startet.
 * Diese Seite besitzt die <b>Sollwerte</b> und zeigt, was daraus wurde.
 *
 * <b>Warum eine Registry und nicht eine CO₂-Seite.</b> CO₂ ist die erste
 * Steuerung, nicht die einzige — Entfeuchter, Chiller, Abluft und Licht sollen
 * folgen. Übersicht und Chip-Leiste speisen sich deshalb aus derselben Liste,
 * die das Backend liefert; eine neue Steuerung braucht hier nur eine
 * Detailansicht, keinen Eingriff in den Rahmen.
 */
export default function SteuerungPage() {
  const { modul } = useParams<{ modul?: string }>()
  const navigate = useNavigate()

  const [uebersicht, setUebersicht] = useState<SteuerungUebersicht | null>(null)
  const [fehler, setFehler] = useState<string | null>(null)
  const [laedt, setLaedt] = useState(true)

  useEffect(() => {
    const controller = new AbortController()
    const laden = async () => {
      setLaedt(true)
      try {
        const geladen = await apiFetch<SteuerungUebersicht>('/api/steuerung', { signal: controller.signal })
        if (!controller.signal.aborted) { setUebersicht(geladen); setFehler(null) }
      } catch (caught) {
        if (!controller.signal.aborted) setFehler(formatApiError(caught, 'Die Steuerungen konnten nicht geladen werden.'))
      } finally {
        if (!controller.signal.aborted) setLaedt(false)
      }
    }
    void laden()
    return () => controller.abort()
  }, [])

  if (modul === 'co2') {
    return <Co2Detail module={uebersicht?.module ?? []} aktiv={modul} onWechsel={(k) => navigate(`/steuerung/${k}`)} />
  }

  if (modul === 'licht') {
    return <LichtDetail module={uebersicht?.module ?? []} aktiv={modul} onWechsel={(k) => navigate(`/steuerung/${k}`)} />
  }

  if (modul === 'chiller') {
    return <ChillerDetail module={uebersicht?.module ?? []} aktiv={modul} onWechsel={(k) => navigate(`/steuerung/${k}`)} />
  }

  if (modul === 'entfeuchter') {
    return <EntfeuchterDetail module={uebersicht?.module ?? []} aktiv={modul} onWechsel={(k) => navigate(`/steuerung/${k}`)} />
  }

  if (modul === 'zuluft') {
    return <ZuluftDetail module={uebersicht?.module ?? []} aktiv={modul} onWechsel={(k) => navigate(`/steuerung/${k}`)} />
  }

  // Ein Pfad, den keine Steuerung kennt. Er landet bewusst NICHT ersatzweise
  // auf CO₂ — sonst stünden fremde Zahlen unter einem falschen Namen.
  if (modul) {
    return (
      <V1Page eyebrow="Steuerung" title="Unbekannte Steuerung">
        <V1Empty
          title={`„${modul}" gibt es hier nicht`}
          text="Diese Steuerung ist noch nicht gebaut oder heißt anders."
          action={<V1Button variant="primary" onClick={() => navigate('/steuerung')}>Zur Übersicht</V1Button>}
        />
      </V1Page>
    )
  }

  return (
    <V1Page eyebrow="Betrieb" title="Steuerung" subtitle="Alle Regelungen des RDWC-Zelts · laufen in Home Assistant">
      {fehler && <V1Alert tone="critical" message={fehler} />}
      {uebersicht && !uebersicht.haErreichbar && (
        <V1Alert title="Home Assistant antwortet nicht" message="Die Werte unten sind der letzte bekannte Stand. Die Regelung läuft davon unabhängig weiter." />
      )}
      {laedt && !uebersicht ? (
        <V1Skeleton rows={5} label="Steuerungen werden geladen" />
      ) : !uebersicht || uebersicht.module.length === 0 ? (
        <V1Empty title="Noch keine Steuerung" text="Sobald eine Regelung eingerichtet ist, steht sie hier." />
      ) : (
        <div className="st-liste">
          {uebersicht.module.map((m) => (
            <ModulZeile key={m.kennung} modul={m} onOeffnen={() => navigate(`/steuerung/${m.kennung}`)} />
          ))}
        </div>
      )}
      <p className="st-fuss">Sollwerte werden in Home Assistant gespiegelt</p>
    </V1Page>
  )
}

function ModulZeile({ modul, onOeffnen }: { modul: SteuerungModul; onOeffnen: () => void }) {
  const punkt = modul.status === 'an' ? 'is-an' : modul.status === 'warn' ? 'is-warn' : 'is-aus'
  return (
    <button type="button" className="st-zeile" onClick={onOeffnen} disabled={!modul.hatDetail}>
      <span className="st-zeile-links">
        <i className={`st-punkt ${punkt}`} aria-hidden="true" />
        <span className="st-titel">
          {modul.titel}
          <small>{modul.kurz}</small>
        </span>
      </span>
      <span className="st-wert">
        {modul.wert}
        <small>{modul.unterzeile}</small>
      </span>
    </button>
  )
}

// ---------------------------------------------------------------- CO₂-Detail

function Co2Detail({ module, aktiv, onWechsel }: { module: SteuerungModul[]; aktiv: string; onWechsel: (kennung: string) => void }) {
  const [seite, setSeite] = useState<Co2Seite | null>(null)
  const [entwurf, setEntwurf] = useState<Co2Einstellungen | null>(null)
  const [reiter, setReiter] = useState<Co2Reiter>('ziel')
  // Fork AI (forkai.45): Was die Steuerung in Home Assistant braucht — siehe Bestand.tsx.
  const { bestand, neuLaden: bestandNeuLaden } = useBestand('co2')
  const [probe, setProbe] = useState<string | null>(null)
  const [absicherung, setAbsicherung] = useState<AbsicherungsLage | null>(null)
  const [sichertAb, setSichertAb] = useState(false)
  const [absicherMeldung, setAbsicherMeldung] = useState<{ ton: 'ok' | 'warn'; text: string } | null>(null)
  const [fehler, setFehler] = useState<string | null>(null)
  const [feldFehler, setFeldFehler] = useState<Record<string, string>>({})
  // Gesperrtes Speichern: auf den Reiter des markierten Felds wechseln und hinrollen.
  const fehlerZeigen = useFehlerZeigen(reiter, setReiter, CO2_REITER)
  const [meldung, setMeldung] = useState<string | null>(null)
  const [laedt, setLaedt] = useState(true)
  const [speichert, setSpeichert] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    const laden = async () => {
      setLaedt(true)
      try {
        const geladen = await apiFetch<Co2Seite>('/api/steuerung/co2', { signal: controller.signal })
        if (!controller.signal.aborted) { setSeite(geladen); setEntwurf(geladen.einstellungen); setFehler(null) }
      } catch (caught) {
        if (!controller.signal.aborted) setFehler(formatApiError(caught, 'Die CO₂-Steuerung konnte nicht geladen werden.'))
      } finally {
        if (!controller.signal.aborted) setLaedt(false)
      }
    }
    void laden()
    return () => controller.abort()
  }, [])

  // Fork AI: Die vorhandenen CO₂-Automationen auf Schwachstellen prüfen. Nur
  // lesen — geändert wird erst, wenn der Bediener auf „Absichern“ tippt.
  useEffect(() => {
    const controller = new AbortController()
    const pruefen = async () => {
      try {
        const geladen = await apiFetch<AbsicherungsLage>('/api/steuerung/co2/absicherung', { signal: controller.signal })
        if (!controller.signal.aborted) setAbsicherung(geladen)
      } catch {
        if (!controller.signal.aborted) setAbsicherung(null)
      }
    }
    void pruefen()
    return () => controller.abort()
  }, [])

  const absichern = async () => {
    setSichertAb(true)
    setAbsicherMeldung(null)
    try {
      const bilanz = await apiFetch<AbsicherungsBilanz>('/api/steuerung/co2/absicherung', { method: 'POST' })
      setAbsicherung(bilanz.nachher)
      if (bilanz.abgelehnt) {
        setAbsicherMeldung({ ton: 'warn', text: bilanz.abgelehnt })
      } else {
        const gut = bilanz.einzeln.filter((e) => e.geschrieben)
        const schlecht = bilanz.einzeln.filter((e) => !e.geschrieben)
        const automationen = gut.filter((e) => e.entityId.startsWith('automation.')).length
        const rechenwerte = gut.length - automationen
        const gezaehlt = [
          automationen > 0 ? `${automationen} ${automationen === 1 ? 'Automation' : 'Automationen'}` : null,
          rechenwerte > 0 ? `${rechenwerte} ${rechenwerte === 1 ? 'Rechenwert' : 'Rechenwerte'}` : null,
        ].filter(Boolean).join(' und ')
        const teile = [gezaehlt ? `${gezaehlt} abgesichert` : 'Nichts abgesichert']
        for (const e of schlecht) teile.push(`${e.name}: ${(e.fehler ?? 'nicht geschrieben').replace(/\.$/, '')}`)
        setAbsicherMeldung({ ton: schlecht.length > 0 ? 'warn' : 'ok', text: `${teile.join('. ')}.` })
      }
    } catch (caught) {
      setAbsicherMeldung({ ton: 'warn', text: formatApiError(caught, 'Absichern fehlgeschlagen.') })
    } finally {
      setSichertAb(false)
    }
  }

  const geaendert = useMemo(
    () => Boolean(seite && entwurf) && JSON.stringify(seite?.einstellungen) !== JSON.stringify(entwurf),
    [seite, entwurf],
  )

  const speichern = async () => {
    if (!entwurf) return
    // Ein geleertes Feld ist NaN, im JSON `null` — das Backend kann es nicht
    // binden und meldete „Es wurde nichts übergeben.". Vorher sperren und das
    // Feld markieren (wie bei Chiller, Entfeuchter und Zuluft).
    const leer = leereZahlenfelder(entwurf)
    if (leer) { setFeldFehler(leer); setMeldung(null); setFehler('Bitte die markierten Felder prüfen.'); fehlerZeigen(); return }
    setSpeichert(true); setMeldung(null); setFeldFehler({})
    try {
      const zurueck = await apiFetch<Co2Seite>('/api/steuerung/co2', { method: 'PUT', body: JSON.stringify(entwurf) })
      setSeite(zurueck); setEntwurf(zurueck.einstellungen); setFehler(null)
      setMeldung(zurueck.haAngenommen === false
        ? 'Gespeichert — aber Home Assistant hat nicht alle Sollwerte angenommen. Die Regelung läuft mit den alten Werten weiter.'
        : 'Gespeichert und nach Home Assistant geschrieben.')
    } catch (caught) {
      const felder = feldFehlerAus(caught)
      if (felder) { setFeldFehler(felder); setFehler('Bitte die markierten Felder prüfen.'); fehlerZeigen() }
      else setFehler(formatApiError(caught, 'Speichern fehlgeschlagen.'))
    } finally {
      setSpeichert(false)
    }
  }

  const ventilProbieren = async () => {
    setProbe('Ventil wird zwei Sekunden geöffnet …')
    try {
      const e = await apiFetch<{ urteil: string; co2Vorher: string | null; co2Nachher: string | null }>(
        '/api/steuerung/co2/probe', { method: 'POST' },
      )
      setProbe(e.urteil + probeWerte(e.co2Vorher, e.co2Nachher))
    } catch (caught) {
      setProbe(formatApiError(caught, 'Probeschaltung fehlgeschlagen.'))
    }
  }

  if (laedt && !seite) return <V1Page title="CO₂-Begasung"><V1Skeleton rows={6} label="Wird geladen" /></V1Page>
  if (!seite || !entwurf) {
    return <V1Page title="CO₂-Begasung">{fehler && <V1Alert tone="critical" message={fehler} />}</V1Page>
  }

  const live = seite.live
  const ziele = wirksameZiele(entwurf, seite.planPpm)
  // Fork AI: Warum der T6 gerade tief läuft — die Grenzen kommen aus dem
  // Livebild, weil der Helfer in HA mit genau diesen Werten rechnet.
  const tempOk = live.canopyC != null && live.tiefBisTempC != null ? live.canopyC <= live.tiefBisTempC : null
  const rhOk = live.rhProzent != null && live.tiefBisRhProzent != null ? live.rhProzent <= live.tiefBisRhProzent : null
  const tiefGrund = !entwurf.abluftDrosseln
    ? 'Die Drosselung ist ausgeschaltet — der T6 bleibt auf der normalen Stufe.'
    : live.klimaOk === false
      ? `Das Klima sperrt gerade — der T6 läuft auf Stufe ${entwurf.t6StufeKlima ?? '–'}, bis die Feuchte im Mittel und die Canopy wieder unter den Grenzen sind.`
      : live.tiefAktiv == null
      ? 'Der Helfer binary_sensor.co2_t6_stufe_tief_sinnvoll meldet sich nicht.'
      : live.tiefAktiv && tempOk === true && rhOk === true
        ? 'Beide Bedingungen erfüllt — der T6 läuft auf der tiefen Stufe.'
        : live.tiefAktiv
          ? 'Haltebereich: die tiefe Stufe bleibt vorerst, damit es nicht flattert — zurück geht es erst am oberen Rand.'
          : 'Zu warm oder zu feucht für die tiefe Stufe — der T6 läuft auf der Dosierstufe, solange das Klima passt.'
  const setz = <K extends keyof Co2Einstellungen>(feld: K, wert: Co2Einstellungen[K]) => setEntwurf({ ...entwurf, [feld]: wert })
  // Fork AI (forkai.150): Was der Entwurf ergibt — nicht erst nach dem
  // Speichern. Die Basis ist die Obergrenze, die in HA gerade gilt (aus dem
  // Plan, wenn der Wochenplan sie führt), und die Plan-Luft der Woche.
  const rhBasis = live.rhObergrenzeProzent ?? entwurf.rhObergrenzeProzent
  const notbremseErgibt = entwurf.rhNotbremseModus === 'plan'
    ? rhBasis + entwurf.rhNotbremseAbstandProzent
    : entwurf.rhNotbremseFestProzent
  const canopyErgibt = entwurf.canopyObergrenzeModus === 'plan' && live.planLuftTagC != null
    ? live.planLuftTagC + entwurf.canopyObergrenzeAbstandK
    : entwurf.canopyObergrenzeC
  const freiAb = rhBasis - entwurf.klimaHystereseProzent

  return (
    <V1Page
      eyebrow="Steuerung"
      title="CO₂-Begasung"
      subtitle={seite.growName ? `${seite.growName}${seite.phase ? ` · ${seite.phase}` : ''}` : undefined}
      action={geaendert ? <V1Button variant="primary" onClick={speichern} disabled={speichert}>{speichert ? 'Speichert …' : 'Speichern'}</V1Button> : undefined}
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

      {/* Fork AI (forkai.21): Geraete werden auf EINER Seite zugeordnet — hier steht
          nur, wie viele Rollen belegt sind, und der Weg dorthin. Doppelte Pflege
          waere doppelte Wahrheit. */}
      <div className="st-geraete-zeile">
        <span>
          Geräte{' '}
          <b className={seite.geraeteZugeordnet < seite.geraeteGesamt ? 'is-offen' : undefined}>
            {seite.geraeteZugeordnet} / {seite.geraeteGesamt}
          </b>{' '}
          zugeordnet
        </span>
        <V1LinkButton to={rollenPfad()} variant="ghost">Rollen bearbeiten ›</V1LinkButton>
      </div>

      {fehler && <V1Alert tone="critical" message={fehler} />}
      {meldung && <V1Alert tone={meldung.startsWith('Gespeichert und') ? 'ok' : 'warn'} message={meldung} />}
      {!live.haErreichbar && <V1Alert title="Home Assistant antwortet nicht" message="Die Werte sind der letzte bekannte Stand." />}


      <BestandHinweis bestand={bestand} />
      <V1Card>
        <div className="st-jetzt">
          <span className="st-gross">{live.co2Ppm != null ? formatNumber(live.co2Ppm, 0) : '–'}<span>ppm</span></span>
          <span className="st-neben">
            Ziel <b>{live.zielPpm ?? '–'}</b>
            {entwurf.zielQuelle === 'plan' && seite.planPpm != null && <> · {seite.planPpm} ppm aus dem Plan</>}
            <br />
            {live.ventilOffen ? 'Ventil offen' : 'Ventil zu'}
            {live.nachschubUnterPpm != null && <> · nächster Impuls unter {live.nachschubUnterPpm}</>}
          </span>
        </div>
        <div className="st-marken">
          <span className={`st-marke ${live.klimaOk ? 'is-ok' : 'is-warn'}`}>{live.klimaOk ? 'Klima OK' : 'Klima sperrt'}</span>
          {live.t6Stufe != null && <span className="st-marke">T6 Stufe {live.t6Stufe}</span>}
          {live.canopyC != null && <span className="st-marke">Canopy {formatNumber(live.canopyC, 1)} °C</span>}
          {live.rhProzent != null && <span className="st-marke">RH {formatNumber(live.rhProzent, 0)} %</span>}
          {live.vpd != null && <span className="st-marke">VPD {formatNumber(live.vpd, 2)}</span>}
          {live.lichtAn === false && <span className="st-marke">Licht aus</span>}
        </div>
      </V1Card>

      <V1Switch
        label="Automatik aktiv"
        checked={entwurf.automatikAktiv}
        onChange={(v) => setz('automatikAktiv', v)}
        hint="Aus: die Dosier-Automation in Home Assistant wird abgeschaltet. Wächter und Licht-aus-Sicherung laufen weiter."
      />

      <V1Tabs items={CO2_REITER} active={reiter} onChange={setReiter} label="Bereich" insBild />

      {reiter === 'ziel' && (
        <V1Section title="Ziel">
          <V1Card>
            <div className="st-feldzeile">
              <span className="st-etikett">Woher das Ziel kommt</span>
              <div className="st-eingaben">
                <V1Button variant={entwurf.zielQuelle === 'fest' ? 'primary' : 'ghost'} onClick={() => setz('zielQuelle', 'fest')}>Fest</V1Button>
                <V1Button variant={entwurf.zielQuelle === 'plan' ? 'primary' : 'ghost'} onClick={() => setz('zielQuelle', 'plan')}>Plan</V1Button>
              </div>
            </div>

            {entwurf.zielQuelle === 'plan' ? (
              <>
                <div className="st-feldzeile">
                  <span className="st-etikett">
                    Aus dem Sollwertprofil
                    <small>{seite.planHerkunft ?? 'Kein laufender Grow — es gelten die festen Werte.'}</small>
                  </span>
                  <span className="st-nurlesen">{seite.planPpm ?? '–'}</span>
                </div>
                <Zahl label="unter 25 °C" einheit="%" wert={entwurf.anteilKuehlProzent} onChange={(v) => setz('anteilKuehlProzent', v)} fehler={feldFehler.AnteilKuehlProzent} />
                <Zahl label="25 – 27 °C" einheit="%" wert={entwurf.anteilMittelProzent} onChange={(v) => setz('anteilMittelProzent', v)} fehler={feldFehler.AnteilMittelProzent} />
                <Zahl label="ab 27 °C" einheit="%" wert={entwurf.anteilWarmProzent} onChange={(v) => setz('anteilWarmProzent', v)} fehler={feldFehler.AnteilWarmProzent} />
              </>
            ) : (
              <>
                <Zahl label="unter 25 °C" einheit="ppm" wert={entwurf.zielKuehlPpm} onChange={(v) => setz('zielKuehlPpm', v)} fehler={feldFehler.ZielKuehlPpm} />
                <Zahl label="25 – 27 °C" einheit="ppm" wert={entwurf.zielMittelPpm} onChange={(v) => setz('zielMittelPpm', v)} fehler={feldFehler.ZielMittelPpm} />
                <Zahl label="ab 27 °C" einheit="ppm" wert={entwurf.zielWarmPpm} onChange={(v) => setz('zielWarmPpm', v)} fehler={feldFehler.ZielWarmPpm} />
              </>
            )}

            <Zahl label="Hysterese" hinweis="Nachschub erst, wenn der Wert so weit unter dem Ziel liegt." einheit="ppm" wert={entwurf.hysteresePpm} onChange={(v) => setz('hysteresePpm', v)} fehler={feldFehler.HysteresePpm} />
            <p className="st-hinweis">Ergibt {ziele.kuehl} / {ziele.mittel} / {ziele.warm} ppm.</p>
          </V1Card>
        </V1Section>
      )}

      {reiter === 'dosierung' && (
        <V1Section title="Dosierung">
          <V1Card>
            <Zahl label="Impuls kürzestens" einheit="s" wert={entwurf.impulsMinSekunden} onChange={(v) => setz('impulsMinSekunden', v)} fehler={feldFehler.ImpulsMinSekunden} />
            <Zahl label="Impuls längstens" hinweis="Der Wächter schließt das Ventil ab 90 s zwangsweise." einheit="s" wert={entwurf.impulsMaxSekunden} onChange={(v) => setz('impulsMaxSekunden', v)} fehler={feldFehler.ImpulsMaxSekunden} />
            <Zahl label="Wartezeit nach Impuls" hinweis="Der Sensor hinkt rund zwei Minuten nach." einheit="s" wert={entwurf.wartezeitSekunden} onChange={(v) => setz('wartezeitSekunden', v)} fehler={feldFehler.WartezeitSekunden} />
            <Zahl label="Impulse je Zyklus höchstens" wert={entwurf.maxImpulseJeZyklus} onChange={(v) => setz('maxImpulseJeZyklus', v)} fehler={feldFehler.MaxImpulseJeZyklus} />
            <Zahl label="Zeltvolumen" einheit="m³" schritt={0.01} wert={entwurf.zeltvolumenM3} onChange={(v) => setz('zeltvolumenM3', v)} fehler={feldFehler.ZeltvolumenM3} />
            <div className="st-feldzeile">
              <span className="st-etikett">
                Durchfluss
                <small>Aus dem ppm-Anstieg je Impuls — letzte Messung {live.letzteMessungGps != null ? formatNumber(live.letzteMessungGps, 4) : '–'} g/s</small>
              </span>
              <span className="st-nurlesen">{live.grammProSekunde != null ? formatNumber(live.grammProSekunde, 3) : '–'} g/s</span>
            </div>
            <div className="st-feldzeile">
              <span className="st-etikett">Flasche</span>
              <span className="st-nurlesen">{live.flascheRestKg != null ? `${formatNumber(live.flascheRestKg, 2)} kg` : '–'}</span>
            </div>
            <V1Switch label="Autokalibrierung" checked={entwurf.autokalibrierung} onChange={(v) => setz('autokalibrierung', v)} hint="Führt den Durchfluss aus dem gemessenen ppm-Anstieg nach." />
          </V1Card>
        </V1Section>
      )}

      {reiter === 'klima' && (
        <V1Section title="Klima hat Vorrang">
          <V1Card>
            <div className="st-feldzeile">
              <span className="st-etikett">Abluft T6 jetzt</span>
              <span className="st-nurlesen">{live.t6Stufe != null ? `Stufe ${live.t6Stufe}` : '–'}</span>
            </div>
            <div className="st-feldzeile">
              <span className="st-etikett">
                Canopy
                <small>tief nur bis {live.tiefBisTempC != null ? `${formatNumber(live.tiefBisTempC, 1)} °C` : '–'}</small>
              </span>
              <span className="st-eingaben">
                <span className="st-nurlesen">{live.canopyC != null ? `${formatNumber(live.canopyC, 1)} °C` : '–'}</span>
                {tempOk != null && <span className={`st-marke ${tempOk ? 'is-ok' : 'is-warn'}`}>{tempOk ? 'erfüllt' : 'zu warm'}</span>}
              </span>
            </div>
            <div className="st-feldzeile">
              <span className="st-etikett">
                Feuchte
                <small>tief nur bis {live.tiefBisRhProzent != null ? `${formatNumber(live.tiefBisRhProzent, 0)} %` : '–'}</small>
              </span>
              <span className="st-eingaben">
                <span className="st-nurlesen">{live.rhProzent != null ? `${formatNumber(live.rhProzent, 1)} %` : '–'}</span>
                {rhOk != null && <span className={`st-marke ${rhOk ? 'is-ok' : 'is-warn'}`}>{rhOk ? 'erfüllt' : 'zu feucht'}</span>}
              </span>
            </div>
            {live.rhMittelVorhanden && (
              <div className="st-feldzeile">
                <span className="st-etikett">
                  Feuchte im Mittel
                  <small>entscheidet über die Freigabe · frei ab {live.freiAbProzent != null ? `${formatNumber(live.freiAbProzent, 1)} %` : '–'}</small>
                </span>
                <span className="st-nurlesen">{live.rhMittelProzent != null ? `${formatNumber(live.rhMittelProzent, 1)} %` : '–'}</span>
              </div>
            )}
            <p className="st-hinweis">{tiefGrund}</p>
          </V1Card>
          <V1Card>
            <h3 className="st-gruppe">Sperre</h3>
            {live.rhObergrenzeAusPlan != null ? (
              /* forkai.115 (F-013): Der Wochenplan führt die Obergrenze. Ein
                 Eingabefeld hier würde beim Speichern nichts bewirken — gepflegt
                 wird sie im Plan des Grows (Menü Plan). */
              <div className="st-feldzeile">
                <span className="st-etikett">
                  Feuchte-Obergrenze
                  <small>Kommt aus dem Plan des Grows, ändern unter Plan.</small>
                </span>
                <span className="st-nurlesen">{formatNumber(live.rhObergrenzeProzent ?? live.rhObergrenzeAusPlan, 0)} %</span>
              </div>
            ) : (
              <Zahl label="Feuchte-Obergrenze" hinweis="Darüber wird gesperrt — der Plan gibt sie je Blütewoche vor." einheit="%" schritt={0.5} wert={entwurf.rhObergrenzeProzent} onChange={(v) => setz('rhObergrenzeProzent', v)} fehler={feldFehler.RhObergrenzeProzent} />
            )}
            <Zahl label="Sperrt nach" hinweis="So lange muss die Feuchte über der Grenze liegen. Überbrückt kurze Spitzen, etwa die Kompressorpause des Entfeuchters." einheit="min" wert={entwurf.klimaToleranzMinuten ?? Number.NaN} onChange={(v) => setz('klimaToleranzMinuten', v)} fehler={feldFehler.KlimaToleranzMinuten} />
            <PlanGrenze
              label="Notbremse Feuchte"
              hinweis={entwurf.rhNotbremseModus === 'plan'
                ? 'Darüber sperrt es sofort. Folgt der Feuchte-Obergrenze.'
                : 'Darüber sperrt es sofort, ohne Wartezeit.'}
              einheit="%"
              modus={entwurf.rhNotbremseModus}
              onModus={(m) => setz('rhNotbremseModus', m)}
              abstand={entwurf.rhNotbremseAbstandProzent}
              onAbstand={(v) => setz('rhNotbremseAbstandProzent', v)}
              fest={entwurf.rhNotbremseFestProzent ?? Number.NaN}
              onFest={(v) => setz('rhNotbremseFestProzent', v)}
              schritt={0.5}
              ergebnis={notbremseErgibt}
              fehlerAbstand={feldFehler.RhNotbremseAbstandProzent}
              fehlerFest={feldFehler.RhNotbremseFestProzent}
            />
            <PlanGrenze
              label="Canopy-Obergrenze"
              hinweis={entwurf.canopyObergrenzeModus === 'plan'
                ? (live.planLuftTagC != null
                  ? `Darüber sperrt es. Folgt der Lufttemperatur aus dem Plan (${live.planWoche ?? 'diese Woche'}: ${formatNumber(live.planLuftTagC, 1)} °C).`
                  : 'Der Plan nennt keine Lufttemperatur — es gilt der feste Wert.')
                : 'Darüber sperrt es.'}
              einheit="°C"
              abstandEinheit="K"
              modus={entwurf.canopyObergrenzeModus}
              onModus={(m) => setz('canopyObergrenzeModus', m)}
              abstand={entwurf.canopyObergrenzeAbstandK}
              onAbstand={(v) => setz('canopyObergrenzeAbstandK', v)}
              fest={entwurf.canopyObergrenzeC}
              onFest={(v) => setz('canopyObergrenzeC', v)}
              schritt={0.5}
              ergebnis={canopyErgibt}
              fehlerAbstand={feldFehler.CanopyObergrenzeAbstandK}
              fehlerFest={feldFehler.CanopyObergrenzeC}
            />

            <h3 className="st-gruppe">Freigabe</h3>
            <Zahl label="Wieder frei ab" hinweis="Abstand unter der Obergrenze, gemessen am Mittelwert der Feuchte." einheit="%" schritt={0.5} wert={entwurf.klimaHystereseProzent} onChange={(v) => setz('klimaHystereseProzent', v)} fehler={feldFehler.KlimaHystereseProzent} />
            <Zahl label="Mittelwert über" hinweis="Glättet das Rauschen der Sonde. Länger = ruhiger, aber spätere Freigabe." einheit="min" wert={entwurf.rhMittelMinuten ?? Number.NaN} onChange={(v) => setz('rhMittelMinuten', v)} fehler={feldFehler.RhMittelMinuten} />
            {!live.rhMittelVorhanden && (
              <p className="st-hinweis">Der Mittelwert-Helfer fehlt in Home Assistant — die Freigabe hängt am Momentanwert. Anlegen unten unter „Was in Home Assistant fehlt“.</p>
            )}
            <p className="st-regel">
              sperrt: Feuchte über {fmtProzent(rhBasis)} für {entwurf.klimaToleranzMinuten ?? '–'} min · sofort über {fmtProzent(notbremseErgibt)}<br />
              frei: Mittel ({entwurf.rhMittelMinuten ?? '–'} min) höchstens {fmtProzent(freiAb)}
            </p>

            <h3 className="st-gruppe">Abluft T6</h3>
            <V1Switch label="Abluft beim Dosieren drosseln" checked={entwurf.abluftDrosseln} onChange={(v) => setz('abluftDrosseln', v)} hint="Ohne Drosselung bläst der T6 das CO₂ hinaus, während dosiert wird." />
            <Zahl label="T6 normal" wert={entwurf.t6StufeNormal} onChange={(v) => setz('t6StufeNormal', v)} fehler={feldFehler.T6StufeNormal} />
            <Zahl label="T6 beim Dosieren" wert={entwurf.t6StufeDosierung} onChange={(v) => setz('t6StufeDosierung', v)} fehler={feldFehler.T6StufeDosierung} />
            <Zahl label="T6 bei Klimasperre" hinweis="Zwischenstufe, solange das Klima sperrt — trocknet, ohne alles CO₂ hinauszublasen." wert={entwurf.t6StufeKlima ?? Number.NaN} onChange={(v) => setz('t6StufeKlima', v)} fehler={feldFehler.T6StufeKlima} />
            <Zahl label="T6 tief" hinweis="Nur bei kühler und trockener Luft — sonst staut sich Feuchte." wert={entwurf.t6StufeTief} onChange={(v) => setz('t6StufeTief', v)} fehler={feldFehler.T6StufeTief} />
            <Zahl label="Stufe tief nur bis" einheit="°C" schritt={0.5} wert={entwurf.t6TiefMaxTempC} onChange={(v) => setz('t6TiefMaxTempC', v)} fehler={feldFehler.T6TiefMaxTempC} />
          </V1Card>
        </V1Section>
      )}

      {reiter === 'zeiten' && (
        <V1Section title="Zeiten">
          <V1Card>
            <Zahl label="Start nach Licht an" hinweis="Die Photosynthese braucht rund 15 bis 30 Minuten, bis sie voll läuft." einheit="min" wert={entwurf.startNachLichtAnMinuten} onChange={(v) => setz('startNachLichtAnMinuten', v)} fehler={feldFehler.StartNachLichtAnMinuten} />
            <Zahl label="Ende vor Licht aus" hinweis="Gerechnet gegen die geplante Aus-Zeit des Licht-Controllers." einheit="min" schritt={5} wert={entwurf.endeVorLichtAusMinuten} onChange={(v) => setz('endeVorLichtAusMinuten', v)} fehler={feldFehler.EndeVorLichtAusMinuten} />
            <p className="st-hinweis">Beide Zeiten gehen als Helfer nach Home Assistant; die Automation rechnet ihr Fenster daraus.</p>
          </V1Card>
        </V1Section>
      )}

      {reiter === 'heute' && (
        <V1Section title="Heute und die letzten Tage">
          <V1Card>
            <div className="st-feldzeile">
              <span className="st-etikett">Impulse heute</span>
              <span className="st-nurlesen">{live.impulseHeute ?? '–'}</span>
            </div>
            <div className="st-feldzeile">
              <span className="st-etikett">Kosten-Artikel<small>Die Tagessumme wird abends darauf gebucht.</small></span>
              <div className="st-eingaben">
                <select
                  value={entwurf.kostenArtikelId ?? ''}
                  onChange={(e) => setz('kostenArtikelId', e.target.value === '' ? null : Number(e.target.value))}
                >
                  <option value="">nur anzeigen</option>
                  {seite.artikel.map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}
                </select>
              </div>
            </div>
            <V1Switch label="In die Chronik schreiben" checked={entwurf.journalBuchen} onChange={(v) => setz('journalBuchen', v)} hint="Ein Eintrag je Tag mit Impulsen, Ventilzeit und Gramm." />
          </V1Card>

          {seite.tage.length === 0 ? (
            <V1Empty title="Noch keine Tage" text="Der erste Tag wird abends abgeschlossen." />
          ) : (
            <div className="st-tage-huelle">
              <table className="st-tage">
                <thead>
                  <tr>
                    <th scope="col">Tag</th>
                    <th scope="col">Impulse</th>
                    <th scope="col">Ventil</th>
                    <th scope="col">CO₂</th>
                    <th scope="col">Ziel ab</th>
                  </tr>
                </thead>
                <tbody>
                  {seite.tage.map((t) => (
                    <tr key={t.datum}>
                      <th scope="row">
                        {tagKurz(t.datum)}
                        {t.flaschenwechsel && <small>Flasche gewechselt</small>}
                      </th>
                      <td>{t.impulse}</td>
                      <td>{minuten(t.ventilSekunden)}</td>
                      <td>{formatNumber(t.gramm, 0)} g</td>
                      <td>{t.zielErreichtUm ?? '–'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </V1Section>
      )}

      <BestandAbschnitt modul="co2" schaltet="das Ventil" bestand={bestand} neuLaden={bestandNeuLaden}>
        <V1Card>
          <div className="st-feldzeile">
            <span className="st-etikett">
              Probeschaltung
              <small>
                Öffnet das Ventil zwei Sekunden und sieht nach, ob der Port reagiert. Zwei Sekunden CO₂
                sind harmlos — ein Ventil, das nur meldet zu schalten, ist es nicht.
              </small>
            </span>
          </div>
          {probe && <p className="st-hinweis">{probe}</p>}
          <V1Button variant="ghost" onClick={ventilProbieren}>Ventil kurz öffnen</V1Button>
        </V1Card>
      </BestandAbschnitt>

      {absicherung?.erreichbar && (
        <V1Section title="Sicherheit der CO₂-Regelung">
          <V1Card>
            {absicherung.automationen.length === 0 ? (
              <p className="st-hinweis">In Home Assistant ist keine CO₂-Automation zu finden.</p>
            ) : absicherung.automationen.map((a) => (
              <div className="st-feldzeile is-gestapelt" key={a.entityId}>
                <span className="st-etikett">
                  {a.name}
                  {a.befunde.length === 0 && <small>Keine der vier bekannten Schwachstellen gefunden.</small>}
                </span>
                {a.befunde.map((b) => (
                  <p className="st-befund" key={b.art}>
                    <b>{b.titel}</b>
                    {b.erklaerung}
                    {b.hinweis && <em>{b.hinweis}</em>}
                  </p>
                ))}
              </div>
            ))}
            {absicherung.rechenwerte.map((r) => (
              <div className="st-feldzeile is-gestapelt" key={r.entityId}>
                <span className="st-etikett">
                  {r.name}
                  <small>
                    {r.stand === 'Aktuell' ? 'Aktuelle Formel — schweigt der Fühler, wird nicht dosiert.'
                      : r.stand === 'Veraltet' ? 'Rechenwert mit älterer Formel'
                      : r.stand === 'Angepasst' ? 'Von Hand angepasste Formel'
                      : r.stand === 'AndererFuehler' ? 'Formel mit anderem Fühler als zugeordnet'
                      : 'Formel nicht geprüft'}
                  </small>
                </span>
                {r.stand === 'Veraltet' && (
                  <p className="st-befund">
                    <b>Rechnet weiter, wenn der CO₂-Fühler schweigt</b>
                    Heute: {r.heute}. Nach dem Absichern: {r.danach}. Mit gültigem Messwert rechnet er wie bisher.
                  </p>
                )}
                {r.hinweis && <p className="st-hinweis">{r.hinweis}</p>}
                {r.alteFormel && r.neueFormel && (
                  <details className="st-formeln">
                    <summary>Formeln vergleichen</summary>
                    <span>In Home Assistant</span>
                    <code>{r.alteFormel}</code>
                    <span>Aktuelle Fassung von Fork AI</span>
                    <code>{r.neueFormel}</code>
                  </details>
                )}
              </div>
            ))}
            {absicherung.hinweis && <p className="st-hinweis">{absicherung.hinweis}</p>}
            {absicherMeldung && <V1Alert tone={absicherMeldung.ton} message={absicherMeldung.text} />}
            {absicherung.behebbar > 0 && (
              <>
                <p className="st-hinweis">
                  Geändert wird nur die schwache Stelle — alles andere in deinen Automationen und Rechenwerten
                  bleibt, wie es ist. Vorher wird alles gesichert, danach aus Home Assistant nachgelesen.
                </p>
                {/* Nach einer Ablehnung steht derselbe Grund schon in der Meldung darüber. */}
                {absicherung.dosiertGerade && !absicherMeldung && (
                  <p className="st-hinweis">
                    Gerade läuft eine Dosierung oder das Ventil steht offen. Neu schreiben würde sie mitten im
                    Impuls abbrechen — absichern geht in einer Pause oder bei Licht aus.
                  </p>
                )}
                <V1Button variant="primary" onClick={absichern} disabled={sichertAb || absicherung.dosiertGerade}>
                  {sichertAb ? 'Sichert ab …' : `Absichern (${absicherung.behebbar} ${absicherung.behebbar === 1 ? 'Stelle' : 'Stellen'})`}
                </V1Button>
              </>
            )}
          </V1Card>
        </V1Section>
      )}

      <p className="st-fuss">Die Regelung selbst läuft in Home Assistant</p>
    </V1Page>
  )
}

/** Eine Zahlenzeile — Etikett links, Eingabe rechts, Fehler darunter. */
function fmtProzent(wert: number | null | undefined): string {
  return wert != null && Number.isFinite(wert) ? `${formatNumber(wert, wert % 1 === 0 ? 0 : 1)} %` : '–'
}

/**
 * Fork AI (forkai.150): Eine Grenze, die fest steht oder der Plan-Woche folgt
 * (Plan + Abstand) — wie „Temperatur max." beim Entfeuchter. Darunter steht,
 * was gerade gilt, damit niemand die Summe im Kopf bilden muss.
 */
function PlanGrenze({ label, hinweis, einheit, abstandEinheit, modus, onModus, abstand, onAbstand, fest, onFest, schritt, ergebnis, fehlerAbstand, fehlerFest }: {
  label: string
  hinweis: string
  einheit: string
  abstandEinheit?: string
  modus: GrenzModus
  onModus: (modus: GrenzModus) => void
  abstand: number
  onAbstand: (wert: number) => void
  fest: number
  onFest: (wert: number) => void
  schritt: number
  ergebnis: number | null | undefined
  fehlerAbstand?: string
  fehlerFest?: string
}) {
  const wert = modus === 'plan' ? abstand : fest
  // Ein Fehler am Feld, das der Modus ausblendet, nennt Feld und Modus —
  // sonst stand die Markierung neben einem gefüllten Feld.
  const fehler = modusFehler(modus, {
    plan: { feld: `${label}, Abstand zum Plan`, modus: 'Plan +', fehler: fehlerAbstand },
    fest: { feld: `${label}, fester Wert`, modus: 'Fest', fehler: fehlerFest },
  })
  return (
    <div className="st-feldzeile">
      <span className="st-etikett">
        {label}
        <small>{hinweis}</small>
        {fehler && <span className="st-fehler">{fehler}</span>}
      </span>
      <span className="st-plangrenze">
        <span className="st-eingaben">
          <V1Button variant={modus === 'fest' ? 'primary' : 'ghost'} onClick={() => onModus('fest')}>Fest</V1Button>
          <V1Button variant={modus === 'plan' ? 'primary' : 'ghost'} onClick={() => onModus('plan')}>Plan +</V1Button>
        </span>
        <span className="st-eingaben">
          <input
            type="number"
            inputMode="decimal"
            step={schritt}
            value={Number.isFinite(wert) ? wert : ''}
            aria-label={modus === 'plan' ? `${label}, Abstand zum Plan` : label}
            aria-invalid={fehler ? true : undefined}
            onChange={(e) => {
              const v = zahlAusFeld(e.target.value)
              if (v == null) return
              if (modus === 'plan') onAbstand(v)
              else onFest(v)
            }}
          />
          <span className="st-einheit">{modus === 'plan' ? (abstandEinheit ?? einheit) : einheit}</span>
        </span>
        {ergebnis != null && Number.isFinite(ergebnis) && <small className="st-ergebnis">ergibt {formatNumber(ergebnis, ergebnis % 1 === 0 ? 0 : 1)} {einheit}</small>}
      </span>
    </div>
  )
}

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
          inputMode="decimal"
          step={schritt}
          value={Number.isFinite(wert) ? wert : ''}
          aria-label={label}
          aria-invalid={fehler ? true : undefined}
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
