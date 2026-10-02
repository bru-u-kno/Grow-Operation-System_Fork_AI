import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
import { buchText } from './plan-reiter'

/**
 * Jede Art im Änderungsbuch hat einen Satz.
 *
 * <b>Der Anlass (02.10.2026).</b> Mit den angehängten Wochen kam die Art
 * `verlaengert` dazu. `buchText` fällt bei einer unbekannten Art auf die rohe
 * Kennung zurück — im Buch stünde „verlaengert". Eine Liste im Test könnte nur an
 * dem scheitern, was schon draufsteht; deshalb kommt die Grundmenge aus der
 * Datei, in der das Backend die Arten führt (`GrowPlanArten`).
 */
describe('Änderungsbuch', () => {
  const quelle = readFileSync(new URL('../../../../GrowDiary.Web/Models/GrowPlan.cs', import.meta.url), 'utf8')
  const block = quelle.slice(quelle.indexOf('public static class GrowPlanArten'))
  const ende = block.indexOf('\n}')
  const arten = [...block.slice(0, ende).matchAll(/public const string \w+ = "([^"]+)";/g)].map((t) => t[1])

  it('sieht seine Grundmenge', () => {
    // Selbsttest: ohne ihn liefe die Prüfung bei geänderter Schreibweise null Mal.
    expect(arten.length).toBeGreaterThanOrEqual(9)
    expect(arten).toContain('verlaengert')
  })

  it.each(arten)('„%s“ steht als Satz im Buch, nicht als Kennung', (art) => {
    const text = buchText(
      { id: 1, zeitUtc: '', art, spalteId: 'flower-w10', feld: 'ecTarget', alt: 'flower-w9', neu: '1.4', ziel: 'grow', grund: null },
      () => 'EC',
      () => 'Blütewoche 9',
    )
    // Der Rückfall in buchText ist genau `return e.art`. „Plan angelegt" enthält
    // das Wort zu Recht — geprüft wird, dass ein eigener Satz entsteht.
    expect(text).not.toBe(art)
  })
})
