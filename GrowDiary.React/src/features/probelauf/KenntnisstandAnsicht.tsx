import { useEffect, useState } from 'react'
import { apiFetch, formatApiError } from '../../api'
import { V1Alert, V1Button, V1Skeleton } from '../../components/v1'
import type { Kenntnisstand, ZielPhase } from '../../types'
import { anteilText, erholungText, lichtText, urteilKlasse, urteilText, wirkungText } from './probelauf-anzeige'

/**
 * Fork AI (A-010, Etappe 2): Der Kenntnisstand — Kann das System die Zielwerte des Plans halten?
 *
 * Alles vom Fork berechnet, ohne KI: Zielabgleich aus dem Verlauf der letzten sieben Tage, gelernte Wirkung aus den
 * Probeläufen, Abdeckung und der nächste sinnvolle Lauf. Hinweise nennen, wo sich ansetzen lässt — geändert wird nichts.
 */
export function KenntnisstandAnsicht({ onVorbereiten }: { onVorbereiten: (modul: string) => void }) {
  const [daten, setDaten] = useState<Kenntnisstand | null>(null)
  const [fehler, setFehler] = useState<string | null>(null)

  useEffect(() => {
    let abgebrochen = false
    apiFetch<Kenntnisstand>('/api/steuerung/probelauf/kenntnisstand')
      .then((k) => { if (!abgebrochen) setDaten(k) })
      .catch((caught) => { if (!abgebrochen) setFehler(formatApiError(caught, 'Der Kenntnisstand konnte nicht berechnet werden.')) })
    return () => { abgebrochen = true }
  }, [])

  if (fehler) return <V1Alert title="Kenntnisstand" message={fehler} tone="warn" />
  if (!daten) return <V1Skeleton rows={5} label="Berechne Kenntnisstand" />

  const phase = (p: ZielPhase) => (
    <>
      <span className={`pl-marke ${urteilKlasse(p.urteil)}`}>{urteilText(p.urteil)}</span>
      {p.anteilProzent != null && <> {anteilText(p.anteilProzent)}</>}
      {p.zielText && <small className="co-row-sub"> · Ziel {p.zielText}</small>}
    </>
  )

  return (
    <div className="pl-seite" data-audit="probelauf-kenntnisstand">
      <div className="pl-karte">
        <h2>Kann das System deine Zielwerte halten?</h2>
        <p className="co-row-sub">
          Die Ziele aus deinem Plan gegen den Verlauf der letzten {daten.tageBetrachtet} Tage. „Im Ziel" heißt: Luftfeuchte und Temperatur unter dem
          Höchstwert, das VPD im Band (± 0,1 kPa).
        </p>
        <table className="pl-tabelle">
          <thead><tr><th>Wert</th><th>Bei Licht an</th><th>Bei Licht aus</th></tr></thead>
          <tbody>
            {daten.zielabgleich.map((z) => (
              <tr key={z.groesse}><td>{z.groesse}</td><td>{phase(z.tag)}</td><td>{phase(z.nacht)}</td></tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="pl-karte">
        <h2>Was jedes Gerät bewirkt (aus deinen Probeläufen)</h2>
        {daten.wirkung.length === 0
          ? <p className="co-row-sub">Noch kein Probelauf, aus dem sich etwas lernen lässt — Läufe unter zwei Minuten zählen nicht.</p>
          : (
            <table className="pl-tabelle">
              <thead><tr><th>Gerät</th><th>Wirkung, wenn aus</th><th>Erholung</th><th>Läufe</th></tr></thead>
              <tbody>
                {daten.wirkung.map((w) => (
                  <tr key={`${w.modul}-${w.tag}`}>
                    <td>{w.titel}<small className="co-row-sub"> {lichtText(w.tag)}</small></td>
                    <td>{w.werte.map((v) => <div key={v.groesse}>{v.groesse === 'Feuchte' ? 'Luftfeuchte' : v.groesse}: {wirkungText(v.groesse, v.proMinute)}</div>)}</td>
                    <td>{w.werte.map((v) => <div key={v.groesse}>{erholungText({ groesse: v.groesse, start: 0, spitze: 0, ende: 0, aenderungProMinute: v.proMinute, erholungMinuten: v.erholungMinuten })}</div>)}</td>
                    <td>{w.laeufe}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
      </div>

      <div className="pl-karte">
        <h2>Wo noch Messungen fehlen</h2>
        <table className="pl-tabelle">
          <thead><tr><th>Gerät</th><th>Licht an</th><th>Licht aus</th></tr></thead>
          <tbody>
            {daten.abdeckung.map((a) => (
              <tr key={a.modul}>
                <td>{a.titel}</td>
                <td>{!a.tagMoeglich ? '–' : a.laeufeTag > 0 ? <span className="pl-marke is-ok">{a.laeufeTag}×</span> : <span className="pl-marke">offen</span>}</td>
                <td>{!a.nachtMoeglich ? '–' : a.laeufeNacht > 0 ? <span className="pl-marke is-ok">{a.laeufeNacht}×</span> : <span className="pl-marke">offen</span>}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="pl-karte" data-audit="probelauf-naechster">
        <h2>Nächster sinnvoller Probelauf</h2>
        {daten.naechster
          ? (
            <>
              <p><b>{daten.naechster.titel} · {daten.naechster.dauerMinuten} Min. · {lichtText(daten.naechster.tag)}</b></p>
              <p className="co-row-sub">{daten.naechster.begruendung}</p>
              <div><V1Button onClick={() => onVorbereiten(daten.naechster!.modul)}>Vorbereiten</V1Button></div>
            </>
          )
          : <p className="co-row-sub">Alle Kombinationen sind gemessen. Wiederholungen lohnen bei anderem Wetter oder in einer neuen Phase.</p>}
        <p className="co-row-sub">Der Fork startet nichts von allein — jeden Lauf bestätigst du.</p>
      </div>

      {daten.hinweise.length > 0 && (
        <div className="pl-karte" data-audit="probelauf-hinweise">
          <h2>Wo sich ansetzen lässt</h2>
          <ul className="pl-liste">{daten.hinweise.map((h) => <li key={h}>{h}</li>)}</ul>
          <p className="co-row-sub">Das sind Hinweise aus deinen Messungen, keine Einstellungen — was geändert wird, entscheidest du.</p>
        </div>
      )}
    </div>
  )
}
