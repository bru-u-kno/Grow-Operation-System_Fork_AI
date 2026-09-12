import { expect, test } from '@playwright/test'
import { navGroups } from '../src/navigation'

/**
 * Menü und Seite müssen dasselbe sagen.
 *
 * Dieser Fehler ist in diesem Projekt dreimal aufgetreten, immer nach dem
 * gleichen Muster: ein Menüpunkt wird umbenannt, weil der alte Name zu eng
 * war — und die Seite selbst behält ihre alte Überschrift. Das Menü sagte
 * „Wasser", die Seite „Leitungswasser". Das Menü sagte „Eigener KI-Berater",
 * der Reiter daneben „Mappe für eigene KI" mit dem ausdrücklichen Kommentar,
 * dass in Grow OS keine KI steckt. Beim Umsortieren der Gruppen wäre es
 * gerade wieder passiert: die Gruppe hieß plötzlich „Einrichtung", vierzehn
 * Seiten trugen weiter „Anlage" über ihrer Überschrift.
 *
 * Wer sich durch das Menü klickt, prüft an der Kopfzeile, ob er richtig ist.
 * Steht dort etwas anderes, als er angeklickt hat, sucht er weiter.
 *
 * Gemessen am gerenderten DOM statt am Quelltext: die Kopfzeile ist das, was
 * ankommt, und nur der Browser weiß, was am Ende dort steht.
 */
/**
 * Die Liste muss etwas hergeben.
 *
 * Ohne diese Zeile laeuft die Schleife darunter bei einer leeren Liste
 * null Mal durch — und der Testlauf meldet gruen, obwohl er nichts geprueft
 * hat. Genau diese Falle hat in diesem Projekt schon zugeschlagen: ein Test
 * suchte einen Routennamen im Quelltext, fand einen beliebigen Link und war
 * zufrieden, waehrend die Seite in keinem Menue stand.
 */
if (navGroups.length < 4) throw new Error(`Der Wegweiser-Test sieht nur ${navGroups.length} Menuegruppen — er wuerde nichts pruefen.`)

const ziele = navGroups.flatMap((gruppe) =>
  gruppe.items.map((eintrag) => ({ gruppe: gruppe.label, ...eintrag })))

/**
 * Die Live-Seite hat gar keine Kopfzeile: sie ist die Startseite, dort ist der
 * Weg hierher nicht die Frage — man ist ja schon da.
 */
const ohneGruppenPfad = new Set(['/'])

/**
 * Seiten, deren Überschrift bewusst ein Satz ist statt des Menüworts.
 *
 * Eine Überschrift darf sprechender sein als ihr Menüpunkt — sie darf ihm nur
 * nicht widersprechen. „Was jetzt zu tun ist" ist eine bessere Überschrift als
 * „Aufgaben" und schickt niemanden in die Irre; „Reservoir" über dem Menüpunkt
 * „Addback" tat genau das und wurde deshalb geändert.
 */
const eigeneUeberschrift = new Set(['/aufgaben', '/messung'])

for (const ziel of ziele) {
  test(`Kopfzeile von ${ziel.label} nennt die Gruppe „${ziel.gruppe}"`, async ({ page }) => {
    await page.goto(ziel.to, { waitUntil: 'networkidle' })

    if (ohneGruppenPfad.has(ziel.to)) return
    const eyebrow = page.locator('main .v1-eyebrow').first()
    await expect(eyebrow, `${ziel.to} hat keine Kopfzeile`).toBeVisible()
    const text = (await eyebrow.textContent())?.trim() ?? ''

    expect(text, `${ziel.to}: Kopfzeile „${text}" nennt nicht die Gruppe „${ziel.gruppe}"`)
      .toMatch(new RegExp(`^${ziel.gruppe}\\b`))
  })

  test(`Überschrift von ${ziel.label} heisst wie der Menüpunkt`, async ({ page }) => {
    await page.goto(ziel.to, { waitUntil: 'networkidle' })
    // Der Menüpunkt ist das Versprechen, die Überschrift die Einlösung. Ein
    // Teilstring genügt: „Hydro-Systeme" darf als Überschrift stehen, wenn
    // das Menü „Hydro-Systeme" sagt, und die Live-Seite darf ihren Zeltnamen
    // tragen.
    if (ohneGruppenPfad.has(ziel.to) || eigeneUeberschrift.has(ziel.to)) return
    const titel = (await page.locator('main h1').first().textContent())?.trim() ?? ''
    // Ein Klammerzusatz im Menue ist eine Unterscheidungshilfe, kein Titel:
    // „Home Assistant (Verbindung)" steht neben „Sensoren & Wartung (erfassen)",
    // damit man in der Liste das Richtige trifft. Auf der Seite selbst ist
    // ohnehin klar, wo man ist — die Klammer gehoert dort nicht in die
    // Ueberschrift. Deshalb vor dem Vergleich abschneiden.
    const erwartet = ziel.label.replace(/\s*\([^)]*\)\s*$/, '').split(' & ')[0].toLowerCase()
    expect(titel.toLowerCase(), `${ziel.to}: Überschrift „${titel}" passt nicht zum Menüpunkt „${ziel.label}"`)
      .toContain(erwartet)
  })
}
