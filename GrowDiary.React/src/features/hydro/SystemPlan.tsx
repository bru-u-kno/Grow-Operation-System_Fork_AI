/* src/features/hydro/SystemPlan.tsx
   Ersetzt components/RdwcPreview.tsx.
   Zeichnet die Draufsicht: Zeltrahmen im Maßstab, Eimer nach Litern,
   Tank nach Volumen, Ruecklauf-Manifold, Sammelleitung, Zulauf. */

import type { CSSProperties } from 'react'
import { buildSystemPlan, type PlanInput, type SystemPlan } from './system-plan-model'
import { fitMessage } from './system-plan-text'
import './system-plan.css'

type Props = PlanInput & {
  /** Kompakt = ohne Legende und Prüfzeile (z. B. in einer Listenzeile). */
  compact?: boolean
  className?: string
}

export function SystemPlan(props: Props) {
  const plan = buildSystemPlan(props)
  const pct = (value: number, total: number) => `${((value / total) * 100).toFixed(2)}%`

  const box = (r: { x: number; y: number; w: number; h: number }): CSSProperties => ({
    left: pct(r.x, plan.frame.w),
    top: pct(r.y, plan.frame.h),
    width: pct(r.w, plan.frame.w),
    height: pct(r.h, plan.frame.h),
  })

  return (
    <figure className={['system-plan', props.compact ? 'is-compact' : '', props.className ?? ''].filter(Boolean).join(' ')}>
      {!props.compact && (
        <figcaption className="system-plan__head">
          <span>Draufsicht · maßstabsgetreu</span>
          <span>{formatTent(props)}</span>
        </figcaption>
      )}

      <div className="system-plan__stage" style={{ aspectRatio: `${plan.frame.w} / ${plan.frame.h}` }}>
        <div className="system-plan__tent" style={box(plan.tent)} />

        {plan.pipes.map((pipe, index) => (
          <div key={`pipe-${index}`} className={`system-plan__pipe is-${pipe.kind}`} style={box(pipe)} />
        ))}

        {plan.tank && (
          <div className="system-plan__tank" style={box(plan.tank)}>
            <span>Tank</span>
            <strong>{Math.round(props.tankLiters)}{'\u00a0'}L</strong>
          </div>
        )}

        {plan.sites.map((site) => (
          <div
            key={site.index}
            className="system-plan__site"
            style={{
              left: pct(site.cx - site.diameterCm / 2, plan.frame.w),
              top: pct(site.cy - site.diameterCm / 2, plan.frame.h),
              width: pct(site.diameterCm, plan.frame.w),
            }}
          >
            <span className="system-plan__netpot">{props.hydroStyle === 'DWC' ? 'DWC' : site.index}</span>
          </div>
        ))}
      </div>

      {!props.compact && (
        <div className="system-plan__legend">
          <span><i className="swatch is-return" />Rücklauf 50 mm</span>
          <span><i className="swatch is-feed" />Zulauf / Pumpe</span>
          <span><i className="swatch is-site" />Site {Math.round(props.potLiters)}{'\u00a0'}L</span>
          <span className={plan.fits ? 'check is-ok' : 'check is-bad'}>{fitMessage(plan)}</span>
        </div>
      )}
    </figure>
  )
}

function formatTent(input: PlanInput): string {
  if (!input.tentWidthCm || !input.tentDepthCm) return 'Zeltmaß nicht hinterlegt'
  return `${Math.round(input.tentWidthCm)}×${Math.round(input.tentDepthCm)}\u00a0cm`
}

