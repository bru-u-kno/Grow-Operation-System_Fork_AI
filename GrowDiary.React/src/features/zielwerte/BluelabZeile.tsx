import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch } from '../../api'
import { rollenPfad } from '../geraete/rollenPfad'

/**
 * Fork AI (forkai.141, F-037): Die Grenzwerte dieser Seite gehen auch an die
 * Alarmgrenzen des Bluelab Guardian. Eine Zeile — nur, wenn ein Gerät
 * zugeordnet ist. Wer kein Bluelab hat, sieht nichts.
 */
type Grenze = { rolle: string; soll: number | null; geraet: number | null }
type Stand = { eingerichtet: boolean; zuletztUtc: string | null; geschrieben: number; fehler: string | null; grenzen: Grenze[] }

export function BluelabZeile() {
  const [stand, setStand] = useState<Stand | null>(null)
  useEffect(() => {
    let aktiv = true
    apiFetch<Stand>('/api/steuerung/bluelab')
      .then((s) => { if (aktiv) setStand(s) })
      .catch(() => { /* ohne Stand kein Hinweis — die Seite geht trotzdem */ })
    return () => { aktiv = false }
  }, [])
  if (!stand?.eingerichtet) return null

  const abweichend = stand.grenzen.filter((g) => g.soll != null && g.geraet != null && Math.abs(g.soll - g.geraet) >= 0.05).length
  const zuletzt = stand.zuletztUtc
    ? new Date(stand.zuletztUtc).toLocaleString('de-DE', { dateStyle: 'short', timeStyle: 'short' })
    : null
  const text = stand.fehler
    ? `Bluelab-Gerät · Übertragung gestört (${stand.fehler})`
    : abweichend > 0
      ? `Bluelab-Gerät · ${abweichend} ${abweichend === 1 ? 'Grenze wird' : 'Grenzen werden'} übertragen`
      : `Bluelab-Gerät · Grenzen übernommen${zuletzt ? ` · zuletzt ${zuletzt}` : ''}`

  return (
    <div className={stand.fehler ? 'pk-kontext ist-warn' : 'pk-kontext'} data-audit="grenzwerte-bluelab-stand">
      <span>{text}</span>
      <Link className="pk-link" to={rollenPfad('bluelab')}>Rollen ›</Link>
    </div>
  )
}
