import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { apiFetch } from '../../api'
import { V1Sheet } from '../../components/V1Sheet'
import { V1Alert } from '../../components/v1'
import { classNames } from '../../utils'
import '../wochenplan/wochenplan.css'
import { anker, waehlePlan, wochenKurz, type WochenPlan } from './wochen-zeile'

/**
 * Fork AI (forkai.125): Die Woche des laufenden Grows als schmale Zeile über
 * den Reitern von „Ziele & Meldungen“ — ersetzt den früheren Menüpunkt
 * „Wochenplan“. Ein Tipp öffnet das Wochen-Blatt mit allen Wochen; eine Woche
 * darin öffnet den Reiter „Plan“ genau an dieser Woche.
 *
 * Fehlt ein Plan, bleibt die Zeile weg: die Reiter sagen dann selbst, warum.
 */
export function WochenZeile() {
  const [plan, setPlan] = useState<WochenPlan | null>(null)
  const [offen, setOffen] = useState(false)
  const [params, setParams] = useSearchParams()

  useEffect(() => {
    let aktiv = true
    async function laden() {
      try {
        const [plaene, ziel] = await Promise.all([
          apiFetch<WochenPlan[]>('/api/wochenplan'),
          apiFetch<{ growId: number | null }>('/api/zielwerte').catch(() => ({ growId: null })),
        ])
        if (aktiv) setPlan(waehlePlan(plaene, ziel.growId))
      } catch {
        // Die Zeile ist eine Zugabe — ohne sie funktionieren alle Reiter weiter.
      }
    }
    void laden()
    return () => { aktiv = false }
  }, [])

  if (!plan) return null

  function zurWoche(id: string) {
    setOffen(false)
    const next = new URLSearchParams(params)
    next.set('tab', 'plan')
    next.set('woche', id)
    setParams(next, { replace: true })
  }

  const punkte = anker(plan)

  return (
    <div className="zw-woche" data-audit="wochen-zeile">
      <button type="button" className="zw-woche-kopf" onClick={() => setOffen(true)} aria-haspopup="dialog" data-audit="wochen-zeile-oeffnen">
        <span className="zw-woche-titel">
          <b>{plan.jetztLabel ?? 'keine passende Woche'}</b>
          {plan.sorte && <span className="wp-leise"> · {plan.sorte}</span>}
        </span>
        <span className="zw-woche-alle">Alle Wochen</span>
      </button>
      {plan.haltehinweis && <p className="wp-halt">{plan.haltehinweis}</p>}
      {punkte.length > 0 && (
        <div className="wp-anker">
          {punkte.map((a) => (
            <span key={a.name} className="wp-pill">
              {a.name} <b>{a.wert}</b>
            </span>
          ))}
        </div>
      )}

      <V1Sheet open={offen} onClose={() => setOffen(false)} title="Wochen" subtitle={`${plan.programmName} · ${plan.growName}`}>
        {!plan.wochenZieleAktiv && (
          <V1Alert
            tone="neutral"
            message="Dieser Grow hat noch keinen eigenen Plan. Die Werte gelten fürs Anmischen, nicht für Kacheln und Alarme — wähle am Grow ein Düngeprogramm, dann entsteht der Plan."
          />
        )}
        <div className="wp-liste" data-audit="wochen-blatt">
          {plan.wochen.map((w) => (
            <button
              key={w.id}
              type="button"
              className={classNames('wp-zeile', 'zw-woche-zeile', w.istJetzt && 'ist-jetzt')}
              aria-current={w.istJetzt ? 'true' : undefined}
              onClick={() => zurWoche(w.id)}
            >
              <span className="wp-zeile-l">
                {w.label}
                {w.wirdGehalten && <span className="wp-leise"> · gehalten</span>}
              </span>
              <span className="wp-zeile-w">{wochenKurz(w)}</span>
            </button>
          ))}
        </div>
      </V1Sheet>
    </div>
  )
}
