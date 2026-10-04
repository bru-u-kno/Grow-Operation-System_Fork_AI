import { describe, expect, it } from 'vitest'
import jsQR from 'jsqr'
import { QUIET_ZONE, buildMatrix, toPathData, totalSize } from './qr'

/**
 * Ein QR-Code lässt sich nicht ansehen und für richtig befinden — falsch sieht
 * genauso aus wie richtig. Deshalb wird hier wirklich gescannt: der erzeugte
 * Code wird zu Pixeln gerechnet und von einem fremden Decoder gelesen. Kommt
 * derselbe Text heraus, funktioniert er auch auf einem Handy.
 */

const SCALE = 6

/** Malt die Matrix als Schwarz-Weiss-Bild, so wie ein Bildschirm es zeigen würde. */
function rasterize(text: string): { data: Uint8ClampedArray; width: number } {
  const matrix = buildMatrix(text)
  const modules = totalSize(matrix)
  const width = modules * SCALE
  const data = new Uint8ClampedArray(width * width * 4).fill(255)

  for (let row = 0; row < matrix.size; row++) {
    for (let column = 0; column < matrix.size; column++) {
      if (!matrix.isDark(row, column)) continue
      const x0 = (column + QUIET_ZONE) * SCALE
      const y0 = (row + QUIET_ZONE) * SCALE
      for (let y = y0; y < y0 + SCALE; y++) {
        for (let x = x0; x < x0 + SCALE; x++) {
          const offset = (y * width + x) * 4
          data[offset] = 0
          data[offset + 1] = 0
          data[offset + 2] = 0
        }
      }
    }
  }

  return { data, width }
}

function scan(text: string): string | null {
  const { data, width } = rasterize(text)
  return jsQR(data, width, width)?.data ?? null
}

describe('QR-Code', () => {
  it('lässt sich von einem fremden Decoder lesen', () => {
    const url = 'https://homeassistant.local:8123/app/local_grow_os'

    expect(scan(url)).toBe(url)
  })

  it('trägt auch eine lange Adresse mit Repo-Hash', () => {
    // So sieht der Slug aus, wenn das Add-on über ein Repository installiert
    // wurde — deutlich laenger als local_grow_os.
    const url = 'https://abcdefghijklmnop.ui.nabu.casa/app/a0d7b954_grow_os'

    expect(scan(url)).toBe(url)
  })

  it('kommt mit einer nackten IP zurecht', () => {
    const url = 'http://192.168.178.68:8123/app/local_grow_os'

    expect(scan(url)).toBe(url)
  })

  it('legt eine Ruhezone um den Code', () => {
    // Ohne den weissen Rand findet kein Scanner die Kanten. Vier Module sind
    // das Minimum der Spezifikation.
    const matrix = buildMatrix('https://example.local/app/local_grow_os')

    expect(totalSize(matrix)).toBe(matrix.size + 8)
    expect(QUIET_ZONE).toBe(4)
  })

  it('zeichnet jedes dunkle Modul genau einmal', () => {
    const matrix = buildMatrix('https://example.local/app/local_grow_os')
    let dunkel = 0
    for (let row = 0; row < matrix.size; row++) {
      for (let column = 0; column < matrix.size; column++) {
        if (matrix.isDark(row, column)) dunkel++
      }
    }

    expect(toPathData(matrix).match(/M/g)?.length).toBe(dunkel)
  })
})
