import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch } from '../../api'
import type { NotificationSettingsDto } from '../../types/notification'
import { classNames } from '../../utils'
import { geraeteName } from './plan-kette'
import './zielwerte.css'

/**
 * Fork AI (forkai.133): die Kette „1 · Plan › 2 · Grenzwerte › 3 · Handy" oben
 * auf allen drei Seiten — sie waren bis 132 Reiter EINER Seite und hängen
 * inhaltlich zusammen: Ziel → ab wann kritisch → was aufs Handy kommt.
 */
export type KettenGlied = 'plan' | 'grenzwerte' | 'handy'

const GLIEDER: ReadonlyArray<{ key: KettenGlied; label: string; to: string }> = [
  { key: 'plan', label: 'Plan', to: '/plan' },
  { key: 'grenzwerte', label: 'Grenzwerte', to: '/grenzwerte' },
  { key: 'handy', label: 'Handy', to: '/handy?tab=push' },
]

export function PlanKette({ aktiv }: { aktiv: KettenGlied }) {
  return (
    <nav className="pk-kette" aria-label="Plan, Grenzwerte, Handy" data-audit="plan-kette">
      {GLIEDER.map((g, i) => (
        <span key={g.key} className="pk-glied-wrap">
          {i > 0 && <span className="pk-pfeil" aria-hidden="true">›</span>}
          {g.key === aktiv
            ? <span className="pk-glied ist-aktiv" aria-current="page">{i + 1} · {g.label}</span>
            : <Link className="pk-glied" to={g.to}>{i + 1} · {g.label}</Link>}
        </span>
      ))}
    </nav>
  )
}

/** Ein Sprung an Ort und Stelle: kurzer Satz links, Link rechts. */
export function KontextSprung({ text, to, label, audit }: { text: string; to: string; label: string; audit?: string }) {
  return (
    <div className="pk-kontext" data-audit={audit}>
      <span>{text}</span>
      <Link className="pk-link" to={to}>{label} ›</Link>
    </div>
  )
}

/**
 * Oben auf „Grenzwerte": kommt eine Grenzwert-Meldung überhaupt aufs Handy?
 * Ohne Push wären alle Grenzen hier stumm — das soll man sehen, ohne zu suchen.
 */
export function PushStand() {
  const [stand, setStand] = useState<NotificationSettingsDto | null>(null)
  useEffect(() => {
    let aktiv = true
    apiFetch<NotificationSettingsDto>('/api/notifications/settings')
      .then((s) => { if (aktiv) setStand(s) })
      .catch(() => { /* ohne Einstellungen kein Hinweis — die Seite geht trotzdem */ })
    return () => { aktiv = false }
  }, [])
  if (!stand) return null

  const ohneGeraet = !(stand.notifyService ?? '').trim()
  const text = ohneGeraet
    ? 'Push aufs Handy ist noch nicht eingerichtet'
    : stand.thresholds
      ? `Push für Grenzwerte ist an · ${geraeteName(stand.notifyService ?? '')}`
      : 'Push für Grenzwerte ist aus'
  return (
    <div className={classNames('pk-kontext', (ohneGeraet || !stand.thresholds) && 'ist-warn')} data-audit="grenzwerte-push-stand">
      <span>{text}</span>
      <Link className="pk-link" to="/handy?tab=push">Handy ›</Link>
    </div>
  )
}
