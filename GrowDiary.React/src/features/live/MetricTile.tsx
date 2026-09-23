import { bandGeometrie, bandText, kachelUrteil, kurzeZahl, targetLabel, urteilText, type MetricStatus } from './metric-tile-model'
import { Sparkline, type HistoryPoint } from '../../components/SensorChart'
import { useEffect, useState } from 'react'
import { naechsterZeitpunkt, restdauer } from './licht-restzeit'
import { classNames } from '../../utils'

export type MetricTileProps = {
  label: string
  value: number | null
  unit?: string | null
  targetMin?: number | null
  targetMax?: number | null
  /** Weitere Grenze: innerhalb auffällig, ausserhalb kritisch. */
  critical?: { min: number | null; max: number | null }
  /** Nachkommastellen für Wert und Zielbereich — gehört zum Messwert, nicht zur Anzeige. */
  decimals?: number
  /** Ersetzt die Zielzeile, wo es keinen Bereich gibt (Licht: „Aus in 4 h 20 min"). */
  footer?: string
  /** Ersetzt den Zahlenwert, wo der Messwert keiner ist (Licht: „18/6"). */
  display?: string
  /** Zeitpunkt des Werts, falls er nicht mehr frisch ist. */
  stale?: string
  /** Klick öffnet die Historie — nur gesetzt, wenn es eine gibt. */
  onOpen?: () => void
  /** Ob die Historie dieser Kachel gerade offen ist. */
  open?: boolean
  /** Herkunft des Werts, wenn er NICHT live ist — „Hand · vor 2 Std“. Neutral, keine Warnung. */
  sourceNote?: string
  /** Die letzten 24 Stunden. Vorhanden = Kurve statt Zielband. */
  trend?: HistoryPoint[]
  /** Woran ein zurueckgerechnetes Ziel haengt — „bei 46 % RLF". */
  targetNote?: string | null
  /** Tag- und Nachtband, wo die Messgroesse eins hat. Zusammen mit `targetPhase`. */
  dayMin?: number | null
  dayMax?: number | null
  nightMin?: number | null
  nightMax?: number | null
  /** Welches der beiden Baender gerade gilt: 'day' oder 'night'. */
  targetPhase?: string | null
  /** Kurzer Status in der Ecke, wo es keine Bewertung gibt — „12/12" beim Licht. */
  statusText?: string | null
  /** Schaltzeiten des Lichts; daraus rechnet die Kachel die Restzeit bis zum Wechsel. */
  lightOnAt?: string | null
  lightOffAt?: string | null
  /** Ob das Licht gerade an ist — entscheidet, welche der beiden Zeiten die naechste ist. */
  lightIsOn?: boolean
  /**
   * Fork AI (F-041): Grenzwerte (Meldegrenzen), getrennt vom Ziel — gelbe
   * Striche auf dem Band, Status „Grenze", wenn überschritten.
   */
  alarmMin?: number | null
  alarmMax?: number | null
}

/**
 * Eine Uhr, die nur tickt, wenn jemand sie liest.
 *
 * Ohne `aktiv` laeuft kein Intervall: die Kachel steht auf jedem Bildschirm
 * mehrfach, und ein Zeitgeber je Kachel waere Arbeit fuer nichts.
 */
function useMinutentakt(aktiv: boolean): Date {
  const [jetzt, setJetzt] = useState(() => new Date())
  useEffect(() => {
    if (!aktiv) return
    const stellen = () => setJetzt(new Date())
    const zeiger = window.setInterval(stellen, 30_000)
    /* Zurueck auf dem Bildschirm heisst: sofort nachstellen.
     *
     * Ein Telefon im Standby laesst Zeitgeber ruhen oder bremst sie stark aus.
     * Wer die Seite offen liegen laesst und nach einer halben Stunde wieder
     * hinschaut, saehe sonst die Restzeit von vorhin — und die ist dann
     * schlicht falsch. Beides zusammen: der Takt fuers Zuschauen, das
     * Wiedersehen fuers Weglegen. */
    const beiRueckkehr = () => { if (!document.hidden) stellen() }
    document.addEventListener('visibilitychange', beiRueckkehr)
    window.addEventListener('focus', stellen)
    return () => {
      window.clearInterval(zeiger)
      document.removeEventListener('visibilitychange', beiRueckkehr)
      window.removeEventListener('focus', stellen)
    }
  }, [aktiv])
  return jetzt
}

/**
 * Ein Messwert mit seinem Zielbereich.
 *
 * Die Skala darunter ist der eigentliche Punkt: „6,02" sagt nur dann etwas, wenn
 * man den Zielbereich auswendig kennt. Mit Band und Marker sieht man auch, ob der
 * Wert mittig sitzt oder am Rand hängt — und das ist der Unterschied zwischen
 * „passt" und „kippt gleich".
 *
 * Die Kacheln tragen ihre Trennlinien selbst (`border-left`/`border-top` plus
 * negativer Rand), statt dass der Container eine Rasterfarbe durchscheinen lässt.
 * Sonst färbt sich eine leere Rasterzelle wie ein leeres Panel ein, sobald die
 * Kachelzahl nicht zur Spaltenzahl passt.
 */
export function MetricTile({
  label, value, unit, targetMin = null, targetMax = null, critical, decimals, footer, display, stale, trend, targetNote, sourceNote, onOpen, open,
  dayMin = null, dayMax = null, nightMin = null, nightMax = null, targetPhase = null,
  statusText = null, lightOnAt = null, lightOffAt = null, lightIsOn = false,
  alarmMin = null, alarmMax = null,
}: MetricTileProps) {
  const jetzt = useMinutentakt(Boolean(lightOnAt || lightOffAt))
  const restzeit = restdauer(jetzt, lightIsOn, lightOnAt, lightOffAt)

  /* Fork AI (F-041): Ziel (Plan) und Grenze (Meldung) getrennt.
   *
   * Vorher stand bei Luft „Tag 21–27" — das waren die Meldegrenzen, und 26,8 °C
   * galt als „im Ziel". Jetzt: das Ziel steht unter „ZIEL", die Grenzen sind
   * gelbe Striche auf dem Band, und rot wird es nur, wo auch gemeldet wird.
   * `critical` (alte zweite Grenze) zählt weiter als Grenze, wo niemand
   * Alarmgrenzen übergibt. */
  const ziel = { min: targetMin, max: targetMax }
  const grenze = { min: alarmMin ?? critical?.min ?? null, max: alarmMax ?? critical?.max ?? null }
  const stellen = decimals ?? 1
  const urteil = display != null && targetMin == null && targetMax == null && grenze.min == null && grenze.max == null
    ? 'unknown'
    : kachelUrteil(value, ziel, grenze, stellen)
  const status: MetricStatus = urteil
  const statusTextJetzt = urteil === 'unknown' ? null : urteilText(urteil, value, ziel, unit, stellen)
  const band = bandGeometrie(value, ziel, grenze, (x) => kurzeZahl(x, stellen))

  /* Tag und Nacht in EINER Zeile unter „ZIEL" (Mockup v3): das gerade gültige
   * hell, das andere gedimmt. Einzelwert als eine Zahl, „höchstens" als „≤". */
  const mitEinheit = (text: string | null) => (text == null ? null : unit ? `${text} ${unit}` : text)
  const tagText = mitEinheit(bandText(dayMin, dayMax, decimals))
  const nachtText = mitEinheit(bandText(nightMin, nightMax, decimals))
  const baender = footer == null && tagText != null && nachtText != null && targetPhase != null
    ? { tag: tagText, nacht: nachtText, nachtAktiv: targetPhase === 'night' }
    : null
  const zielText = targetMin == null && targetMax == null ? null : bandText(targetMin, targetMax, decimals)
  // Nur für Kacheln ohne Ziel/Grenze, die trotzdem eine Fußzeile haben (z. B. „Kein Entity gemappt").
  const fusszeile = footer ?? (zielText == null ? targetLabel(targetMin, targetMax, unit, decimals) : null)

  /* Licht: beide Schaltzeiten untereinander, die nächste hell. */
  const lichtZeiten = lightOnAt || lightOffAt
    ? (() => {
        const an = lightOnAt ? naechsterZeitpunkt(jetzt, lightOnAt) : null
        const aus = lightOffAt ? naechsterZeitpunkt(jetzt, lightOffAt) : null
        const naechsteIstAn = an != null && (aus == null || an.getTime() < aus.getTime())
        return { naechsteIstAn }
      })()
    : null

  const shown = display ?? (value == null || Number.isNaN(value)
    ? '—'
    : (decimals == null ? String(value) : value.toFixed(decimals)).replace('.', ','))

  // Klickbar nur, wenn es etwas zu oeffnen gibt: ein button, der nichts tut,
  // ist schlimmer als keiner. Semantisch bleibt es eine Kachel — role/tabIndex
  // statt <button>, weil in der Kachel keine verschachtelten Buttons erlaubt
  // waeren und der Anpassen-Modus eigene Knoepfe hineinlegt.
  return (
    <div
      className={classNames('gos-metric', `is-${status}`, onOpen && 'is-clickable', open && 'is-open')}
      data-audit={`metric-${label.toLowerCase()}`}
      role={onOpen ? 'button' : undefined}
      tabIndex={onOpen ? 0 : undefined}
      aria-expanded={onOpen ? open === true : undefined}
      aria-label={onOpen ? `${label}: Verlauf ${open ? 'schließen' : 'anzeigen'}` : undefined}
      onClick={onOpen}
      onKeyDown={onOpen ? (event) => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); onOpen() } } : undefined}
    >
      <div className="gos-metric-head">
        <span className="gos-metric-label">{label}</span>
        {statusTextJetzt
          ? <span className="gos-metric-status">{statusTextJetzt}</span>
          : statusText && <span className="gos-metric-status is-note">{statusText}</span>}
      </div>

      <div className="gos-metric-value">
        {shown}
        {unit && display == null && <span className="unit">{unit}</span>}
      </div>

      {/* Die 24-h-Kurve bleibt — sie ist der Einstieg in den Verlauf. */}
      {trend && trend.length > 1 && (
        <div className="gos-metric-spark" aria-hidden="true">
          <Sparkline points={trend} height={22} />
        </div>
      )}

      {(baender || zielText) && (
        <div className="gos-metric-ziel">
          <div className="gos-metric-ziel-n">ZIEL</div>
          {baender ? (
            <div className="gos-metric-tn">
              <span className={classNames(!baender.nachtAktiv && 'is-aktiv')}>
                <i aria-hidden="true">{'\u2600\uFE0E'}</i>{baender.tag}
              </span>
              <span className={classNames(baender.nachtAktiv && 'is-aktiv')}>
                <i aria-hidden="true">{'\u263E\uFE0E'}</i>{baender.nacht}
              </span>
            </div>
          ) : (
            <div className="gos-metric-zv">
              {zielText}{unit && <span className="u"> {unit}</span>}
            </div>
          )}
        </div>
      )}

      {band && (
        <div className="gos-metric-band" aria-hidden="true">
          <div className="balken">
            <div className="spur" />
            {band.zone && <div className="zone" style={{ left: `${band.zone.links}%`, width: `${band.zone.breite}%` }} />}
            {band.zielMarke != null && <div className="zielmarke" style={{ left: `${band.zielMarke}%` }} />}
            {band.grenzen.map((g, i) => <div key={i} className="grenze" style={{ left: `${g}%` }} />)}
            <div className={classNames('nadel', `ist-${urteil}`)} style={{ left: `${band.nadel}%` }} />
          </div>
          <div className="skala">
            {band.skala.map((z, i) => (
              <span key={i} className={z.art === 'grenze' ? 'ist-grenze' : 'ist-ziel'} style={{ left: `${z.pos}%` }}>{z.text}</span>
            ))}
          </div>
        </div>
      )}

      {targetNote && !baender && <div className="gos-metric-target">{targetNote}</div>}
      {!band && !baender && !zielText && fusszeile && !lichtZeiten && <div className="gos-metric-target">{fusszeile}</div>}

      {lichtZeiten && (
        <div className="gos-metric-licht">
          {lightOnAt && <><span className="k">an</span><span className={classNames(lichtZeiten.naechsteIstAn && 'is-aktiv')}>{lightOnAt} Uhr</span></>}
          {lightOffAt && <><span className="k">aus</span><span className={classNames(!lichtZeiten.naechsteIstAn && 'is-aktiv')}>{lightOffAt} Uhr</span></>}
        </div>
      )}
      {restzeit && (
        restzeit === 'gleich'
          ? <div className="gos-metric-source">Wechsel gleich</div>
          : (
            <div className="gos-metric-wechsel">
              <div className="gos-metric-ziel-n">WECHSELT IN</div>
              <div className="dauer">{restzeit}</div>
            </div>
          )
      )}
      {stale
        ? <div className="gos-metric-stale">{stale}</div>
        : sourceNote && <div className="gos-metric-source">{sourceNote}</div>}
    </div>
  )
}
