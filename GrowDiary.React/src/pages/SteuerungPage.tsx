import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { apiFetch, formatApiError } from '../api'
import { V1Alert, V1Button, V1Card, V1Empty, V1LinkButton, V1Page, V1Section, V1Skeleton, V1Switch, V1Tabs } from '../components/v1'
import { CO2_REITER, minuten, wirksameZiele } from '../features/steuerung/steuerung-typen'
import type { Co2Einstellungen, Co2Reiter, Co2Seite, SteuerungModul, SteuerungUebersicht } from '../features/steuerung/steuerung-typen'
import { formatNumber } from '../utils'
import '../features/steuerung/steuerung.css'

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
  const [fehler, setFehler] = useState<string | null>(null)
  const [feldFehler, setFeldFehler] = useState<Record<string, string>>({})
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

  const geaendert = useMemo(
    () => Boolean(seite && entwurf) && JSON.stringify(seite?.einstellungen) !== JSON.stringify(entwurf),
    [seite, entwurf],
  )

  const speichern = async () => {
    if (!entwurf) return
    setSpeichert(true); setMeldung(null); setFeldFehler({})
    try {
      const zurueck = await apiFetch<Co2Seite>('/api/steuerung/co2', { method: 'PUT', body: JSON.stringify(entwurf) })
      setSeite(zurueck); setEntwurf(zurueck.einstellungen); setFehler(null)
      setMeldung(zurueck.haAngenommen === false
        ? 'Gespeichert — aber Home Assistant hat nicht alle Sollwerte angenommen. Die Regelung läuft mit den alten Werten weiter.'
        : 'Gespeichert und nach Home Assistant geschrieben.')
    } catch (caught) {
      const felder = (caught as { fields?: Record<string, string> })?.fields
      if (felder) { setFeldFehler(felder); setFehler('Bitte die markierten Felder prüfen.') }
      else setFehler(formatApiError(caught, 'Speichern fehlgeschlagen.'))
    } finally {
      setSpeichert(false)
    }
  }

  if (laedt && !seite) return <V1Page title="CO₂-Begasung"><V1Skeleton rows={6} label="Wird geladen" /></V1Page>
  if (!seite || !entwurf) {
    return <V1Page title="CO₂-Begasung">{fehler && <V1Alert tone="critical" message={fehler} />}</V1Page>
  }

  const live = seite.live
  const ziele = wirksameZiele(entwurf, seite.planPpm)
  const setz = <K extends keyof Co2Einstellungen>(feld: K, wert: Co2Einstellungen[K]) => setEntwurf({ ...entwurf, [feld]: wert })

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
        <V1LinkButton to="/steuerung/geraete" variant="ghost">Geräte &amp; Entitäten ›</V1LinkButton>
      </div>

      {fehler && <V1Alert tone="critical" message={fehler} />}
      {meldung && <V1Alert tone={meldung.startsWith('Gespeichert und') ? 'ok' : 'warn'} message={meldung} />}
      {!live.haErreichbar && <V1Alert title="Home Assistant antwortet nicht" message="Die Werte sind der letzte bekannte Stand." />}

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

      <V1Tabs items={CO2_REITER} active={reiter} onChange={setReiter} label="Bereich" />

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
            <Zahl label="Feuchte-Obergrenze" hinweis="Darüber wird nicht dosiert — der Plan gibt sie je Blütewoche vor." einheit="%" schritt={0.5} wert={entwurf.rhObergrenzeProzent} onChange={(v) => setz('rhObergrenzeProzent', v)} fehler={feldFehler.RhObergrenzeProzent} />
            <Zahl label="Wieder frei ab" hinweis="Abstand unter der Obergrenze, damit es nicht flattert." einheit="%" schritt={0.5} wert={entwurf.klimaHystereseProzent} onChange={(v) => setz('klimaHystereseProzent', v)} fehler={feldFehler.KlimaHystereseProzent} />
            <Zahl label="Canopy-Obergrenze" einheit="°C" schritt={0.5} wert={entwurf.canopyObergrenzeC} onChange={(v) => setz('canopyObergrenzeC', v)} fehler={feldFehler.CanopyObergrenzeC} />
            <V1Switch label="Abluft beim Dosieren drosseln" checked={entwurf.abluftDrosseln} onChange={(v) => setz('abluftDrosseln', v)} hint="Ohne Drosselung bläst der T6 das CO₂ hinaus, während dosiert wird." />
            <Zahl label="T6 normal" wert={entwurf.t6StufeNormal} onChange={(v) => setz('t6StufeNormal', v)} fehler={feldFehler.T6StufeNormal} />
            <Zahl label="T6 beim Dosieren" wert={entwurf.t6StufeDosierung} onChange={(v) => setz('t6StufeDosierung', v)} fehler={feldFehler.T6StufeDosierung} />
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
                        {t.datum}
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

      <p className="st-fuss">Die Regelung selbst läuft in Home Assistant</p>
    </V1Page>
  )
}

/** Eine Zahlenzeile — Etikett links, Eingabe rechts, Fehler darunter. */
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
          onChange={(e) => onChange(e.target.value === '' ? Number.NaN : Number(e.target.value))}
        />
        {einheit && <span className="st-einheit">{einheit}</span>}
      </span>
    </div>
  )
}
