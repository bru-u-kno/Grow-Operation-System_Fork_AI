import { describe, expect, it } from 'vitest'
import { tiefenlinkAus } from './useHaTiefenlink'

// So schickt das HA-Frontend (20260826.7) die Route über das generische
// App-Panel /app/<slug>/<rest>: computeRouteTail trennt Slug und Rest.
const eigenschaften = (prefix: unknown, path: unknown) => ({
  type: 'home-assistant/properties',
  narrow: true,
  route: { prefix, path },
})

describe('tiefenlinkAus', () => {
  it('liest die Seite hinter dem Slug', () => {
    expect(tiefenlinkAus(eigenschaften('/app/d48160c2_grow_os_fork_ai', '/aufgaben'))).toEqual({
      pfad: '/aufgaben',
      praefix: '/app/d48160c2_grow_os_fork_ai',
    })
    expect(tiefenlinkAus(eigenschaften('/app/local_grow_os', '/zelte/3'))?.pfad).toBe('/zelte/3')
  })

  it('springt nicht, wenn der Link auf die Startseite zeigt', () => {
    // Auch nach dem Zurücksetzen der Adresse kommt genau das — kein Kreislauf.
    expect(tiefenlinkAus(eigenschaften('/app/local_grow_os', ''))).toBeNull()
    expect(tiefenlinkAus(eigenschaften('/app/local_grow_os', '/'))).toBeNull()
  })

  it('lässt nur schlichte App-Pfade durch', () => {
    for (const pfad of ['//boese.example/x', '/x?y=1', '/../config', 'aufgaben', '/a b', 'javascript:alert(1)']) {
      expect(tiefenlinkAus(eigenschaften('/app/local_grow_os', pfad))).toBeNull()
    }
    expect(tiefenlinkAus(eigenschaften('//boese.example', '/aufgaben'))).toBeNull()
  })

  it('übergeht fremde Nachrichten', () => {
    expect(tiefenlinkAus(null)).toBeNull()
    expect(tiefenlinkAus('home-assistant/properties')).toBeNull()
    expect(tiefenlinkAus({ type: 'home-assistant/navigate', route: { prefix: '/app/x', path: '/aufgaben' } })).toBeNull()
    expect(tiefenlinkAus({ type: 'home-assistant/properties' })).toBeNull()
    expect(tiefenlinkAus(eigenschaften(undefined, '/aufgaben'))).toBeNull()
  })
})
