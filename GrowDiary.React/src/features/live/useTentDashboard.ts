import { useCallback, useEffect, useState } from 'react'
import { apiFetch } from '../../api'
import type { DashboardLayout, EntityValue } from './dashboard-layout'

/**
 * Die Anordnung des Zelts plus die Werte der Kacheln, die auf beliebige
 * Home-Assistant-Entitäten zeigen.
 *
 * Die eigenen Messwerte kommen weiter aus dem Live-Payload — nur so behalten
 * sie Zielbereich und Ampelfarbe. Dieser Hook füllt auf, was Cultivate Control nicht
 * selbst misst.
 */
export function useTentDashboard(tentId: number | null) {
  const [layout, setLayout] = useState<DashboardLayout | null>(null)
  const [entityValues, setEntityValues] = useState<Map<string, EntityValue>>(new Map())
  const [reloadKey, setReloadKey] = useState(0)

  const reload = useCallback(() => setReloadKey((key) => key + 1), [])

  useEffect(() => {
    const controller = new AbortController()

    async function load() {
      if (tentId == null) {
        setLayout(null)
        return
      }
      try {
        const data = await apiFetch<DashboardLayout>(`/api/tents/${tentId}/dashboard`, { signal: controller.signal })
        if (!controller.signal.aborted) setLayout(data)
      } catch {
        // Ohne Layout zeichnet der Bildschirm seine eingebaute Anordnung — das ist
        // kein Fehlerfall, sondern der Normalzustand für alle, die nichts anpassen.
        if (!controller.signal.aborted) setLayout(null)
      }
    }

    void load()
    return () => controller.abort()
  }, [tentId, reloadKey])

  useEffect(() => {
    const controller = new AbortController()
    const hasCustom = (layout?.sections ?? []).some((section) => section.tiles.some((tile) => tile.kind === 'Entity'))

    async function loadValues() {
      if (tentId == null || !hasCustom) {
        setEntityValues(new Map())
        return
      }
      try {
        const values = await apiFetch<EntityValue[]>(`/api/tents/${tentId}/dashboard/values`, { signal: controller.signal })
        if (!controller.signal.aborted) setEntityValues(new Map(values.map((value) => [value.entityId, value])))
      } catch {
        // Die vorherigen Werte stehen lassen, statt die Kacheln leer zu räumen.
      }
    }

    void loadValues()
    const timer = window.setInterval(() => void loadValues(), 30_000)
    return () => {
      controller.abort()
      window.clearInterval(timer)
    }
  }, [tentId, layout])

  return { layout, setLayout, entityValues, reload }
}
