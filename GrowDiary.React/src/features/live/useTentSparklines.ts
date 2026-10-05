import { useEffect, useState } from 'react'
import { apiFetch } from '../../api'
import type { HistoryPoint, TentHistory } from '../../components/SensorChart'

/**
 * Die Werte, hinter denen eine 24-Stunden-Kurve stehen kann.
 *
 * Exportiert, weil die Verlaufs-Kachel dieselbe Liste braucht: einen Wert
 * anzubieten, der nie eine Linie ergibt (etwa „Licht", ein Zustand), waere eine
 * leere Zusage.
 *
 * Alles in EINEM Abruf — die Liste muss deshalb in die Obergrenze des
 * Endpunkts passen (`SensorHistoryApiController.MaxMetrics`), sonst fallen die
 * letzten Werte still weg. Gehalten von `messgroessen-vollstaendig.test.ts`.
 */
export const VERLAUFS_METRIKEN = [
  'temperature', 'humidity', 'vpd',
  // Die Luft außerhalb des Zelts direkt hinter der innen — so liegen die
  // Karten, die man vergleicht, nebeneinander.
  'outside-temperature', 'outside-humidity', 'outside-vpd',
  'co2', 'ppfd',
  'reservoir-ph', 'reservoir-ec', 'reservoir-temp', 'reservoir-level', 'orp', 'dissolved-oxygen',
] as const

const SPARK_METRICS = VERLAUFS_METRIKEN.join(',')

/**
 * Die letzten 24 Stunden je Messwert, in EINEM Abruf.
 *
 * Selten aktualisiert: die Zahl auf der Kachel kommt alle 30 s frisch, der
 * Verlauf dahinter ändert sich ungleich langsamer. Fehlt die Historie, bleibt
 * es bei der Kachel ohne Kurve — sie ist die Zugabe, nicht der Inhalt.
 */
export function useTentSparklines(tentId: number | null): Map<string, HistoryPoint[]> {
  const [byMetric, setByMetric] = useState<Map<string, HistoryPoint[]>>(new Map())

  useEffect(() => {
    const controller = new AbortController()

    async function load() {
      if (tentId == null) {
        setByMetric(new Map())
        return
      }
      try {
        const history = await apiFetch<TentHistory>(
          `/api/tents/${tentId}/history?metrics=${SPARK_METRICS}&days=1&resolution=raw`,
          { signal: controller.signal },
        )
        if (controller.signal.aborted) return
        // Ein einzelner Punkt ist keine Kurve — dann lieber das Zielband zeigen.
        setByMetric(new Map(history.series.filter((series) => series.points.length > 1).map((series) => [series.metricKey, series.points])))
      } catch {
        if (!controller.signal.aborted) setByMetric(new Map())
      }
    }

    void load()
    const timer = window.setInterval(() => void load(), 5 * 60 * 1000)
    return () => {
      controller.abort()
      window.clearInterval(timer)
    }
  }, [tentId])

  return byMetric
}
