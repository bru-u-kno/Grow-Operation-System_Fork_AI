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
 * Fork AI: Die zuletzt geladene Woche je Zelt, für ein paar Minuten.
 *
 * Seit eine angetippte Kachel das Verlaufsdiagramm öffnet, hängt es sich bei
 * jedem Öffnen neu ein — und holte jedes Mal die ganze Woche (gut 20 000
 * Punkte; Befund des Prüfers, 05.10.2026). Was älter als 24 h ist, ändert
 * sich nicht mehr; das Frischeste kommt ohnehin aus den 24 h der Live-Seite
 * (`zusammenfuehren`). Fünf Minuten wie deren Takt.
 */
const WOCHE_GILT_MS = 5 * 60 * 1000
const geladeneWochen = new Map<number, { zeit: number; daten: Map<string, { t: number; v: number }[]> }>()

function gemerkteWoche(tentId: number | null) {
  if (tentId == null) return null
  const eintrag = geladeneWochen.get(tentId)
  return eintrag && Date.now() - eintrag.zeit < WOCHE_GILT_MS ? eintrag.daten : null
}

/**
 * Die Rohwerte der letzten sieben Tage — erst, wenn jemand sie braucht.
 *
 * Die 24 Stunden lädt die Live-Seite ohnehin (`useTentSparklines`). Sieben
 * Tage sind bei fünf Minuten Takt und elf Messgrößen gut 20 000 Punkte; die
 * holt die Verlaufs-Kachel nur, wenn „7 Tage" gewählt oder über die 24 h
 * hinaus zurückgeblättert wird — und dann einmal, nicht bei jedem Blättern.
 */
export function useWochenverlauf(tentId: number | null, benoetigt: boolean): Wochenverlauf {
  const [stand, setStand] = useState<Wochenverlauf & { fuer: number | null }>(() => {
    const daten = gemerkteWoche(tentId)
    return { daten, laedt: false, fehler: false, fuer: daten ? tentId : null }
  })
  // Für welches Zelt schon angefragt wurde. Ein Ref und kein State: der
  // State-Wechsel auf „lädt" darf die laufende Anfrage nicht über die
  // Abhängigkeiten des Effekts selbst wieder abbrechen.
  const angefragt = useRef<number | null>(stand.fuer)

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
        geladeneWochen.set(tentId, { zeit: Date.now(), daten })
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
