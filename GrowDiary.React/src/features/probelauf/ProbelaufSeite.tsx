import { useCallback, useEffect, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { apiFetch, formatApiError } from '../../api'
import { V1Alert, V1Button, V1Page, V1Skeleton } from '../../components/v1'
import type { ProbelaufGrenzen, ProbelaufLauf, ProbelaufModul } from '../../types'
import { formatNumber } from '../../utils'
import { feldText } from '../../zahlenfeld'
import '../steuerung/steuerung.css'
import './probelauf.css'
import { KenntnisstandAnsicht } from './KenntnisstandAnsicht'
import { ProbelaufKurve } from './ProbelaufKurve'
import {
  erholungText, grenzenAusText, istOffen, jetztWerte, proMinuteText, restzeit, startText, statusText, wertText,
  type GrenzenText,
} from './probelauf-anzeige'

/**
 * Fork AI (A-010, 07.10.2026): Probelauf — eine Steuerung für einige Minuten abschalten und sehen,
 * wie sich Temperatur, Luftfeuchte und VPD entwickeln.
 *
 * Eine einzige Seite: ohne offenen Lauf das Formular (Gerät, Dauer, Start), mit offenem Lauf der Stand
 * mit „Abbrechen", danach die Auswertung. Die Grenzen sind aus den Pflanzenzielen vorbelegt und unter
 * „Erweitert" eingeklappt.
 *
 * <b>Namen.</b> Die Titel der Steuerungen kommen vom Server (zentral gepflegt) — nichts davon ist hier
 * eingetippt.
 */

const DAUERN = [10, 20, 30]
const TAKT_MS = 5000

const aus = (g: ProbelaufGrenzen | null): GrenzenText => ({
  feuchteMax: feldText(g?.feuchteMax), tempMax: feldText(g?.tempMax), vpdMin: feldText(g?.vpdMin), vpdMax: feldText(g?.vpdMax),
})

export default function ProbelaufSeite() {
  const [params, setParams] = useSearchParams()
  const [module, setModule] = useState<ProbelaufModul[]>([])
  const [liste, setListe] = useState<ProbelaufLauf[] | null>(null)
  const [detail, setDetail] = useState<ProbelaufLauf | null>(null)
  const [fehler, setFehler] = useState<string | null>(null)
  const [modul, setModul] = useState('entfeuchter')
  const [dauer, setDauer] = useState(20)
  const [voreinstellung, setVoreinstellung] = useState<ProbelaufGrenzen | null>(null)
  const [grenzen, setGrenzen] = useState<GrenzenText>({ feuchteMax: '', tempMax: '', vpdMin: '', vpdMax: '' })
  const [sendet, setSendet] = useState(false)

  const offen = useMemo(() => liste?.find((l) => istOffen(l.status)) ?? null, [liste])
  const gewaehltId = Number(params.get('lauf')) || offen?.id || null
  const kenntnis = params.get('ansicht') === 'kenntnis'

  const ladeListe = useCallback(async () => {
    try {
      setListe(await apiFetch<ProbelaufLauf[]>('/api/steuerung/probelauf'))
    } catch (caught) {
      setFehler(formatApiError(caught, 'Die Probeläufe konnten nicht geladen werden.'))
    }
  }, [])

  useEffect(() => {
    let abgebrochen = false
    apiFetch<ProbelaufLauf[]>('/api/steuerung/probelauf')
      .then((l) => { if (!abgebrochen) setListe(l) })
      .catch((caught) => { if (!abgebrochen) setFehler(formatApiError(caught, 'Die Probeläufe konnten nicht geladen werden.')) })
    apiFetch<ProbelaufModul[]>('/api/steuerung/probelauf/module')
      .then((m) => { if (!abgebrochen) setModule(m) })
      .catch(() => undefined)
    apiFetch<ProbelaufGrenzen>('/api/steuerung/probelauf/voreinstellung')
      .then((g) => { if (!abgebrochen) { setVoreinstellung(g); setGrenzen(aus(g)) } })
      .catch(() => undefined) // ohne Voreinstellung bleiben die Felder leer — der Nutzer trägt sie ein
    return () => { abgebrochen = true }
  }, [])

  const ladeDetail = useCallback(async (id: number) => {
    try {
      setDetail(await apiFetch<ProbelaufLauf>(`/api/steuerung/probelauf/${id}`))
    } catch (caught) {
      setFehler(formatApiError(caught, 'Dieser Probelauf konnte nicht geladen werden.'))
    }
  }, [])

  useEffect(() => {
    if (gewaehltId == null) return
    let abgebrochen = false
    apiFetch<ProbelaufLauf>(`/api/steuerung/probelauf/${gewaehltId}`)
      .then((l) => { if (!abgebrochen) setDetail(l) })
      .catch((caught) => { if (!abgebrochen) setFehler(formatApiError(caught, 'Dieser Probelauf konnte nicht geladen werden.')) })
    return () => { abgebrochen = true }
  }, [gewaehltId])

  // Solange ein Lauf nicht abgeschlossen ist, wird im Takt nachgesehen.
  useEffect(() => {
    if (!offen) return
    const uhr = setInterval(() => { void ladeListe(); void ladeDetail(offen.id) }, TAKT_MS)
    return () => clearInterval(uhr)
  }, [offen, ladeListe, ladeDetail])

  async function starten() {
    setFehler(null)
    const gelesen = grenzenAusText(grenzen)
    if ('fehler' in gelesen) { setFehler(gelesen.fehler); return }
    const g = gelesen.grenzen
    setSendet(true)
    try {
      const lauf = await apiFetch<ProbelaufLauf>('/api/steuerung/probelauf', {
        method: 'POST', body: JSON.stringify({ modul, dauerMinuten: dauer, grenzen: g }),
      })
      setParams({}, { replace: true })
      setDetail(lauf)
      await ladeListe()
    } catch (caught) {
      setFehler(formatApiError(caught, 'Der Probelauf konnte nicht gestartet werden.'))
      await ladeListe()
    } finally {
      setSendet(false)
    }
  }

  async function abbrechen(id: number) {
    setFehler(null)
    setSendet(true)
    try {
      setDetail(await apiFetch<ProbelaufLauf>(`/api/steuerung/probelauf/${id}/abbrechen`, { method: 'POST' }))
      await ladeListe()
    } catch (caught) {
      setFehler(formatApiError(caught, 'Der Probelauf konnte nicht abgebrochen werden.'))
    } finally {
      setSendet(false)
    }
  }

  const angezeigt = gewaehltId != null && detail?.id === gewaehltId ? detail : null

  if (liste == null && !fehler) return <V1Page eyebrow="Betrieb" title="Probelauf"><V1Skeleton rows={4} label="Lade Probeläufe" /></V1Page>

  return (
    <V1Page eyebrow="Betrieb" title="Probelauf" subtitle="Ein Gerät für einige Minuten abschalten und sehen, wie sich Temperatur, Luftfeuchte und VPD entwickeln.">
      <div className="pl-seite" data-audit="probelauf-seite">
        {fehler && <V1Alert title="Probelauf" message={fehler} tone="warn" />}

        <div className="st-wechsel" role="group" aria-label="Ansicht">
          <button type="button" className="st-chip" aria-current={!kenntnis} onClick={() => setParams({}, { replace: true })}>Probelauf</button>
          <button type="button" className="st-chip" aria-current={kenntnis} onClick={() => setParams({ ansicht: 'kenntnis' }, { replace: true })}>Kenntnisstand</button>
        </div>

        {kenntnis ? <KenntnisstandAnsicht onVorbereiten={(m) => { setModul(m); setDauer(10); setParams({}, { replace: true }) }} /> : <>

        {angezeigt
          ? <LaufAnsicht lauf={angezeigt} sendet={sendet} onAbbrechen={() => void abbrechen(angezeigt.id)} onZurueck={() => setParams({}, { replace: true })} offen={offen != null} />
          : (
            <Formular
              module={module} modul={modul} setModul={setModul} dauer={dauer} setDauer={setDauer}
              grenzen={grenzen} setGrenzen={setGrenzen} voreinstellung={voreinstellung}
              sendet={sendet} onStart={() => void starten()}
            />
          )}

        {liste && liste.length > 0 && (
          <div className="pl-karte" data-audit="probelauf-liste">
            <h2>Frühere Läufe</h2>
            <table className="pl-tabelle">
              <thead><tr><th>Wann</th><th>Gerät</th><th>Status</th><th /></tr></thead>
              <tbody>
                {liste.map((l) => (
                  <tr key={l.id}>
                    <td>{startText(l.startUtc)}</td>
                    <td>{l.modulTitel}</td>
                    <td><span className={`pl-marke${l.status === 'Fertig' ? ' is-ok' : l.status === 'Laeuft' || l.status === 'Nachlauf' ? '' : ' is-warn'}`}>{statusText(l.status)}</span></td>
                    <td><button type="button" onClick={() => setParams({ lauf: String(l.id) })}>ansehen</button></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        </>}
      </div>
    </V1Page>
  )
}

// ------------------------------------------------------------------ Formular

function Formular(p: {
  module: ProbelaufModul[]; modul: string; setModul: (m: string) => void; dauer: number; setDauer: (d: number) => void
  grenzen: GrenzenText; setGrenzen: (g: GrenzenText) => void; voreinstellung: ProbelaufGrenzen | null
  sendet: boolean; onStart: () => void
}) {
  const gewaehlt = p.module.find((m) => m.modul === p.modul)
  const feld = (name: keyof GrenzenText, beschriftung: string, vorgabe: number | null | undefined) => (
    <label>
      {beschriftung}
      <input
        inputMode="decimal" value={p.grenzen[name]}
        onChange={(e) => p.setGrenzen({ ...p.grenzen, [name]: e.target.value })}
        aria-label={beschriftung}
      />
      {vorgabe != null && <em>Empfohlen · Pflanzenziel plus Spielraum</em>}
    </label>
  )

  return (
    <>
      <div className="pl-karte" data-audit="probelauf-geraet">
        <h2>1 · Was soll aus?</h2>
        <div className="st-wechsel" role="group" aria-label="Gerät">
          {p.module.map((m) => (
            <button key={m.modul} type="button" className="st-chip" aria-current={m.modul === p.modul} onClick={() => p.setModul(m.modul)}>{m.titel}</button>
          ))}
        </div>
      </div>

      <div className="pl-karte" data-audit="probelauf-dauer">
        <h2>2 · Wie lange?</h2>
        <div className="pl-dauer" role="group" aria-label="Dauer">
          {DAUERN.map((d) => (
            <button key={d} type="button" className="st-chip" aria-current={d === p.dauer} onClick={() => p.setDauer(d)}>{d} Min.</button>
          ))}
        </div>
      </div>

      <div className="pl-karte">
        <h2>3 · Das passiert</h2>
        <ul className="pl-liste">
          <li>Der Fork merkt sich den Zustand und nimmt die letzten 10 Minuten als Vergleich.</li>
          <li>Regelung und Gerät gehen aus — die Wächter bleiben an.</li>
          <li>Wird eine Grenze überschritten, bricht der Lauf ab und stellt sofort zurück.</li>
          <li>Danach: alles wie vorher, 10 Minuten Nachlauf, dann die Auswertung.</li>
        </ul>
        <details>
          <summary>Erweitert · Grenzen (leer = nicht überwacht)</summary>
          <div className="pl-grenzen">
            {feld('feuchteMax', 'Luftfeuchte max. (%)', p.voreinstellung?.feuchteMax)}
            {feld('tempMax', 'Temperatur max. (°C)', p.voreinstellung?.tempMax)}
            {feld('vpdMin', 'VPD min. (kPa)', p.voreinstellung?.vpdMin)}
            {feld('vpdMax', 'VPD max. (kPa)', p.voreinstellung?.vpdMax)}
          </div>
        </details>
      </div>

      <V1Alert message="Das greift real ins Zelt ein. Es läuft nur, solange die Grenzen halten; ein Neustart stellt von selbst zurück." tone="warn" />
      <div>
        <V1Button variant="primary" disabled={p.sendet || !gewaehlt} onClick={p.onStart} audit="probelauf-start">
          {p.sendet ? 'Startet…' : 'Probelauf starten'}
        </V1Button>
      </div>
    </>
  )
}

// ------------------------------------------------------------------ Ansicht

function LaufAnsicht({ lauf, sendet, onAbbrechen, onZurueck, offen }: {
  lauf: ProbelaufLauf; sendet: boolean; onAbbrechen: () => void; onZurueck: () => void; offen: boolean
}) {
  const laeuft = lauf.status === 'Laeuft'
  const jetzt = jetztWerte(lauf)
  const gesamt = (new Date(lauf.geplantesEndeUtc).getTime() - new Date(lauf.startUtc).getTime()) / 1000
  const vergangen = Math.max(0, gesamt - lauf.restSekunden)

  return (
    <>
      {lauf.status === 'Abgebrochen' || (lauf.abbruchGrund && lauf.status !== 'Laeuft')
        ? <V1Alert title={lauf.status === 'Abgebrochen' ? 'Abgebrochen' : 'Beendet'} message={lauf.abbruchGrund ?? ''} tone="warn" />
        : null}
      {lauf.status === 'RueckstellungOffen' && (
        <V1Alert title="Zurückstellen nicht bestätigt" tone="critical"
          message={`${lauf.modulTitel} ließ sich noch nicht in den früheren Zustand bringen. Der Fork versucht es weiter und meldet sich aufs Handy. Bitte in Home Assistant prüfen.`} />
      )}

      <div className="pl-karte" data-audit="probelauf-stand">
        <h2>{lauf.modulTitel} · {statusText(lauf.status)}</h2>
        {laeuft && (
          <>
            <div className="pl-zahlen">
              <div className="pl-zahl"><b>{restzeit(lauf.restSekunden)}</b><span>Restzeit</span></div>
              <div className="pl-zahl"><b>{restzeit(vergangen)}</b><span>Laufzeit</span></div>
              <div className="pl-zahl"><b>an</b><span>Wächter aktiv</span></div>
            </div>
            <div className="pl-balken" role="progressbar" aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.round((vergangen / Math.max(gesamt, 1)) * 100)}>
              <i style={{ width: `${Math.min(100, (vergangen / Math.max(gesamt, 1)) * 100)}%` }} />
            </div>
          </>
        )}
        {!laeuft && lauf.status !== 'Fertig' && lauf.status !== 'Abgebrochen' && (
          <p className="co-row-sub">Der Eingriff ist beendet, der Nachlauf wird noch aufgezeichnet.</p>
        )}
        {jetzt && (laeuft || lauf.status === 'Nachlauf') && (
          <table className="pl-tabelle">
            <thead><tr><th>Wert</th><th>Zuletzt</th><th>Grenze</th></tr></thead>
            <tbody>
              {jetzt.feuchte != null && <tr><td>Luftfeuchte</td><td>{wertText('Feuchte', jetzt.feuchte)}</td><td>{lauf.grenzen.feuchteMax != null ? `max. ${formatNumber(lauf.grenzen.feuchteMax, 1)} %` : '–'}</td></tr>}
              {jetzt.temp != null && <tr><td>Temperatur</td><td>{wertText('Temperatur', jetzt.temp)}</td><td>{lauf.grenzen.tempMax != null ? `max. ${formatNumber(lauf.grenzen.tempMax, 1)} °C` : '–'}</td></tr>}
              {jetzt.vpd != null && <tr><td>VPD</td><td>{wertText('VPD', jetzt.vpd)}</td><td>{lauf.grenzen.vpdMin != null || lauf.grenzen.vpdMax != null ? `${formatNumber(lauf.grenzen.vpdMin, 2)} bis ${formatNumber(lauf.grenzen.vpdMax, 2)} kPa` : '–'}</td></tr>}
            </tbody>
          </table>
        )}
        {laeuft && (
          <>
            <p className="co-row-sub">Bricht automatisch ab, sobald eine Grenze überschritten wird oder ein Fühler länger als eine Minute nichts meldet.</p>
            <div><V1Button variant="danger" disabled={sendet} onClick={onAbbrechen} audit="probelauf-abbrechen">Jetzt abbrechen und zurückstellen</V1Button></div>
          </>
        )}
        {!offen && <div><V1Button onClick={onZurueck}>Neuen Probelauf vorbereiten</V1Button></div>}
      </div>

      <ProbelaufKurve lauf={lauf} />

      {lauf.auswertung && (
        <div className="pl-karte" data-audit="probelauf-auswertung">
          <h2>Was sich getan hat</h2>
          <table className="pl-tabelle">
            <thead><tr><th>Wert</th><th>Start</th><th>Spitze</th><th>Ende</th><th>je Minute</th><th>Erholung</th></tr></thead>
            <tbody>
              {lauf.auswertung.kennzahlen.map((k) => (
                <tr key={k.groesse}>
                  <td>{k.groesse === 'Feuchte' ? 'Luftfeuchte' : k.groesse}</td>
                  <td>{wertText(k.groesse, k.start)}</td>
                  <td>{wertText(k.groesse, k.spitze)}</td>
                  <td>{wertText(k.groesse, k.ende)}</td>
                  <td className={k.aenderungProMinute > 0 ? 'is-steigt' : k.aenderungProMinute < 0 ? 'is-faellt' : ''}>{proMinuteText(k)}</td>
                  <td>{erholungText(k)}</td>
                </tr>
              ))}
            </tbody>
          </table>
          {lauf.auswertung.hinweise.map((h) => <p key={h} className="co-row-sub">{h}</p>)}
        </div>
      )}

      {lauf.empfehlung && (
        <div className="pl-karte" data-audit="probelauf-empfehlung">
          <h2>Empfehlung der KI</h2>
          <p>{lauf.empfehlung}</p>
          <p className="co-row-sub">Nichts wird ohne deinen Klick geändert.</p>
        </div>
      )}
    </>
  )
}
