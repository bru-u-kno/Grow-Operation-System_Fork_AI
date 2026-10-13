import { useEffect, useState } from 'react'
import { apiFetch } from '../../api'
import type { GrenzwertRegel } from './entfeuchtung-ziel'

/**
 * Fork AI (A-021): Woher kommt das Ziel? — der Überblick liest dazu `GET /api/zielwerte` (dieselbe
 * Antwort wie die Seite Grenzwerte): Grow, Programm und Woche des Plans und die Grenzwerte für
 * Luftfeuchte und Lufttemperatur.
 *
 * <b>Kein Schreibweg, kein Schlüssel</b> — Lesen geht ohne. Antwortet der Server nicht, bleibt der
 * Überblick ohne diese Angaben stehen und zeigt, was er aus den Entfeuchter-Daten selbst weiß.
 */

type Antwort = {
  growName?: string | null
  programm?: string | null
  woche?: string | null
  phase?: string | null
  werte?: Array<{ key: string; regel?: GrenzwertRegel | null; quelle?: string | null; quelleZusatz?: string | null }>
}

export type ZielwerteKurz = {
  growName: string | null
  programm: string | null
  woche: string | null
  feuchteRegel: GrenzwertRegel | null
  temperaturRegel: GrenzwertRegel | null
  /** Wie der Server die Herkunft des Grenzwerts beschreibt (vom Plan nachgezogen, von dir gesetzt …). */
  feuchteQuelle: string | null
  temperaturQuelle: string | null
}

/** Reiner Teil: aus der Antwort das Wenige machen, das der Überblick braucht. */
export function zielwerteKurz(antwort: Antwort): ZielwerteKurz {
  const wert = (key: string) => antwort.werte?.find((w) => w.key === key)
  const regel = (key: string) => wert(key)?.regel ?? null
  const quelle = (key: string) => wert(key)?.quelleZusatz ?? null
  return {
    growName: antwort.growName ?? null,
    programm: antwort.programm ?? null,
    woche: antwort.woche ?? null,
    feuchteRegel: regel('humidity'),
    temperaturRegel: regel('temperature'),
    feuchteQuelle: quelle('humidity'),
    temperaturQuelle: quelle('temperature'),
  }
}

export function useZielwerteKurz(): ZielwerteKurz | null {
  const [kurz, setKurz] = useState<ZielwerteKurz | null>(null)
  useEffect(() => {
    const controller = new AbortController()
    void apiFetch<Antwort>('/api/zielwerte', { signal: controller.signal })
      .then((antwort) => { if (!controller.signal.aborted) setKurz(zielwerteKurz(antwort)) })
      .catch(() => undefined)
    return () => controller.abort()
  }, [])
  return kurz
}
