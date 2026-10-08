import type { SteuerungModul } from './steuerung-typen'
import './steuerung.css'

/**
 * Fork AI (A-015): Die Auswahl der Steuerungen — steht ganz oben auf jeder
 * Steuerungsseite, noch über der Überschrift (Bru, 08.10.2026), damit man von
 * jeder Seite zur nächsten springt, ohne erst am Titel vorbeizuscrollen.
 */
export function SteuerungWechsel({ module, aktiv, onWechsel }: {
  module: SteuerungModul[]
  aktiv: string
  onWechsel: (kennung: string) => void
}) {
  return (
    <div className="st-wechsel" role="tablist" aria-label="Steuerung wechseln">
      {module.map((m) => (
        <button
          key={m.kennung}
          type="button"
          role="tab"
          className="st-chip"
          aria-current={m.kennung === aktiv}
          disabled={!m.hatDetail}
          onClick={() => onWechsel(m.kennung)}
        >
          <i className={m.status === 'an' ? 'is-an' : m.status === 'warn' ? 'is-warn' : ''} aria-hidden="true" />
          {m.titel}
        </button>
      ))}
    </div>
  )
}
