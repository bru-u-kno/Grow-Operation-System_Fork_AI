import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'

/**
 * Der Weg zurück nach Home Assistant darf kein Seitenwechsel sein.
 *
 * <b>Der Anlass.</b> Das „⌂"-Zeichen setzte `window.top.location.href`. Im
 * Browser sieht das richtig aus. In der Home-Assistant-App für Android nicht:
 * die App reicht jeden echten Seitenwechsel an den Standardbrowser weiter, und
 * statt im Dashboard landete man in Firefox — außerhalb der App, ohne den Weg
 * zurück.
 *
 * <b>Was hier geprüft wird.</b> Der Rücksprung im HA-Rahmen benutzt die
 * Verlaufsliste plus das Ereignis `location-changed`, mit dem das Frontend
 * seine Ansicht wechselt. `location.href` bleibt nur die Rückfallebene für den
 * Fall, dass Grow OS gar nicht im Rahmen steckt (eigener Tab, fremder Proxy).
 */
describe('Rücksprung nach Home Assistant', () => {
  const quelle = readFileSync(new URL('./useHomeAssistantFrame.ts', import.meta.url), 'utf8')

  const funktion = quelle.slice(quelle.indexOf('export function zurueckZuHomeAssistant'))

  it('sieht die Funktion überhaupt', () => {
    // Sonst prüft der Test nichts und ist trotzdem grün.
    expect(funktion.length).toBeGreaterThan(200)
  })

  it('wechselt die Ansicht über das Frontend statt über die Adresszeile', () => {
    expect(funktion).toContain('pushState')
    expect(funktion).toContain('location-changed')
  })

  it('lädt im Rahmen keine neue Seite', () => {
    // Erlaubt ist nur das eine `window.location.href` der Rückfallebene —
    // nichts, was das oberste Fenster neu lädt.
    expect(funktion).not.toContain('top.location.href')
    expect(funktion).not.toContain('oben.location')
    expect(funktion.match(/location\.href/g) ?? []).toHaveLength(1)
  })
})
