import { describe, expect, it } from 'vitest'
import { insBildHolen, rollArt } from './ins-bild'

/**
 * Fork AI (A-003, 04.10.2026): Der Kasten mit dem Klartext eines neuen
 * Schlüssels wird nach dem Anlegen oben unter die Kopfleiste gerollt und bekommt
 * den Fokus. Ob er am Handy wirklich dort steht, misst
 * `e2e/ki-zugriff-rundweg.spec.ts` — hier nur, was die Funktion verlangt.
 */

function fenster(weniger: boolean) {
  return { matchMedia: (abfrage: string) => ({ matches: weniger && abfrage === '(prefers-reduced-motion: reduce)' }) as MediaQueryList }
}

function ziel() {
  const aufrufe: string[] = []
  const el = {
    scrollIntoView: () => { aufrufe.push('scrollIntoView') },
    focus: (arg?: FocusOptions) => { aufrufe.push(`fokus ${JSON.stringify(arg)}`) },
  } as unknown as HTMLElement
  const rollen = (z: () => HTMLElement | null, art: ScrollBehavior) => {
    aufrufe.push(`rollen ${art} ${z() === el ? 'kasten' : 'anderes'}`)
    return () => { aufrufe.push('abgebrochen') }
  }
  return { el, aufrufe, rollen }
}

describe('Klartext ins Bild holen', () => {
  it('setzt den Fokus ohne Sprung und rollt dann selbst — nie per scrollIntoView', () => {
    const { el, aufrufe, rollen } = ziel()
    insBildHolen(el, fenster(false), rollen)
    expect(aufrufe).toEqual(['fokus {"preventScroll":true}', 'rollen smooth kasten'])
  })

  it('springt ohne Lauf, wenn „Bewegung reduzieren" eingestellt ist', () => {
    const { el, aufrufe, rollen } = ziel()
    insBildHolen(el, fenster(true), rollen)
    expect(aufrufe).toContain('rollen auto kasten')
    expect(rollArt(fenster(true))).toBe('auto')
    expect(rollArt(fenster(false))).toBe('smooth')
  })

  it('gibt das Abbrechen weiter — der Effekt räumt beim Aushängen auf', () => {
    const { el, aufrufe, rollen } = ziel()
    insBildHolen(el, fenster(false), rollen)()
    expect(aufrufe.at(-1)).toBe('abgebrochen')
  })

  it('kommt ohne matchMedia und ohne Ziel aus', () => {
    expect(rollArt({} as Pick<Window, 'matchMedia'>)).toBe('smooth')
    expect(() => insBildHolen(null)()).not.toThrow()
  })
})
