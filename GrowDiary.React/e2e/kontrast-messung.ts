/**
 * Die Farbmessung von `kontrast.spec.ts` — als Quelltext, der im Browser läuft.
 *
 * Eine eigene Datei, weil mehrere Prüfungen sie brauchen: der Kontrast über alle
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

/**
 * Der tatsächlich gemalte Kontrast jedes Textknotens — als Ausdruck für
 * `evaluate(`(${…})()`)`. Liefert die Funde unter `schwelle` (große Schrift: 3,0).
 *
 * Genutzt von `kontrast.spec.ts` und der Gerätematrix (`geraete/`).
 *
 * Der Knackpunkt sind halbtransparente Flächen: ein Abzeichen mit
 * `rgba(…, 0.10)` über einer Karte ist NICHT diese Farbe, sondern die
 * Mischung. Wer das ignoriert, misst Unsinn — eine erste Fassung dieser
 * Prüfung meldete deshalb lauter Fehlalarme auf Stellen, die in Wahrheit gut
 * lesbar sind.
 */
export function kontrastMessung(schwelle: number): string {
  return `() => {
  // Farben vom Browser umrechnen lassen, statt sie mit einem Regex zu zerlegen.
  // Grund: die App benutzt inzwischen auch oklch() — aus
  // "oklch(0.55 0.18 27.4 / 0.04)" liest ein Zahlen-Regex 0.55/0.18/27.4 und
  // haelt das fuer RGB. Das Ergebnis ist frei erfunden, in beide Richtungen:
  // eine erste Fassung dieser Pruefung meldete so einen Kontrast von 2,79 an
  // einer Stelle, die in Wahrheit bei 5,9 lag. Ueber eine 1x1-Leinwand malt der
  // Browser jede Farbschreibweise korrekt und mischt die Deckkraft gleich mit.
  ${KONTRAST_HELFER}
  const funde = []
  // ALLES im Koerper, nicht nur main/nav/header. Die frueheren drei Selektoren
  // waren schon eine Reparatur (die Navigation lag ausserhalb von <main>), aber
  // immer noch eine Aufzaehlung — und was nicht aufgezaehlt ist, wird nicht
  // gemessen. Skripte und Stildateien fallen unten ueber die Sichtbarkeit weg.
  for (const el of document.querySelectorAll('body *')) {
    // Der EIGENE Text des Elements, nicht der seiner Kinder.
    //
    // Vorher wurde jedes Element mit Kindern uebersprungen. Genau so ist der
    // Zaehler im aktiven Menuepunkt durchgerutscht: der Menuepunkt traegt
    // seinen Namen SELBST und daneben ein Kind mit der Zahl — also hat ihn die
    // Pruefung wegen des Kindes gar nicht erst angesehen.
    const eigen = [...el.childNodes]
      .filter((k) => k.nodeType === 3)
      .map((k) => k.textContent)
      .join('')
      .trim()
    if (!eigen) continue
    const s = getComputedStyle(el)
    if (s.visibility === 'hidden' || s.display === 'none') continue

    // GESPERRTE Bedienelemente sind ausgenommen.
    //
    // WCAG 1.4.3 nimmt inaktive Bedienelemente ausdruecklich aus: „Text, der
    // Teil eines inaktiven Bedienelements ist, hat keine Kontrastanforderung."
    // Genau darum geht es: die App zeigt gesperrte Knoepfe mit opacity 0.45,
    // und seit die geerbte Deckkraft mitgerechnet wird, meldete diese Pruefung
    // drei davon — „Eintragen" bei 1,76 auf /aushaerten, obwohl der Knopf dort
    // absichtlich nicht anklickbar ist. Ein Fehlalarm, der die ganze Pruefung
    // entwertet haette.
    const gesperrt = el.closest('[disabled], [aria-disabled="true"], .is-disabled')
    if (gesperrt) continue

    // GEERBTE Deckkraft — und zwar richtig gerechnet.
    //
    // Hier stand nur die Deckkraft des Elements SELBST. Eine Gruppe darueber
    // mit opacity 0.72 blieb damit unsichtbar, und ihre Wirkung auf den Text
    // ebenso: „Topf N" im leeren Topf kam im hellen Thema auf 3,97:1 und riss
    // AA, ohne dass diese Pruefung etwas meldete. Fuenfter blinder Fleck
    // dieser Datei.
    //
    // Der erste Anlauf mischte nur die SCHRIFT in ihren Untergrund und liess
    // den Untergrund stehen. Das ist zu hart: Gruppen-Deckkraft dimmt BEIDES.
    // Die Gruppe wird fertig gemalt und dann als Ganzes ueber das gelegt, was
    // ausserhalb von ihr liegt — also werden Schrift und Flaeche mit derselben
    // Deckung gegen denselben aeusseren Grund gemischt.
    // Laufende Einblendungen zaehlen NICHT.
    //
    // Die App blendet Karten beim Laden ein. Wird waehrenddessen gemessen,
    // steht dort eine Deckkraft, die eine Zehntelsekunde spaeter 1 ist —
    // gemeldet wurden dadurch „Laeuft" bei 1,76 und zwei Knoepfe, die in
    // Ruhe tadellos sind. Eine Pruefung mit Fehlalarmen wird binnen einer
    // Woche uebergangen; also wird nur bewertet, was steht.
    let gruppe = null
    let deckung = 1
    for (let k = el; k && k !== document.documentElement; k = k.parentElement) {
      const d = Number(getComputedStyle(k).opacity)
      if (d >= 1) continue
      const bewegt = typeof k.getAnimations === 'function'
        && k.getAnimations().some((a) => a.playState === 'running')
      if (bewegt) continue
      deckung *= d
      gruppe = k
    }
    if (deckung < 0.3) continue

    const [, , , ta = 1] = zahl(s.color)
    if (ta * deckung < 0.3) continue

    let grund = flaeche(el)
    let vorne = alsRgb(s.color, grund)

    if (gruppe && gruppe.parentElement) {
      const aussen = flaeche(gruppe.parentElement)
      const mische = (farbe) => [0, 1, 2].map((i) => deckung * farbe[i] + (1 - deckung) * aussen[i])
      vorne = mische(vorne)
      grund = mische(grund)
    }

    const l1 = lum(vorne), l2 = lum(grund)
    const k = (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05)
    // Grosse Schrift darf nach WCAG bei 3,0 bleiben — sie ist auch mit
    // weniger Kontrast noch gut zu lesen.
    const px = parseFloat(s.fontSize)
    const gross = px >= 24 || (px >= 18.66 && Number(s.fontWeight) >= 700)
    if (k < (gross ? 3 : ${schwelle})) funde.push((el.className || el.tagName) + ' — „' + eigen.slice(0, 40) + '" — Kontrast ' + k.toFixed(2))
  }
  return [...new Set(funde)]
}`
}
