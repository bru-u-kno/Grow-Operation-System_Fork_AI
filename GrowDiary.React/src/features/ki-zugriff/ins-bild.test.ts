import { describe, expect, it } from 'vitest'
import { insBildHolen, rollArt } from './ins-bild'

/**
 * Fork AI (A-003, 04.10.2026): Der Kasten mit dem Klartext eines neuen
 * Schlüssels wird nach dem Anlegen in den Blick gerollt und bekommt den Fokus.
 * Ob er am Handy wirklich im Bild steht, misst `e2e/ki-zugriff-rundweg.spec.ts`
 * bei 412 px — hier nur, was die Funktion verlangt.
 */

function fenster(weniger: boolean) {
  return { matchMedia: (abfrage: string) => ({ matches: weniger && abfrage === '(prefers-reduced-motion: reduce)' }) as MediaQueryList }
}

function ziel() {
  const aufrufe: string[] = []
  const el = {
    scrollIntoView: (arg?: boolean | ScrollIntoViewOptions) => { aufrufe.push(`rollen ${JSON.stringify(arg)}`) },
    focus: (arg?: FocusOptions) => { aufrufe.push(`fokus ${JSON.stringify(arg)}`) },
  }
  return { el, aufrufe }
}

describe('Klartext ins Bild holen', () => {
  it('rollt sanft und setzt danach den Fokus, ohne dass der Fokus selbst springt', () => {
    const { el, aufrufe } = ziel()
    insBildHolen(el, fenster(false))
    expect(aufrufe).toEqual([
      'rollen {"block":"nearest","behavior":"smooth"}',
      'fokus {"preventScroll":true}',
    ])
  })

  it('springt ohne Lauf, wenn „Bewegung reduzieren" eingestellt ist', () => {
    const { el, aufrufe } = ziel()
    insBildHolen(el, fenster(true))
    expect(aufrufe[0]).toBe('rollen {"block":"nearest","behavior":"auto"}')
    expect(rollArt(fenster(true))).toBe('auto')
    expect(rollArt(fenster(false))).toBe('smooth')
  })

  it('kommt ohne matchMedia und ohne Ziel aus', () => {
    expect(rollArt({} as Pick<Window, 'matchMedia'>)).toBe('smooth')
    expect(() => insBildHolen(null)).not.toThrow()
  })
})
