import { useEffect, useRef, useState } from 'react'
import { apiFetch } from '../../api'
import type { TentHistory } from '../../components/SensorChart'
import { VERLAUFS_METRIKEN } from './useTentSparklines'

export type Wochenverlauf = {
  /** Je Messgröße die Rohwerte der letzten 7 Tage; null, solange nichts geladen ist. */
  daten: Map<string, { t: number; v: number }[]> | null
  laedt: boolean
  fehler: boolean
}

/**
 * Die Rohwerte der letzten sieben Tage — erst, wenn jemand sie braucht.
 *
 * Die 24 Stunden lädt die Live-Seite ohnehin (`useTentSparklines`). Sieben
 * Tage sind bei fünf Minuten Takt und vierzehn Messgrößen gut 28 000 Punkte; die
 * holt die Verlaufs-Kachel nur, wenn „7 Tage" gewählt oder über die 24 h
 * hinaus zurückgeblättert wird — und dann einmal, nicht bei jedem Blättern.
 */
export function useWochenverlauf(tentId: number | null, benoetigt: boolean): Wochenverlauf {
  const [stand, setStand] = useState<Wochenverlauf & { fuer: number | null }>({ daten: null, laedt: false, fehler: false, fuer: null })
  // Für welches Zelt schon angefragt wurde. Ein Ref und kein State: der
  // State-Wechsel auf „lädt" darf die laufende Anfrage nicht über die
  // Abhängigkeiten des Effekts selbst wieder abbrechen.
  const angefragt = useRef<number | null>(null)

  useEffect(() => {
    if (!benoetigt || tentId == null || angefragt.current === tentId) return
    angefragt.current = tentId
    const controller = new AbortController()
    let fertig = false
    setStand({ daten: null, laedt: true, fehler: false, fuer: tentId })
    apiFetch<TentHistory>(
      `/api/tents/${tentId}/history?metrics=${VERLAUFS_METRIKEN.join(',')}&days=7&resolution=raw`,
      { signal: controller.signal },
    )
      .then((history) => {
        fertig = true
        if (controller.signal.aborted) return
        const daten = new Map(history.series
          .filter((series) => series.points.length > 1)
          .map((series) => [series.metricKey, series.points.map((p) => ({ t: new Date(p.t).getTime(), v: p.v }))]))
        setStand({ daten, laedt: false, fehler: false, fuer: tentId })
      })
      .catch(() => {
        fertig = true
        if (!controller.signal.aborted) setStand({ daten: null, laedt: false, fehler: true, fuer: tentId })
      })
    return () => {
      controller.abort()
      // Abgebrochen, ehe die Antwort kam (Zeltwechsel, oder der doppelte
      // Effektlauf im Entwicklungsmodus): dann darf der nächste Lauf neu fragen.
      if (!fertig) {
        angefragt.current = null
        // Sonst stünde „Lade …" für immer da, obwohl niemand mehr lädt.
        setStand((alt) => (alt.laedt ? { ...alt, laedt: false } : alt))
      }
    }
  }, [benoetigt, tentId])

  if (stand.fuer !== tentId) return { daten: null, laedt: false, fehler: false }
  return { daten: stand.daten, laedt: stand.laedt, fehler: stand.fehler }
}
