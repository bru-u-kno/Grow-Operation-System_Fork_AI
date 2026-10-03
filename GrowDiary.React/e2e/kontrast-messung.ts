/**
 * Die Farbmessung von `kontrast.spec.ts` — als Quelltext, der im Browser läuft.
 *
 * Eine eigene Datei, weil zwei Prüfungen sie brauchen: der Kontrast über alle
 * Seiten und die Lesbarkeit im Verlaufsdiagramm (`verlaufsdiagramm.spec.ts`).
 * Die zweite hatte zuerst eine eigene Fassung, und die rechnete die
 * Deckkraft der SCHRIFT nicht mit — eine Schrift mit 15 % Deckung galt dort als
 * lesbar (Befund des Prüfers, 03.10.2026).
 *
 * Stellt im Browser bereit: `alsRgb(farbe, unter)` (malt `farbe` über die
 * Grundfarbe `unter` und liefert das gemischte RGB — jede Schreibweise, jede
 * Deckkraft), `zahl(text)`, `flaeche(element)` (die tatsächlich gemalte Fläche
 * unter einem Element, alle Schichten gemischt) und `lum(rgb)`.
 */
export const KONTRAST_HELFER = `
  const c = document.createElement('canvas'); c.width = c.height = 1
  const ctx = c.getContext('2d', { willReadFrequently: true })
  const alsRgb = (farbe, unter) => {
    ctx.clearRect(0, 0, 1, 1)
    ctx.fillStyle = 'rgb(' + unter[0] + ',' + unter[1] + ',' + unter[2] + ')'
    ctx.fillRect(0, 0, 1, 1)
    ctx.fillStyle = farbe
    ctx.fillRect(0, 0, 1, 1)
    const d = ctx.getImageData(0, 0, 1, 1).data
    return [d[0], d[1], d[2]]
  }
  const zahl = (c) => (c.match(/[\\d.]+/g) || []).map(Number)
  const flaeche = (el) => {
    const schichten = []
    for (let e = el; e; e = e.parentElement) {
      const bg = getComputedStyle(e).backgroundColor
      if (bg && bg !== 'rgba(0, 0, 0, 0)' && bg !== 'transparent') schichten.unshift(bg)
    }
    // Ueberlappende Geschwister, die NICHT Vorfahren sind: der Fuellbalken der
    // laufenden Phase liegt als eigenes Element ueber der Beschriftung, gehoert
    // aber keinem gemeinsamen Ast an — ueber die Elternkette allein war er
    // unsichtbar, und genau dort lagen 3,93:1 im hellen Thema.
    //
    // Nur Elemente, die im Dokument SPAETER kommen (die also darueber malen),
    // und nur solche, die den Textkasten wirklich ueberdecken. Sonst gibt es
    // Fehlalarme bei jedem beliebigen Nachbarn.
    const r = el.getBoundingClientRect()
    if (r.width > 0 && r.height > 0) {
      for (const o of document.querySelectorAll('body *')) {
        if (o === el || o.contains(el) || el.contains(o)) continue
        if (!(el.compareDocumentPosition(o) & Node.DOCUMENT_POSITION_FOLLOWING)) continue
        const os = getComputedStyle(o)
        const bg = os.backgroundColor
        if (!bg || bg === 'rgba(0, 0, 0, 0)' || bg === 'transparent') continue
        if (os.visibility === 'hidden' || os.display === 'none') continue
        const q = o.getBoundingClientRect()
        if (q.left <= r.left && q.right >= r.right && q.top <= r.top && q.bottom >= r.bottom) {
          schichten.push(bg)
        }
      }
    }
    let unten = [255, 255, 255]
    for (const s of schichten) unten = alsRgb(s, unten)
    return unten
  }
  const lum = ([r, g, b]) => {
    const f = (v) => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4) }
    return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b)
  }
`
