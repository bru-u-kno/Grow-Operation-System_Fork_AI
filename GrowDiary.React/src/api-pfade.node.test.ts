import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'

/**
 * Jeder Aufruf der API geht über den Basispfad der App.
 *
 * <b>Der Anlass (19.10.2026).</b> „Jetzt sichern“ meldete in Home Assistant „Backup konnte nicht erstellt werden (404)“.
 * Der Knopf rief `fetch('/api/system/backup')` mit einem absoluten Pfad auf. Im Ingress liegt die App unter
 * `/api/hassio_ingress/<Schlüssel>/`; ein absoluter Pfad trifft dort die API von Home Assistant selbst, und die kennt die Route
 * nicht. Am Wurzelpfad (Entwicklung, Demo, Tests) fällt das nie auf. Gleiches galt für das Löschen eines Hydro-Aufbaus.
 *
 * Richtig ist `apiFetch(...)` oder `fetch(resolveUrl(...))` (`base.ts`). Die Prüfung liest den Quelltext und sucht
 * `fetch(` mit einem Pfad, der direkt mit `/api` beginnt — Kommentare und Tests zählen nicht.
 */
const WURZEL = join(__dirname)

function quelldateien(ordner: string): string[] {
  return readdirSync(ordner).flatMap((name) => {
    const pfad = join(ordner, name)
    if (statSync(pfad).isDirectory()) return quelldateien(pfad)
    return /\.(ts|tsx)$/.test(name) && !/\.test\.(ts|tsx)$/.test(name) ? [pfad] : []
  })
}

function ohneKommentare(text: string): string {
  return text.replace(/\/\*[\s\S]*?\*\//g, '').replace(/^\s*\/\/.*$/gm, '')
}

describe('API-Aufrufe', () => {
  it('rufen /api nie mit einem absoluten Pfad auf', () => {
    const dateien = quelldateien(WURZEL)
    expect(dateien.length).toBeGreaterThan(100)

    const funde = dateien.flatMap((datei) => ohneKommentare(readFileSync(datei, 'utf8'))
      .split('\n')
      .filter((zeile) => /\bfetch\(\s*[`'"]\/api/.test(zeile))
      .map((zeile) => `${datei.slice(WURZEL.length + 1)}: ${zeile.trim()}`))

    expect(funde).toEqual([])
  })

  it('beißt: ein absoluter Aufruf würde gefunden', () => {
    const beispiel = "const r = await fetch('/api/system/backup', { method: 'POST' })"
    expect(/\bfetch\(\s*[`'"]\/api/.test(beispiel)).toBe(true)
    expect(/\bfetch\(\s*[`'"]\/api/.test("await fetch(resolveUrl('/api/system/backup'))")).toBe(false)
  })
})
