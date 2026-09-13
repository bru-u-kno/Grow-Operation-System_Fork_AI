import { bandText, metricScale, metricStatus, statusLabel, targetLabel, type MetricStatus } from './metric-tile-model'
import { Sparkline, type HistoryPoint } from '../../components/SensorChart'
import { useEffect, useState } from 'react'
import { restzeitText } from './licht-restzeit'
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
    const zeiger = window.setInterval(() => setJetzt(new Date()), 30_000)
    return () => window.clearInterval(zeiger)
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
}: MetricTileProps) {
  const jetzt = useMinutentakt(Boolean(lightOnAt || lightOffAt))
  const restzeit = restzeitText(jetzt, lightIsOn, lightOnAt, lightOffAt)
  const status: MetricStatus = display != null && targetMin == null && targetMax == null
    ? 'unknown'
    : metricStatus(value, targetMin, targetMax, critical)
  const scale = metricScale(value, targetMin, targetMax)
  const target = footer ?? targetLabel(targetMin, targetMax, unit, decimals)

  /* Tag und Nacht nebeneinander statt der Zielzeile.
   *
   * Nur wo beide Baender bekannt sind und ein Zielband ueberhaupt gilt: die
   * Leiste ersetzt die Zeile „Ziel …", sie kommt nicht dazu. `footer` gewinnt,
   * denn wo eine eigene Fusszeile steht (Licht: „12/12 · an 05:04"), gibt es
   * kein Band zu zeigen.
   *
   * Warum beide Spalten auch dann, wenn dieselbe Spanne zweimal dasteht: das
   * ist die Aussage. Bei der Luftfeuchte heisst es „hier wird nachts nicht
   * gelockert" — und genau das ist die Frage, die man sich nachts vor der
   * Kachel stellt. */
  const tagText = bandText(dayMin, dayMax, decimals)
  const nachtText = bandText(nightMin, nightMax, decimals)
  const baender = footer == null && tagText != null && nachtText != null && targetPhase != null
    ? { tag: tagText, nacht: nachtText, nachtAktiv: targetPhase === 'night' }
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
        {status !== 'unknown'
          ? <span className="gos-metric-status">{statusLabel(status)}</span>
          : statusText && <span className="gos-metric-status is-note">{statusText}</span>}
      </div>

      <div className="gos-metric-value">
        {shown}
        {unit && display == null && <span className="unit">{unit}</span>}
      </div>

      {trend && trend.length > 1 ? (
        // Die Kurve ERSETZT das Zielband, sie kommt nicht dazu: sonst wächst jede
        // Kachel und die Seite mit ihr. Der Zielbereich steht als Text darunter
        // weiter da, und die Farbe kommt vom Status der Kachel.
        <div className="gos-metric-spark" aria-hidden="true">
          <Sparkline points={trend} height={22} />
        </div>
      ) : scale ? (
        <div className="gos-metric-scale" aria-hidden="true">
          <span className="band" style={{ left: `${scale.bandLeft}%`, width: `${scale.bandWidth}%` }} />
          <span className={classNames('mark', scale.clamped && 'clamped')} style={{ left: `${scale.marker}%` }} />
        </div>
      ) : (
        // Ohne Skala bleibt die Höhe trotzdem stehen, sonst stehen Kacheln mit
        // und ohne Zielbereich unterschiedlich hoch nebeneinander.
        <div className="gos-metric-scale is-empty" aria-hidden="true" />
      )}

      {/* Der Zusatz nennt, woran ein zurueckgerechnetes Ziel haengt. „Ziel
          15,8–19,6 °C" allein liest sich als „kuehl runter", obwohl in
          Wahrheit die Feuchte zu niedrig ist. */}
      {baender ? (
        <>
          <div className="gos-metric-bands">
            <div className={classNames('spalte', !baender.nachtAktiv && 'is-aktiv')}>
              <i><span className="zeichen tag" aria-hidden="true">☀</span>Tag</i>{baender.tag}
            </div>
            <div className={classNames('spalte', baender.nachtAktiv && 'is-aktiv')}>
              <i><span className="zeichen nacht" aria-hidden="true">☾</span>Nacht</i>{baender.nacht}
            </div>
          </div>
          {targetNote && <div className="gos-metric-target">{targetNote}</div>}
        </>
      ) : target && <div className="gos-metric-target">{target}{targetNote ? ` · ${targetNote}` : ''}</div>}
      {/* Herkunft neutral, Veraltet warnend — beides zusammen waere doppelt,
          also gewinnt die Warnung. */}
      {/* Wann es umschlaegt — die Frage, die man vor der Licht-Kachel hat.
          Gerechnet in der Oberflaeche, damit die Angabe nicht zwischen zwei
          Abrufen altert. */}
      {restzeit && <div className="gos-metric-source">{restzeit}</div>}
      {stale
        ? <div className="gos-metric-stale">{stale}</div>
        : sourceNote && <div className="gos-metric-source">{sourceNote}</div>}
    </div>
  )
}
