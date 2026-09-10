/* src/useNavBar.ts — Fork AI
   Welche Ziele in der Leiste am oberen Rand stehen und in welcher Reihenfolge.

   Die Reihenfolge liegt im Server (AppSettings, /api/navbar), damit Telefon und
   Notebook dieselbe Leiste zeigen. Bis die Antwort da ist, gilt die
   Werkseinstellung — die Leiste ist also nie leer, auch nicht fuer den
   Sekundenbruchteil beim Laden. Genau das war beim Theme-Umschalter frueher der
   Fehler: kurz nichts, dann ein Sprung. */

import { useCallback, useEffect, useState } from 'react'
import { apiFetch } from './api'
import { barCandidates, defaultBarRoutes, type NavLeaf } from './navigation'

type NavBarDto = { items: string[] | null; dashboardPath: string }

/** Wenn der Server (noch) nicht antwortet: die Adresse, die jede HA-Installation hat. */
const DASHBOARD_FALLBACK = '/lovelace/0'

/** Alle Ereignisse gehen an alle Instanzen: der Anpassen-Modus sitzt woanders als die Leiste. */
const NAVBAR_EVENT = 'growos-navbar-changed'

function toLeaves(routes: string[]): NavLeaf[] {
  // Unbekannte Pfade fliegen hier raus, nicht im Server: welche Ziele es gibt,
  // weiss nur das Frontend. Ein Menuepunkt, den eine spaetere Version entfernt,
  // hinterlaesst damit keine tote Kachel.
  const found: NavLeaf[] = []
  for (const route of routes) {
    const leaf = barCandidates.find((item) => item.to === route)
    if (leaf && !found.includes(leaf)) found.push(leaf)
  }
  return found
}

function defaults(): NavLeaf[] {
  return toLeaves(defaultBarRoutes)
}

export function useNavBar(): {
  items: NavLeaf[]
  /** Ziel des Haus-Zeichens in der Titelzeile. */
  dashboardPath: string
  /** true, solange die gespeicherte Reihenfolge noch nicht da ist. */
  loading: boolean
  save: (routes: string[]) => Promise<void>
  reset: () => Promise<void>
  saveDashboardPath: (pfad: string) => Promise<void>
} {
  const [items, setItems] = useState<NavLeaf[]>(defaults)
  const [dashboardPath, setDashboardPath] = useState(DASHBOARD_FALLBACK)
  const [loading, setLoading] = useState(true)

  const apply = useCallback((dto: NavBarDto | null) => {
    const routes = dto?.items ?? null
    const leaves = routes && routes.length > 0 ? toLeaves(routes) : defaults()
    // Eine gespeicherte Liste, aus der nur noch entfernte Ziele uebrig sind,
    // faellt auf die Werkseinstellung zurueck statt auf eine leere Leiste.
    setItems(leaves.length > 0 ? leaves : defaults())
    if (dto?.dashboardPath) setDashboardPath(dto.dashboardPath)
  }, [])

  useEffect(() => {
    let abgebrochen = false
    apiFetch<NavBarDto>('/api/navbar')
      .then((dto) => {
        if (abgebrochen) return
        apply(dto ?? null)
      })
      .catch(() => {
        // Kein Drama und keine Meldung: ohne Antwort gilt die Werkseinstellung.
        // Eine Fehlerzeile ueber der Navigation waere hier schlimmer als das
        // Problem, das sie meldet.
      })
      .finally(() => {
        if (!abgebrochen) setLoading(false)
      })
    return () => {
      abgebrochen = true
    }
  }, [apply])

  useEffect(() => {
    const onChange = (event: Event) => {
      apply((event as CustomEvent<NavBarDto>).detail)
    }
    window.addEventListener(NAVBAR_EVENT, onChange)
    return () => window.removeEventListener(NAVBAR_EVENT, onChange)
  }, [apply])

  const senden = useCallback(async (body: { items?: string[]; dashboardPath?: string }) => {
    const dto = await apiFetch<NavBarDto>('/api/navbar', {
      method: 'PUT',
      body: JSON.stringify(body),
    })
    window.dispatchEvent(new CustomEvent<NavBarDto>(NAVBAR_EVENT, { detail: dto }))
  }, [])

  const save = useCallback((routes: string[]) => senden({ items: routes }), [senden])
  const reset = useCallback(() => senden({ items: [] }), [senden])
  const saveDashboardPath = useCallback((pfad: string) => senden({ dashboardPath: pfad }), [senden])

  return { items, dashboardPath, loading, save, reset, saveDashboardPath }
}
