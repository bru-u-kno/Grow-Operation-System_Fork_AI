/**
 * Die Geometrie einer Messwert-Kachel mit Zielband.
 *
 * Der Entwurf zeigt jeden Wert nicht als nackte Zahl, sondern über einer schmalen
 * Skala: ein Band markiert den Zielbereich, ein Strich steht für den aktuellen
 * Wert. Damit sieht man in einem Blick, ob der Wert passt *und* ob er am Rand
 * hängt — „6,02" allein sagt Ersteres nur, wenn man den Zielbereich auswendig
 * kennt.
 *
 * Die Rechnerei steht hier und nicht in der Komponente, weil die Randfälle die
 * interessanten sind: ein Wert weit außerhalb, ein halboffener Zielbereich
 * (`≥ 7,0` beim gelösten Sauerstoff), gar kein Zielbereich.
 */

export type MetricStatus = 'ok' | 'warn' | 'crit' | 'unknown'

export type MetricScale = {
  /** Linker Rand des Zielbands, in Prozent der Skala. */
  bandLeft: number
  /** Breite des Zielbands, in Prozent. */
  bandWidth: number
  /** Position des Markers, in Prozent. */
  marker: number
  /** True, wenn der Wert ausserhalb der gezeichneten Skala liegt und am Rand klebt. */
  clamped: boolean
}

/**
 * Die Skala zeigt nicht den Zielbereich, sondern eine Umgebung davon — sonst
 * stünde ein Wert knapp daneben genauso am Anschlag wie einer, der völlig
 * entgleist ist. Gewählt: der Zielbereich plus die Hälfte seiner Breite nach
 * jeder Seite, mindestens aber so viel, dass der aktuelle Wert noch sichtbar
 * hineinfällt.
 */
export function metricScale(value: number | null, min: number | null, max: number | null): MetricScale | null {
  if (value == null || Number.isNaN(value)) return null
  if (min == null && max == null) return null

  // Halboffener Zielbereich: die fehlende Seite bekommt eine Spanne aus der
  // vorhandenen, damit „>= 7,0" überhaupt zeichenbar ist.
  const span = min != null && max != null ? max - min : Math.max(Math.abs(min ?? max ?? 1) * 0.25, 0.5)
  const low = min ?? (max as number) - span
  const high = max ?? (min as number) + span
  const width = Math.max(high - low, Number.EPSILON)

  const padding = width / 2
  let scaleLow = low - padding
  let scaleHigh = high + padding

  // Liegt der Wert ausserhalb, wird die Skala so weit gedehnt, dass er noch
  // hineinpasst — aber der Marker klebt dann sichtbar am Rand.
  const clamped = value < scaleLow || value > scaleHigh
  if (value < scaleLow) scaleLow = value - padding / 2
  if (value > scaleHigh) scaleHigh = value + padding / 2

  const total = scaleHigh - scaleLow
  const percent = (x: number) => ((x - scaleLow) / total) * 100

  return {
    bandLeft: clampPercent(percent(low)),
    bandWidth: Math.max(2, clampPercent(percent(high)) - clampPercent(percent(low))),
    marker: clampPercent(percent(value)),
    clamped,
  }
}

function clampPercent(value: number): number {
  return Math.max(0, Math.min(100, Math.round(value * 10) / 10))
}

/**
 * Ob der Wert im Ziel liegt.
 *
 * `criticalOutside` erlaubt eine zweite, weitere Grenze: innerhalb davon ist der
 * Wert auffällig, ausserhalb kritisch. Ohne Angabe gilt alles ausserhalb des
 * Ziels als auffällig — nicht als kritisch, denn „pH 6,3 statt 6,2" ist kein
 * Notfall.
 */
export function metricStatus(
  value: number | null,
  min: number | null,
  max: number | null,
  criticalOutside?: { min: number | null; max: number | null },
): MetricStatus {
  if (value == null || Number.isNaN(value)) return 'unknown'
  if (min == null && max == null) return 'unknown'

  const inside = (lo: number | null, hi: number | null) =>
    (lo == null || value >= lo) && (hi == null || value <= hi)

  if (inside(min, max)) return 'ok'
  if (criticalOutside && !inside(criticalOutside.min, criticalOutside.max)) return 'crit'
  return criticalOutside ? 'warn' : 'warn'
}

export function statusLabel(status: MetricStatus): string {
  switch (status) {
    case 'ok': return 'im Ziel'
    case 'warn': return 'daneben'
    case 'crit': return 'kritisch'
    default: return '—'
  }
}

/**
 * Der Zielbereich als Text unter der Skala: „Ziel 5,8–6,2", „Ziel ≥ 7,0".
 *
 * `decimals` muss von aussen kommen. In JavaScript sind 7.0 und 7 dieselbe Zahl —
 * ob „≥ 7" oder „≥ 7,0" richtig ist, hängt am Messwert: gelösten Sauerstoff
 * schreibt man mit einer Nachkommastelle, Luftfeuchte ohne. Ohne Angabe wird so
 * geschrieben, wie die Zahl es hergibt.
 */
/**
 * Ein Zielband als nackte Spanne: „22,0–28,0", „≥ 7,0", „≤ 60".
 *
 * Fuer die Tag/Nacht-Leiste, die zwei Baender nebeneinander zeigt. Dort waere
 * „Ziel" zweimal dieselbe Auskunft und die Einheit dritte Wiederholung — sie
 * steht schon gross am Wert.
 */
export function bandText(min: number | null, max: number | null, decimals?: number): string | null {
  /* Nachkommastellen so viele wie noetig, nicht so viele wie erlaubt.
   *
   * „Ziel 22,0–28,0" liest sich genauer, als die Zahl ist — eingetragen wurde
   * 22 und 28. Steht dort spaeter 22,5, gehoert die Stelle hin. Gerundet wird
   * eine Stelle grosszuegiger als die Konvention der Messgroesse, damit
   * Rechenrauschen (22.700000000000003) wegfaellt, eine tatsaechlich
   * eingetippte Stelle aber nicht.
   *
   * Nur fuer die Tag/Nacht-Leiste: die Zeile „Ziel …" behaelt ihre feste
   * Schreibweise, damit Kachel und Grenzwertseite dieselbe Zahl zeigen. */
  const format = (x: number) => {
    const gerundet = x.toFixed((decimals ?? 1) + 1)
    const knapp = gerundet.includes('.') ? gerundet.replace(/0+$/, '').replace(/\.$/, '') : gerundet
    return knapp.replace('.', ',')
  }
  if (min != null && max != null && min === max) return format(min)
  if (min != null && max != null) return `${format(min)}–${format(max)}`
  if (min != null) return `≥ ${format(min)}`
  if (max != null) return `≤ ${format(max)}`
  return null
}

export function targetLabel(
  min: number | null,
  max: number | null,
  unit?: string | null,
  decimals?: number,
): string | null {
  const suffix = unit ? ` ${unit}` : ''
  const format = (x: number) => (decimals == null ? String(x) : x.toFixed(decimals)).replace('.', ',')
  // Fallen Ober- und Untergrenze zusammen, ist es keine Spanne. Die Wassertemperatur
  // trifft das: im Wissen stehen Tag- und Nachtwert, und wo sie gleich sind, stand
  // hier „Ziel 20,0–20,0".
  if (min != null && max != null && min === max) return `Ziel ${format(min)}${suffix}`
  if (min != null && max != null) return `Ziel ${format(min)}–${format(max)}${suffix}`
  if (min != null) return `Ziel ≥ ${format(min)}${suffix}`
  if (max != null) return `Ziel ≤ ${format(max)}${suffix}`
  return null
}

/**
 * Nachkommastellen je Messwert.
 *
 * Konvention des Messwerts, nicht der Zahl: pH schreibt man zweistellig,
 * Luftfeuchte gar nicht. Stand zweimal im Code — auf der Live-Seite mit VPD auf
 * zwei Stellen, auf der Zelt-Detailseite ohne, sodass derselbe Wert dort „0,92"
 * und hier „1" hiess.
 */
export function decimalsForMetric(key: string): number {
  switch (key) {
    case 'reservoir-ph':
    case 'reservoir-ec':
    case 'vpd':
      return 2
    case 'temperature':
    case 'reservoir-temp':
    case 'dissolved-oxygen':
    case 'reservoir-level-cm':
      // Das Zehntel wegen des eTape: in dieser Größenordnung bewegt sich der
      // Wasserstand überhaupt. Auf ganze Zentimeter gerundet stünde die Kachel
      // den halben Tag still.
      return 1
    default:
      return 0
  }
}
