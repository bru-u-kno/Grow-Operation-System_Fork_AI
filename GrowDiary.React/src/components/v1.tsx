import { Link } from 'react-router-dom'
import { useEffect, useRef, type ReactNode } from 'react'
import { classNames } from '../utils'
import { auslaufZuruecksetzen, reiterInsBild } from './reiter-ins-bild'

export type Tone = 'neutral' | 'ok' | 'warn' | 'critical' | 'accent'

export function V1Page({ eyebrow, title, subtitle, action, children, className }: { eyebrow?: string; title: string; subtitle?: string; action?: ReactNode; children: ReactNode; className?: string }) {
  return (
    <main className={classNames('v1-page', className)}>
      <section className="v1-hero">
        <div>
          {eyebrow && <div className="v1-eyebrow">{eyebrow}</div>}
          <h1>{title}</h1>
          {subtitle && <p>{subtitle}</p>}
        </div>
        {action && <div className="v1-hero-action">{action}</div>}
      </section>
      {children}
    </main>
  )
}

export function V1Section({ title, action, children, className }: { title: string; action?: ReactNode; children: ReactNode; className?: string }) {
  return (
    <section className={classNames('v1-section', className)}>
      <header className="v1-section-head">
        <h2>{title}</h2>
        {action}
      </header>
      <div className="v1-section-body">{children}</div>
    </section>
  )
}

export function V1Card({ children, className, tone = 'neutral' }: { children: ReactNode; className?: string; tone?: Tone }) {
  return <article className={classNames('v1-card', `tone-${tone}`, className)}>{children}</article>
}

export function V1Button({ children, onClick, type = 'button', disabled, variant = 'secondary', className, audit }: { children: ReactNode; onClick?: () => void; type?: 'button' | 'submit'; disabled?: boolean; variant?: 'primary' | 'secondary' | 'ghost' | 'danger'; className?: string; audit?: string }) {
  return <button type={type} className={classNames('v1-button', `is-${variant}`, className)} data-audit={audit} disabled={disabled} onClick={onClick}>{children}</button>
}

export function V1LinkButton({ to, children, variant = 'secondary', className }: { to: string; children: ReactNode; variant?: 'primary' | 'secondary' | 'ghost' | 'danger'; className?: string }) {
  return <Link to={to} className={classNames('v1-button', `is-${variant}`, className)}>{children}</Link>
}

export function V1Badge({ children, tone = 'neutral' }: { children: ReactNode; tone?: Tone }) {
  return <span className={classNames('v1-badge', `tone-${tone}`)}>{children}</span>
}

/**
 * Eine Kennzahl-Kachel.
 *
 * @param wortwert Für Werte, die ein <b>Wort</b> sind und kein Messwert.
 *
 * Kennzahlen stehen normalerweise auf einer Zeile — „5,79 · 1,02" darf nicht
 * umbrechen. Ein Wort schon: auf /grows/1 stand in der Sorten-Kachel
 * „gemischt (…)", weil `white-space: nowrap` den Text bei 4 px Überlauf
 * abschnitt und die ANZAHL wegfiel — also genau die Auskunft.
 */
export function V1Stat({ label, value, unit, hint, tone = 'neutral', wortwert = false }: { label: string; value: ReactNode; unit?: string | null; hint?: string | null; tone?: Tone; wortwert?: boolean }) {
  return (
    <div className={classNames('v1-stat', `tone-${tone}`, wortwert && 'is-wortwert')}>
      <span>{label}</span>
      <strong>{value}{unit && value !== '–' && <em>{unit}</em>}</strong>
      {hint && <small>{hint}</small>}
    </div>
  )
}

export function V1Empty({ title, text, action }: { title: string; text?: string; action?: ReactNode }) {
  return (
    <div className="v1-empty">
      <strong>{title}</strong>
      {text && <span>{text}</span>}
      {action}
    </div>
  )
}

/**
 * Platzhalter beim Laden — dieselben Kästen wie der fertige Inhalt.
 *
 * Statt „Lade ..." oder eines Spinners: die Boxen haben schon ihre endgültige
 * Höhe, nur die Werte sind Balken. Dadurch springt beim Eintreffen der Daten
 * nichts, und man sieht sofort, wie viel gleich kommt.
 *
 * `rows` sind Zeilen einer Liste, `tiles` Kacheln eines Rasters — mehr Formen
 * braucht es nicht, weil alle Seiten aus diesen beiden bestehen.
 */
export function V1Skeleton({ rows = 0, tiles = 0, label = 'Lädt' }: { rows?: number; tiles?: number; label?: string }) {
  return (
    <div className="v1-skeleton" role="status" aria-label={label} data-audit="skeleton">
      {tiles > 0 && (
        <div className="v1-skeleton-tiles">
          {Array.from({ length: tiles }, (_, index) => (
            <div key={index} className="v1-skeleton-tile">
              <span className="bar sm" />
              <span className="bar lg" />
            </div>
          ))}
        </div>
      )}
      {rows > 0 && (
        <div className="v1-skeleton-rows">
          {Array.from({ length: rows }, (_, index) => (
            <div key={index} className="v1-skeleton-row">
              <span className="bar md" />
              <span className="bar sm" />
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

export function V1Alert({ title, message, tone = 'warn' }: { title?: string; message: string; tone?: Tone }) {
  return (
    <div className={classNames('v1-alert', `tone-${tone}`)}>
      {title && <strong>{title}</strong>}
      <span>{message}</span>
    </div>
  )
}

export function V1Tabs<T extends string | number>({ items, active, onChange, label, insBild }: { items: Array<{ value: T; label: string; meta?: string | null; audit?: string }>; active: T; onChange: (value: T) => void; label?: string; /** Fork AI (F-048): nach dem Wechsel die Leiste unter die Kopfflaeche rollen — nur fuer Seiten-Reiter, nicht fuer Auswahlen in Blaettern. */ insBild?: boolean }) {
  const leiste = useRef<HTMLDivElement>(null)
  const abbrechen = useRef<(() => void) | null>(null)
  useEffect(() => () => {
    abbrechen.current?.()
    if (insBild) auslaufZuruecksetzen()
  }, [insBild])
  return (
    <div className={classNames('v1-tabs', insBild && 'scroll-ziel')} role="tablist" aria-label={label} ref={leiste}>
      {items.map((item) => (
        <button
          key={String(item.value)}
          type="button"
          className={classNames('v1-tab', item.value === active && 'active')}
          data-audit={item.audit}
          onClick={() => {
            onChange(item.value)
            if (insBild) {
              abbrechen.current?.()
              abbrechen.current = reiterInsBild(leiste.current)
            }
          }}
        >
          {/* The count rides in the label rather than on its own line: a second line
              was what forced the fixed heights and the truncation underneath them. */}
          {item.meta ? `${item.label} · ${item.meta}` : item.label}
        </button>
      ))}
    </div>
  )
}

export function V1Field({ label, children, hint, wide }: { label: string; children: ReactNode; hint?: string | null; wide?: boolean }) {
  return (
    <label className={classNames('v1-field', wide && 'is-wide')}>
      <span>{label}</span>
      {children}
      {hint && <small>{hint}</small>}
    </label>
  )
}

export function V1Switch({ label, checked, onChange, hint }: { label: string; checked: boolean; onChange: (checked: boolean) => void; hint?: string }) {
  return (
    <label className="v1-switch">
      <input type="checkbox" checked={checked} onChange={(event) => onChange(event.target.checked)} />
      <span>
        <strong>{label}</strong>
        {hint && <small>{hint}</small>}
      </span>
    </label>
  )
}

