import { test, expect } from '@playwright/test'
import { ALLE_SEITEN } from './seiten'
import { kontrastMessung } from './kontrast-messung'

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

/** Die Messung selbst steht in `kontrast-messung.ts` — die Gerätematrix nutzt sie mit. */
const MESSUNG = kontrastMessung(SCHWELLE)

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
