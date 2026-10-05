import { describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

/**
 * Die Regel, ohne die das Telefon-Layout zerbricht.
 *
 * Der Fehler, der dahinter steckt: ein Grid-Item hat `min-width: auto` — seine
 * Untergrenze ist damit die min-content-Breite seines Inhalts. Die Live-Seite
 * ist ein Grid mit einer einzigen Spalte, also zieht das breiteste Kind die
 * ganze Seite auf. Verursacher ist die Phasen-Zeitachse (ihre Abschnitte
 * tragen `min-width: fit-content` und teilen sich die Breite nach Tagesanteil,
 * gemessen bis 711 px). Dann standen alle vier Klima-Kacheln in einer Zeile
 * und VPD lag ausserhalb des Schirms — der Tester musste quer drehen.
 *
 * Warum ein Quelltext-Test und kein Browser-Test: die E2E-Suite laeuft ohne
 * Backend, auf `/` steht dort nur der Ladezustand — Kachelband, Kamera und
 * Zeitachse sind gar nicht im DOM. Eine Gegenprobe mit ausgebauter Regel blieb
 * dort gruen. Ein Test, der den Fehler nicht fangen kann, ist kein Waechter;
 * dieser haelt wenigstens die Regel selbst fest, bis die E2E-Suite gemockte
 * API-Antworten bekommt.
 */
describe('Schutzregeln des Live-Layouts', () => {
  // Die Datei direkt lesen: ein `?raw`-Import liefert unter vitest leeren Text.
  const css = readFileSync(fileURLToPath(new URL('./live-screen.css', import.meta.url)), 'utf8')

  it('erlaubt den Kindern der Live-Seite zu schrumpfen', () => {
    // Ohne diese Regel bestimmt das breiteste Kind die Breite der ganzen Seite.
    expect(css).toMatch(/\.ls\s*>\s*\*\s*\{[^}]*min-width:\s*0/)
  })

  it('laesst das Kamerabild nicht seine Naturbreite erzwingen', () => {
    const regel = css.match(/\.ls-cam-stage img \{[^}]*\}/)?.[0] ?? ''
    expect(regel, '.ls-cam-stage img nicht gefunden').not.toBe('')
    expect(regel).toContain('min-width: 0')
    expect(regel).toContain('max-width: 100%')
  })

  it('macht die Phasen-Zeitachse auf dem Telefon wischbar statt abgeschnitten', () => {
    // Die Balkenlaengen SIND die Dauer — umbrechen wuerde die Aussage zerstoeren.
    expect(css).toMatch(/\.ls-timeline-wrap\s*\{[^}]*overflow-x:\s*auto/)
  })

  it('stellt die Handlung im Quelltext vor die Kamera', () => {
    // Beim Umbruch — also auf jedem Telefon — ist die Quelltext-Reihenfolge
    // die Reihenfolge auf dem Schirm. Kritisches Risiko und „heute fällig“
    // gehoeren dann vor ein Bild, das man auch spaeter noch ansehen kann.
    const tsx = readFileSync(fileURLToPath(new URL('./LiveScreen.tsx', import.meta.url)), 'utf8')
    const handlung = tsx.indexOf('ls-lower-right')
    const kamera = tsx.indexOf('<CameraPanel')
    expect(handlung, 'ls-lower-right nicht gefunden').toBeGreaterThan(-1)
    expect(kamera, '<CameraPanel nicht gefunden').toBeGreaterThan(-1)
    expect(handlung).toBeLessThan(kamera)
  })

  it('holt die Kamera nur dann nach links, wenn beide Spalten nebeneinander passen', () => {
    // Ohne die Media-Abfrage wuerde `order` auch auf dem Telefon greifen und
    // das Bild wieder nach oben ziehen — der ganze Umbau waere umsonst.
    expect(css).toMatch(/@media\s*\(min-width:\s*9\d\dpx\)\s*\{\s*\.ls-cam\s*\{[^}]*order:\s*-1/)
  })

  it('macht die Kacheln in BEIDEN Ansichten anklickbar', () => {
    // Der Kachel-Klick kam in beta.38 — aber nur in `DashboardBands`, also in
    // der Ansicht mit eigener Anordnung. Wer keine gespeichert hat, sieht das
    // Band in LiveScreen, und dort wurde `onOpen` nie durchgereicht: die
    // Kacheln sahen gleich aus und taten nichts. Das ist der Standardfall, es
    // traf also die Mehrheit — gemeldet vom Tester, nicht von einem Test.
    const lies = (datei: string) =>
      readFileSync(fileURLToPath(new URL(datei, import.meta.url)), 'utf8')

    for (const datei of ['LiveScreen.tsx', 'DashboardBands.tsx']) {
      expect(lies(datei), `${datei} reicht onOpen nicht an MetricTile durch`).toMatch(/onOpen=\{/)
      // Fork AI (05.10.2026): beide zeigen das große Verlaufsdiagramm unter
      // der Zeile der Kachel — nicht mehr das kleine Liniendiagramm.
      expect(lies(datei), `${datei} zeigt keinen aufgeklappten Verlauf`).toMatch(/<KachelVerlauf\b/)
      expect(lies(datei), `${datei} zeigt noch das alte Liniendiagramm`).not.toMatch(/<SensorChart\b/)
    }
  })

  it('laesst die Kamerabuehne schrumpfen, wenn gar keine Kamera zugeordnet ist', () => {
    // 260 px grauer Klotz fuer den Satz „keine gemappt“ schoben auf dem
    // Telefon alles Darunterliegende aus dem Bild.
    expect(css).toMatch(/\.ls-cam-stage\.is-empty\s*\{[^}]*min-height:\s*0/)
  })
})
