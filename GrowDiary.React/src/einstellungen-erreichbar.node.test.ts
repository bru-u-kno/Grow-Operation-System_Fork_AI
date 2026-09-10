import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'

/**
 * Die Einstellungen müssen auch am Telefon einen Weg haben.
 *
 * <b>Der Anlass.</b> Mit der neuen Navigation (forkai.13) wanderte die
 * Hauptnavigation in die Icon-Leiste, die Seitenleiste blendet sich am Telefon
 * aus — und der Verweis „EINSTELLUNGEN" hängt genau dort unten drin. Damit war
 * die Seite auf dem Handy nur noch über die getippte Adresse erreichbar. Der
 * Nutzer wollte den Rücksprung des „⌂" umstellen und kam nicht hin.
 *
 * <b>Was hier geprüft wird.</b> Das „Mehr"-Menü — der einzige Ort, an dem am
 * Telefon alles versammelt ist — muss einen Verweis auf `/settings` tragen.
 * Ein Verweis irgendwo sonst in der Datei zählt nicht, sonst wäre der Test
 * schon durch die Seitenleiste grün.
 */
describe('Einstellungen erreichbar', () => {
  const shell = readFileSync(new URL('./AppShell.tsx', import.meta.url), 'utf8')

  const anfang = shell.indexOf('v1-mobile-more-panel')
  const ende = shell.indexOf('<main')
  const mehrMenue = shell.slice(anfang, ende)

  it('sieht das Mehr-Menü überhaupt', () => {
    // Sonst prüft der Test nichts und ist trotzdem grün.
    expect(anfang).toBeGreaterThan(0)
    expect(ende).toBeGreaterThan(anfang)
    expect(mehrMenue).toContain('Leiste anpassen')
  })

  it('führt aus dem Mehr-Menü zu den Einstellungen', () => {
    expect(mehrMenue).toContain('to="/settings"')
  })
})
