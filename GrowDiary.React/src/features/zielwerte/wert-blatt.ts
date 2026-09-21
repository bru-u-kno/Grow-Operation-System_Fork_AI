import { zahlOderNull } from '../../zahlenfeld'
import type { AlertRuleDto } from '../../types/alert'

/**
 * Fork AI (Grow-Plan, Schritt 2): die Rechenseite des Bearbeiten-Blatts einer
 * Werte-Karte — ohne React, damit sie sich prüfen lässt.
 *
 * Ein Blatt ändert zwei Dinge auf einmal: das Ziel der laufenden Woche im Plan
 * des Grows und die Alarmregel des Zelts. Beides sind zwei Zahlen zu EINER
 * Sache, deshalb ein Speichern. Die Server-Wege bleiben die bestehenden
 * (`/api/wochenplan/werte`, `/api/alerts/tents`).
 */

export type PlanFeld = {
  feld: string
  bezeichnung: string
  einheit: string
  min: number
  max: number
  schritt: number
  wert: number | null
  startwert: number | null
  herkunft: string
}

export type AlarmRegel = {
  quelle: string
  min: number | null
  max: number | null
  nachtMin: number | null
  nachtMax: number | null
  toleranz: number | null
  standardToleranz: number
  karenzMinuten: number
  aktiv: boolean
  planMoeglich: boolean
}

export type Entwurf = {
  plan: Record<string, string>
  quelle: 'Fest' | 'Plan'
  min: string
  max: string
  toleranz: string
  karenz: string
  aktiv: boolean
}

/** Welche Übergabe-Zeilen zu welcher Messgröße gehören. */
export const UEBERGABE_JE_METRIK: Record<string, readonly string[]> = {
  temperature: ['luft-unten', 'luft-oben', 'luft-nacht-unten', 'luft-nacht-oben'],
  humidity: ['rh-obergrenze', 'feuchte-oben'],
  co2: ['co2-ziel'],
  'reservoir-temp': ['wasser-tag', 'wasser-nacht'],
  // Fork AI (forkai.129): VPD-Band und Blatt-Offset gehen an den Entfeuchter.
  vpd: ['vpd-unten', 'vpd-oben', 'blatt-offset'],
}

/** Paare, bei denen „von“ nicht über „bis“ liegen darf. */
const PAARE: ReadonlyArray<readonly [string, string]> = [
  ['ecMin', 'ecMax'], ['phMin', 'phMax'], ['vpdMin', 'vpdMax'], ['co2Min', 'co2Max'],
  ['ppfdMin', 'ppfdMax'], ['orpMin', 'orpMax'],
]

/** Zahl deutsch, ohne überflüssige Nachkommastellen. */
export function zahlText(wert: number | null | undefined): string {
  if (wert == null) return ''
  return wert.toLocaleString('de-DE', { maximumFractionDigits: 3 })
}

export function entwurfAus(felder: readonly PlanFeld[], regel: AlarmRegel | null): Entwurf {
  return {
    plan: Object.fromEntries(felder.map((f) => [f.feld, zahlText(f.wert)])),
    quelle: regel?.quelle === 'Plan' ? 'Plan' : 'Fest',
    min: zahlText(regel?.min),
    max: zahlText(regel?.max),
    toleranz: zahlText(regel?.toleranz),
    karenz: String(regel?.karenzMinuten ?? 30),
    aktiv: regel?.aktiv ?? false,
  }
}

function gleich(a: number | null, b: number | null): boolean {
  return a == null ? b == null : b != null && Math.abs(a - b) < 1e-9
}

/**
 * Die Planänderungen fürs Speichern. Ein Wert, der dem Startstand entspricht,
 * geht als `null` raus — der Server stellt dann den Startwert her, statt eine
 * Kopie als „eigen“ abzulegen.
 */
export function planAenderungen(
  felder: readonly PlanFeld[],
  entwurf: Entwurf,
  spalteId: string,
): Array<{ spalteId: string; feld: string; wert: number | null }> {
  return felder.flatMap((f): Array<{ spalteId: string; feld: string; wert: number | null }> => {
    const neu = zahlOderNull(entwurf.plan[f.feld] ?? '')
    if (gleich(neu, f.wert)) return []
    if (neu == null || gleich(neu, f.startwert)) return [{ spalteId, feld: f.feld, wert: null }]
    return [{ spalteId, feld: f.feld, wert: neu }]
  })
}

/** Hat sich an der Alarmregel etwas geändert? */
export function alarmGeaendert(regel: AlarmRegel | null, entwurf: Entwurf): boolean {
  const vorher = entwurfAus([], regel)
  if (regel == null) {
    // Ohne Regel gilt nur eine eingetragene Grenze oder ein Plan-Wechsel als Änderung.
    return entwurf.aktiv || entwurf.quelle === 'Plan'
      || zahlOderNull(entwurf.min) != null || zahlOderNull(entwurf.max) != null
  }
  return vorher.quelle !== entwurf.quelle
    || vorher.aktiv !== entwurf.aktiv
    || !gleich(zahlOderNull(vorher.karenz), zahlOderNull(entwurf.karenz))
    || (entwurf.quelle === 'Fest' && (!gleich(regel.min, zahlOderNull(entwurf.min)) || !gleich(regel.max, zahlOderNull(entwurf.max))))
    || (entwurf.quelle === 'Plan' && !gleich(regel.toleranz, zahlOderNull(entwurf.toleranz)))
}

/** Prüft den Entwurf; gibt den ersten Fehler als Satz zurück oder null. */
export function pruefen(felder: readonly PlanFeld[], entwurf: Entwurf): string | null {
  for (const f of felder) {
    const roh = entwurf.plan[f.feld] ?? ''
    if (roh.trim() === '') continue
    const zahl = zahlOderNull(roh)
    if (zahl == null) return `${f.bezeichnung}: „${roh}“ ist keine Zahl.`
    if (zahl < f.min || zahl > f.max) {
      return `${f.bezeichnung} muss zwischen ${zahlText(f.min)} und ${zahlText(f.max)} liegen.`
    }
  }
  for (const [von, bis] of PAARE) {
    const a = zahlOderNull(entwurf.plan[von] ?? '')
    const b = zahlOderNull(entwurf.plan[bis] ?? '')
    if (a != null && b != null && a > b + 1e-9) {
      const name = felder.find((f) => f.feld === von)?.bezeichnung ?? von
      return `${name} liegt über dem Bis-Wert.`
    }
  }
  if (entwurf.quelle === 'Fest') {
    const min = zahlOderNull(entwurf.min)
    const max = zahlOderNull(entwurf.max)
    if (entwurf.min.trim() !== '' && min == null) return `„${entwurf.min}“ ist keine Zahl.`
    if (entwurf.max.trim() !== '' && max == null) return `„${entwurf.max}“ ist keine Zahl.`
    if (min != null && max != null && min > max) return 'Die untere Alarmgrenze liegt über der oberen.'
    if (entwurf.aktiv && min == null && max == null) return 'Für einen festen Alarm braucht es mindestens eine Grenze.'
  } else if (entwurf.toleranz.trim() !== '' && (zahlOderNull(entwurf.toleranz) ?? -1) <= 0) {
    return 'Die Toleranz muss größer als null sein.'
  }
  const karenz = zahlOderNull(entwurf.karenz)
  if (karenz == null || karenz < 1) return 'Zwischen zwei Meldungen muss mindestens eine Minute liegen.'
  return null
}

/**
 * Der ganze Regelsatz des Zelts mit der geänderten Regel — der Server ersetzt
 * immer den ganzen Satz. Nachtband und Benachrichtigungsdienst bleiben stehen.
 */
export function regelnMitAenderung(
  alle: readonly AlertRuleDto[],
  metricKey: string,
  entwurf: Entwurf,
): AlertRuleDto[] {
  const alt = alle.find((r) => r.metricKey === metricKey)
  const plan = entwurf.quelle === 'Plan'
  const neu: AlertRuleDto = {
    metricKey,
    minValue: plan ? null : zahlOderNull(entwurf.min),
    maxValue: plan ? null : zahlOderNull(entwurf.max),
    notifyService: alt?.notifyService ?? '',
    enabled: entwurf.aktiv,
    cooldownMinutes: Math.max(1, Math.round(zahlOderNull(entwurf.karenz) ?? 30)),
    quelle: entwurf.quelle,
    toleranz: plan ? zahlOderNull(entwurf.toleranz) : null,
    nightMinValue: plan ? null : alt?.nightMinValue ?? null,
    nightMaxValue: plan ? null : alt?.nightMaxValue ?? null,
  }
  return alt
    ? alle.map((r) => (r.metricKey === metricKey ? neu : r))
    : [...alle, neu]
}
