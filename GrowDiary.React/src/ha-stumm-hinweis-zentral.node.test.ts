import { readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

/**
 * Der Hinweis „Home Assistant antwortet nicht" steht an EINER Stelle (`HaStummHinweis.tsx`).
 *
 * Anlass (A-017, Lücke 8): sechs Seiten trugen fünf Fassungen desselben Hinweises, alle mit dem Satz „Die Regelung
 * läuft weiter" — auch dort, wo noch nichts eingerichtet war. Wer einen Hinweis neu baut, statt die Komponente zu
 * nehmen, bringt das auseinanderlaufende Wortlaut-Problem zurück (Gedächtnis „Komponenten zentral").
 *
 * Gesucht wird in den Dateien der Steuerung ohne Kommentare. Selbsttest: die Komponente wird wirklich eingebunden.
 */
const SRC = fileURLToPath(new URL('.', import.meta.url))
const STEUERUNG = join(SRC, 'features/steuerung')

const ohneKommentare = (text: string) => text.replace(/\/\*[\s\S]*?\*\//g, '').split('\n').map((z) => z.replace(/(^|\s)\/\/.*$/, '$1')).join('\n')

const dateien = [
  ...readdirSync(STEUERUNG).filter((n) => n.endsWith('.tsx')).map((n) => join('features/steuerung', n)),
  'pages/SteuerungPage.tsx',
]

describe('Hinweis „Home Assistant antwortet nicht" ist zentral', () => {
  it('Selbsttest: die Grundmenge ist da', () => {
    expect(dateien.length).toBeGreaterThanOrEqual(10)
  })

  it('keine Seite baut den Hinweis selbst', () => {
    const eigene = dateien
      .filter((d) => !d.endsWith('HaStummHinweis.tsx') && !d.endsWith('Bereitstellen.tsx'))
      .filter((d) => /title="Home Assistant (antwortet nicht|ist noch nicht verbunden)"/.test(ohneKommentare(readFileSync(join(SRC, d), 'utf8'))))
    expect(eigene, 'Diese Dateien bauen den Hinweis selbst — HaStummHinweis nehmen.').toEqual([])
  })

  it('die sechs Stellen binden die Komponente ein', () => {
    const mit = dateien.filter((d) => ohneKommentare(readFileSync(join(SRC, d), 'utf8')).includes('<HaStummHinweis'))
    expect(mit.sort()).toEqual([
      'features/steuerung/ChillerDetail.tsx', 'features/steuerung/EntfeuchtungSeite.tsx', 'features/steuerung/LichtDetail.tsx',
      'features/steuerung/ZuluftDetail.tsx', 'pages/SteuerungPage.tsx',
    ])
  })
})
