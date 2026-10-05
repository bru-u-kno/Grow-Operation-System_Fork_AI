import type { Marke } from './tagebuch-modell'

const B = 240
const H = 64
const OBEN = 6
const UNTEN = 4

/** Größere Lücke als das heißt: der Sensor war weg — die Linie wird dort unterbrochen, nicht überbrückt. */
const LUECKE_MINUTEN = 30

/**
 * Eine Tageskurve 0–24 Uhr mit Lichtphase und Ereignis-Strichen.
 *
 * Warum nicht `SensorChart` oder `Verlaufsdiagramm`: beide sind ganze
 * Diagramme mit Achsen, Auswahl und Gesten (600 × 170 bzw. die Live-Kachel).
 * Hier stehen sechs davon in einer Handy-Zeile nebeneinander; gebraucht wird
 * eine Linie, kein Werkzeug. Die Zahlen daneben (Min–Max) formatiert die
 * aufrufende Kachel.
 *
 * Die Achse steht in HTML unter dem SVG: das SVG wird verzerrt skaliert
 * (`preserveAspectRatio="none"`), Text darin würde mitgezerrt.
 */
export function MiniKurve({ punkte, licht, marken, beschriftung }: {
  punkte: Array<{ minute: number; wert: number }>
  licht: Array<{ von: number; bis: number }>
  marken: Marke[]
  /** Für Bildschirmleser: „EC von 1,17 bis 1,64 mS/cm". */
  beschriftung: string
}) {
  const werte = punkte.map((p) => p.wert)
  let lo = Math.min(...werte)
  let hi = Math.max(...werte)
  if (!(hi - lo > 1e-9)) { lo -= 0.5; hi += 0.5 }
  const x = (minute: number) => (minute / 1440) * B
  const y = (wert: number) => OBEN + (1 - (wert - lo) / (hi - lo)) * (H - OBEN - UNTEN)

  const sortiert = [...punkte].sort((a, b) => a.minute - b.minute)
  const pfad = sortiert
    .map((p, i) => {
      const neu = i === 0 || p.minute - sortiert[i - 1].minute > LUECKE_MINUTEN
      return `${neu ? 'M' : 'L'}${x(p.minute).toFixed(1)},${y(p.wert).toFixed(1)}`
    })
    .join(' ')

  return (
    <div className="tb-kurve-rahmen">
      <svg className="tb-kurve" viewBox={`0 0 ${B} ${H}`} preserveAspectRatio="none" role="img" aria-label={beschriftung}>
        {licht.map((s) => (
          <rect key={`${s.von}-${s.bis}`} x={x(s.von)} y={0} width={x(s.bis) - x(s.von)} height={H} className="tb-kurve-licht" />
        ))}
        {marken.map((m, i) => (
          <line key={`${m.minute}-${m.farbe}-${i}`} x1={x(m.minute)} x2={x(m.minute)} y1={1} y2={H - 1} className={`tb-kurve-marke is-${m.farbe}`} />
        ))}
        {sortiert.length === 1
          ? <circle cx={x(sortiert[0].minute)} cy={y(sortiert[0].wert)} r={1.6} className="tb-kurve-punkt" />
          : <path d={pfad} className="tb-kurve-linie" />}
      </svg>
      <div className="tb-kurve-achse" aria-hidden="true"><span>0</span><span>6</span><span>12</span><span>18</span><span>24 Uhr</span></div>
    </div>
  )
}
