import type { MetricPayload } from '../../types'
import { zahlOderNull } from '../../zahlenfeld'

/**
 * Prüft die eingetippten Werte gegen die Zielbereiche — während man tippt.
 *
 * Bisher erfuhr man erst nach dem Speichern, dass der gelöste Sauerstoff zu
 * niedrig ist: Messung sichern, Diagnose öffnen, lesen. Der Entwurf stellt die
 * Prüfung neben das Formular, und das ist der richtige Moment — wer gerade am
 * Reservoir steht, kann sofort handeln, statt später zurückzulaufen.
 *
 * Die Zielbereiche kommen aus derselben Quelle wie die Kacheln der Live-Ansicht,
 * also aus der Wissensbasis für die aktuelle Phase. Damit sagt das Formular
 * dasselbe wie das Dashboard — zwei Wahrheiten wären schlimmer als keine.
 */

export type CheckSeverity = 'ok' | 'warn' | 'crit'

export type CheckFinding = {
  /** Feld im Entwurf, z. B. `dissolvedOxygenMgL`. */
  field: string
  label: string
  severity: CheckSeverity
  /** Abstand zum nächsten Rand des Ziels, vorzeichenbehaftet; null wenn im Ziel. */
  delta: number | null
  text: string
  /** SOP-Vorschlag, wo das Wissen einen kennt. */
  hint?: string
}

/** Zuordnung Entwurfsfeld → Live-Metrik, damit der Zielbereich gefunden wird. */
const FIELD_TO_METRIC: Record<string, string> = {
  airTemperatureC: 'temperature',
  humidityPercent: 'humidity',
  reservoirPh: 'reservoir-ph',
  reservoirEc: 'reservoir-ec',
  reservoirWaterTempC: 'reservoir-temp',
  dissolvedOxygenMgL: 'dissolved-oxygen',
  orpMv: 'orp',
  co2Ppm: 'co2',
}

const LABELS: Record<string, string> = {
  airTemperatureC: 'Lufttemperatur',
  humidityPercent: 'Luftfeuchte',
  reservoirPh: 'pH',
  reservoirEc: 'EC',
  reservoirWaterTempC: 'Wassertemperatur',
  dissolvedOxygenMgL: 'Gelöster Sauerstoff',
  orpMv: 'ORP',
  co2Ppm: 'CO₂',
}

const UNITS: Record<string, string> = {
  airTemperatureC: '°C',
  humidityPercent: '%',
  reservoirEc: 'mS/cm',
  reservoirWaterTempC: '°C',
  dissolvedOxygenMgL: 'mg/l',
  orpMv: 'mV',
  co2Ppm: 'ppm',
}

/**
 * Wo ein Befund eine Prozedur nach sich zieht, steht sie dabei. Bewusst knapp und
 * nur dort, wo die Wissensbasis eine eindeutige Antwort hat — ein Vorschlag zu
 * jedem Wert wäre Rauschen.
 */
const HINTS: Record<string, string> = {
  dissolvedOxygenMgL: 'Wurzelfäule-Risiko — SOP-S1 prüfen',
  reservoirWaterTempC: 'Warmes Wasser hält weniger Sauerstoff',
}


function format(value: number): string {
  return String(Math.round(value * 100) / 100).replace('.', ',')
}

/**
 * Was für eine Messgröße physikalisch überhaupt vorkommen kann.
 *
 * <b>Warum das hier nochmal steht.</b> Die Wahrheit liegt im Backend
 * (`MeasurementSanityService.PhysikalischeGrenzen`) und sperrt beim Speichern.
 * Beim TIPPEN half das nicht: 9000 °C meldete die Prüfung als „+8971 über
 * 29 °C" — also wie eine etwas zu warme Kammer. Erst beim Speichern kam eine
 * Fehlermeldung vom Server.
 *
 * Ein unmöglicher Wert ist keine Abweichung, sondern ein Tippfehler oder eine
 * falsche Einheit. Er gehört anders benannt, und zwar sofort.
 *
 * Die Zahlen sind bewusst dieselben wie im Backend; der Test
 * `live-check-model.test.ts` hält sie dagegen, damit sie nicht auseinanderlaufen.
 */
const PHYSIK: Record<string, [number, number]> = {
  airTemperatureC: [-20, 60],
  humidityPercent: [0, 100],
  reservoirPh: [0, 14],
  reservoirEc: [0, 10],
  reservoirWaterTempC: [-5, 60],
  dissolvedOxygenMgL: [0, 20],
  orpMv: [-1000, 1000],
  co2Ppm: [0, 30000],
}

/**
 * @param draft die eingetippten Werte als Zeichenketten, wie sie im Formular stehen
 * @param metrics die Live-Metriken mit ihren Zielbereichen
 */
export function checkDraft(draft: Record<string, string>, metrics: MetricPayload[]): CheckFinding[] {
  const byKey = new Map(metrics.map((metric) => [metric.key, metric]))
  const findings: CheckFinding[] = []

  for (const [field, metricKey] of Object.entries(FIELD_TO_METRIC)) {
    const raw = draft[field]
    if (raw == null) continue
    const value = zahlOderNull(raw)
    if (value == null) continue

    const label = LABELS[field] ?? field
    const unit = UNITS[field] ? ` ${UNITS[field]}` : ''
    const shown = `${label} ${format(value)}${unit}`

    // Physikalisch Unmögliches ZUERST — und unabhängig davon, ob es für diese
    // Größe ein Ziel gibt. CO₂ hat ohne Anreicherung absichtlich keins; ohne
    // diese Reihenfolge wären −500 ppm stillgeblieben.
    const grenze = PHYSIK[field]
    if (grenze && (value < grenze[0] || value > grenze[1])) {
      findings.push({
        field,
        label,
        severity: 'crit',
        delta: null,
        text: `${shown} — das kann es nicht geben`,
        hint: `Möglich sind ${format(grenze[0])} bis ${format(grenze[1])}${unit}. Bitte Messgerät oder Einheit prüfen.`,
      })
      continue
    }

    const metric = byKey.get(metricKey)
    const min = metric?.targetMin ?? null
    const max = metric?.targetMax ?? null
    if (min == null && max == null) continue

    if ((min == null || value >= min) && (max == null || value <= max)) {
      findings.push({ field, label, severity: 'ok', delta: null, text: shown })
      continue
    }

    const below = min != null && value < min
    const delta = below ? value - min : value - (max as number)
    const bound = below ? (min as number) : (max as number)
    // Mehr als ein Viertel der Zielbreite daneben ist nicht mehr „knapp".
    const width = min != null && max != null ? Math.abs(max - min) : Math.abs(bound) * 0.2
    const severity: CheckSeverity = Math.abs(delta) > Math.max(width, Number.EPSILON) ? 'crit' : 'warn'

    findings.push({
      field,
      label,
      severity,
      delta: Math.round(delta * 100) / 100,
      text: `${shown} ${below ? 'unter' : 'über'} ${format(bound)}${unit}`,
      hint: HINTS[field],
    })
  }

  // Was Aufmerksamkeit braucht, steht oben; die in Ordnung befundenen Werte
  // fasst die Anzeige danach zu einer Zeile zusammen.
  const rank = { crit: 0, warn: 1, ok: 2 } as const
  return findings.sort((a, b) => rank[a.severity] - rank[b.severity] || a.label.localeCompare(b.label))
}

/** „pH, EC und Wassertemperatur im Zielband" — eine Zeile statt drei Haken. */
export function summariseOk(findings: CheckFinding[]): string | null {
  const ok = findings.filter((finding) => finding.severity === 'ok').map((finding) => finding.label)
  if (ok.length === 0) return null
  if (ok.length === 1) return `${ok[0]} im Zielband`
  return `${ok.slice(0, -1).join(', ')} und ${ok[ok.length - 1]} im Zielband`
}
