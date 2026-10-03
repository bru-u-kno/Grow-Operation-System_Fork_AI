import { test, expect } from '@playwright/test'
import { ALLE_SEITEN } from './seiten'
import { KONTRAST_HELFER } from './kontrast-messung'

// Schrift, die man nicht lesen kann.
//
// Zweimal an einem Tag ist derselbe Fehler durchgekommen: eine Farbe fest im
// Stylesheet verdrahtet, weil sie in der dunklen Ansicht gut aussah — und in
// der hellen stand dann dunkler Text auf dunklem Grund (gemessener Kontrast
// 1,0 bis 1,2, also praktisch unsichtbar). Aufgefallen ist es beide Male nur
// durch Zufall, weil ein Screenshot in der anderen Ansicht entstand.
//
// Deshalb misst dieser Test beide Ansichten. Er prüft NICHT die
// Gestaltungsvorgaben — die Schwelle liegt bewusst tief bei 3,0. Alles
// darunter ist kein Geschmacksfall mehr, sondern unleserlich.
/**
 * Die Seiten kommen aus `e2e/seiten.ts` — also aus dem Menue der App.
 *
 * <b>Hier stand eine abgetippte Liste, und `/ac-test` fehlte darin.</b> Genau
 * dort lag ein Knopf mit Kontrast 3,60 im hellen Thema (`--accent` als
 * Textfarbe statt `--accent-text`, wovor die Randnotiz am Token sogar warnt).
 * Vier Pruefungen pflegten vier verschiedene Listen; diese war die vierte.
 */
const ROUTEN = ALLE_SEITEN

// Die Liste muss etwas hergeben. Ohne diese Zeile laeuft die Schleife
// darunter bei einer leeren Liste null Mal durch — und der Testlauf meldet
// gruen, obwohl er nichts geprueft hat. Genau diese Falle hat in diesem
// Projekt schon zugeschlagen.
if (ROUTEN.length < 20) throw new Error(`Diese Pruefung sieht nur ${ROUTEN.length} Seiten — sie wuerde nichts messen.`)

/**
 * Die Schwelle — seit 2026-08-17 der WCAG-AA-Wert statt der alten 3,0.
 *
 * Die 3,0 waren als Notbremse gedacht: „darunter ist es nicht mehr lesbar".
 * Damit blieb aber ein ganzes Feld unbeachtet, in dem Text zwar erkennbar,
 * aber mühsam ist — und genau dort lagen die Funde eines Durchgangs durch die
 * laufende App: jeder Link in einer Tabelle bei 3,39, die Zelt-Beschriftungen
 * bei 4,26, der Notfall-Knopf bei 3,82. Nach deren Korrektur misst die App auf
 * allen Seiten in beiden Ansichten sauber, also ist der strengere Wert
 * haltbar.
 */
const SCHWELLE = 4.5

/**
 * Der tatsächlich gemalte Kontrast eines Textknotens.
 *
 * Der Knackpunkt sind halbtransparente Flächen: ein Abzeichen mit
 * `rgba(…, 0.10)` über einer Karte ist NICHT diese Farbe, sondern die
 * Mischung. Wer das ignoriert, misst Unsinn — eine erste Fassung dieser
 * Prüfung meldete deshalb lauter Fehlalarme auf Stellen, die in Wahrheit gut
 * lesbar sind.
 */
const MESSUNG = `() => {
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
    if (k < (gross ? 3 : ${SCHWELLE})) funde.push((el.className || el.tagName) + ' — „' + eigen.slice(0, 40) + '" — Kontrast ' + k.toFixed(2))
  }
  return [...new Set(funde)]
}`

/**
 * Bausteine, die eine eigene Tonfarbe tragen.
 *
 * Der Routen-Durchgang unten sieht nur, was ohne Backend rendert — und leere
 * Seiten haben keine Abzeichen. Genau dort saß der Fehler: mit wieder
 * eingebautem Fehler lief die Prüfung grün durch. Deshalb werden die
 * betroffenen Klassen hier zusätzlich in eine echte Seite eingesetzt und in
 * ihrer normalen Umgebung gemessen (Karte innerhalb `.v1-page`).
 *
 * Kommt eine neue tonführende Klasse dazu, gehört sie hier hinein.
 */
const BAUSTEINE = `
  <div class="v1-page"><div class="card"><div class="card-header">
    <span class="card-title">Titel</span><span class="text-muted">Zusatz</span>
  </div><div style="padding:14px 18px">
    <span class="badge badge-ok">Info</span>
    <span class="badge badge-warn">Warnung</span>
    <span class="badge badge-danger">Kritisch</span>
    <span class="badge badge-neutral">Neutral</span>
    <span class="dz-pump-state">bereit</span>
    <span class="dz-pump-state is-blocked">gesperrt</span>
    <div class="tn-group"><div class="tn-group-label">Klima</div>
      <div class="tn-row"><span>Lufttemperatur</span><strong class="is-faint">nicht gemappt</strong></div></div>
    <span class="cu-alter">Abgelesen vor 2 h</span>
    <div class="cu-ampel is-warn"><span class="cu-alter">Abgelesen gerade eben</span>
      <strong class="cu-befund">63,5 % — über dem Fenster</strong><p>Länger lüften.</p>
      <small>Quelle: budtrainer.com</small></div>
    <span class="kb-tag is-accent">Akzent</span>
    <span class="kb-tag is-warn">Warnung</span>
    <span class="kb-tag is-info">Hinweis</span>
    <span class="kb-tag is-muted">Leise</span>
    <span class="kb-tag is-danger">Notfall</span>
    <div class="tl-title">Zeilentitel</div>
    <div class="tl-sub">Zeilenzusatz</div>
    <div class="empty-hint">Nichts vorhanden</div>
    <div class="section-label">Abschnitt</div>
    <a class="btn">Knopf</a>
    <a class="btn btn-primary">Hauptknopf</a>
  </div></div></div>`

for (const schema of ['light', 'dark'] as const) {
  test.describe(`Lesbarkeit in der ${schema === 'light' ? 'hellen' : 'dunklen'} Ansicht`, () => {
    test.use({ colorScheme: schema })

    test('die tonfuehrenden Bausteine sind lesbar', async ({ page }) => {
      await page.goto('/')
      await page.waitForLoadState('networkidle')
      await page.evaluate((markup) => {
        const halter = document.createElement('div')
        halter.innerHTML = markup
        document.querySelector('main')!.appendChild(halter)
      }, BAUSTEINE)

      const funde: string[] = await page.evaluate(`(${MESSUNG})()`)

      expect(funde, `Unlesbare Bausteine (${schema}):\n` + funde.join('\n')).toEqual([])
    })

    for (const route of ROUTEN) {
      test(`${route} hat lesbare Schrift`, async ({ page }) => {
        await page.goto(route)
        await page.waitForLoadState('networkidle')
        await page.waitForTimeout(400)

        // Als Ausdruck aufrufen, nicht nur reichen: `page.evaluate` mit einem
        // String wertet ihn als Ausdruck aus — ohne die Klammern kaeme die
        // Funktion selbst zurueck und nie ihr Ergebnis.
        const funde: string[] = await page.evaluate(`(${MESSUNG})()`)

        // Mengenwaechter: hat die Messung ueberhaupt Text gesehen? Ohne ihn ist
        // eine leere Seite — oder ein Selektor, der nichts trifft — genauso
        // gruen wie eine tadellos lesbare. Der Kontrast-Test war in diesem
        // Projekt schon DREIMAL blind; einmal, weil er nur `main *` mass.
        const gemessen: number = await page.evaluate(
          `[...document.querySelectorAll('body *')].filter(e =>`
          + ` e.children.length === 0 && (e.textContent || '').trim().length > 2).length`)
        expect(gemessen, `Auf ${route} (${schema}) wurde kein Text gemessen — dann sagt `
          + 'ein leeres Ergebnis nichts ueber die Lesbarkeit.')
          .toBeGreaterThan(10)

        expect(funde, `Unlesbare Schrift auf ${route} (${schema}):\n` + funde.join('\n')).toEqual([])
      })
    }
  })
}

/**
 * Dasselbe noch einmal am Handy — denn genau dort ist der Fehler ein drittes
 * Mal durchgekommen.
 *
 * Der Durchgang oben laeuft in Desktop-Breite. Die Hauptnavigation am Handy
 * (`.v1-mobile-nav`) existiert erst unterhalb des Umbruchs und war damit fuer
 * die Pruefung schlicht unsichtbar: ihr Balken trug ein festverdrahtetes
 * Dunkelgruen, waehrend die Beschriftungen dem Thema folgten — in der hellen
 * Ansicht gemessene 1,15, also unlesbar. Auf der Flaeche, die im
 * Home-Assistant-Handy als Erstes erscheint.
 */
for (const schema of ['light', 'dark'] as const) {
  test.describe(`Lesbarkeit am Handy in der ${schema === 'light' ? 'hellen' : 'dunklen'} Ansicht`, () => {
    test.use({ colorScheme: schema, viewport: { width: 390, height: 844 } })

    for (const route of ['/', '/aufgaben', '/messungen']) {
      test(`${route} ist am Handy lesbar`, async ({ page }) => {
        await page.goto(route)
        await page.waitForLoadState('networkidle')
        await page.waitForTimeout(400)

        const funde: string[] = await page.evaluate(`(${MESSUNG})()`)

        expect(funde, `Unlesbare Schrift am Handy auf ${route} (${schema}):\n` + funde.join('\n')).toEqual([])
      })
    }
  })
}
