import { renderToStaticMarkup } from 'react-dom/server'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { KiAktivKontext, type KiAktivZustand } from './ki-aktiv'
import { NurMitKi } from './NurMitKi'
import { navGroups, sichtbareGruppen, suchbareSeiten } from './navigation'

/**
 * Der globale Schalter „KI-Funktionen" (A-011) — ohne Browser.
 *
 * - Bei „aus" verschwindet jede KI-Seite aus Menü und Suche.
 * - `NurMitKi` zeigt die Seite nur bei „an", wartet aber auf die Antwort des Servers.
 */

const zustand = (aktiv: boolean, geladen = true): KiAktivZustand => ({ aktiv, geladen, setzen: async () => {} })

function seite(z: KiAktivZustand): string {
  return renderToStaticMarkup(
    <KiAktivKontext.Provider value={z}>
      <MemoryRouter>
        <NurMitKi><p>KI-Seite</p></NurMitKi>
      </MemoryRouter>
    </KiAktivKontext.Provider>,
  )
}

describe('KI-Funktionen: Menü und Suche', () => {
  const alleBlaetter = navGroups.flatMap((g) => g.items)

  it('jedes Ziel unter /ki ist als KI-Seite markiert', () => {
    const kiZiele = alleBlaetter.filter((b) => b.to === '/ki' || b.to.startsWith('/ki/'))
    // Wächter: Die Erkennung muss die KI-Seite sehen, sonst wäre die Prüfung grundlos grün.
    expect(kiZiele.length).toBeGreaterThan(0)
    expect(kiZiele.every((b) => b.ki === true)).toBe(true)
  })

  it('bei „KI aus" fehlt /ki im Menü, bei „KI an" steht es da', () => {
    const route = (kiAktiv: boolean) => sichtbareGruppen(undefined, kiAktiv).flatMap((g) => g.items).map((i) => i.to)
    expect(route(true)).toContain('/ki')
    expect(route(false)).not.toContain('/ki')
    // Alles andere bleibt.
    expect(route(false)).toContain('/steuerung')
  })

  it('bei „KI aus" findet die Suche keine KI-Seite', () => {
    expect(suchbareSeiten(true).map((s) => s.route)).toContain('/ki')
    expect(suchbareSeiten(false).map((s) => s.route)).not.toContain('/ki')
  })

  it('der Standard ohne Angabe zeigt alles (bestehende Aufrufer)', () => {
    expect(sichtbareGruppen().flatMap((g) => g.items).map((i) => i.to)).toContain('/ki')
  })
})

describe('NurMitKi', () => {
  it('zeigt die Seite bei „an"', () => {
    expect(seite(zustand(true))).toContain('KI-Seite')
  })

  it('zeigt bei „aus" nichts von der Seite', () => {
    expect(seite(zustand(false))).not.toContain('KI-Seite')
  })

  it('wartet auf die Antwort des Servers: noch nichts, auch kein Weiterleiten', () => {
    expect(seite(zustand(false, false))).toBe('')
  })
})
