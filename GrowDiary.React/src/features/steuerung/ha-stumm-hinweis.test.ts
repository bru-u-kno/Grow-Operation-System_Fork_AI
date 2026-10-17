import { describe, expect, it } from 'vitest'
import { haStummText } from './ha-stumm-text'

describe('haStummText (A-017, Lücke 8)', () => {
  it('leere Installation: kein Wort über eine laufende Regelung', () => {
    const t = haStummText(false)
    expect(t.titel).toBe('Home Assistant ist noch nicht verbunden')
    expect(t.text).toContain('Eingerichtet ist noch nichts')
    expect(`${t.titel} ${t.text}`).not.toMatch(/läuft|letzte bekannte/)
    // Auch für die Lampe: ohne Einrichtung gibt es keinen Zeitplan, der weiterläuft.
    expect(haStummText(false, 'zeitplan')).toEqual(t)
  })

  it('eingerichtet: letzter Stand, und was weiterläuft', () => {
    expect(haStummText(true)).toEqual({
      titel: 'Home Assistant antwortet nicht',
      text: 'Die Werte sind der letzte bekannte Stand. Die Regelung läuft in Home Assistant unabhängig weiter.',
    })
    expect(haStummText(true, 'zeitplan').text).toBe('Die Werte sind der letzte bekannte Stand. Der Zeitplan läuft im Controller weiter.')
  })
})
