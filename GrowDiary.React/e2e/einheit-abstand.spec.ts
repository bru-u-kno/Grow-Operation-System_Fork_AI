import { test, expect } from '@playwright/test'
import { darfUeberspringen } from './pflicht'

/**
 * Zwischen Zahl und Einheit steht ein Abstand.
 *
 * <b>Der Anlass.</b> forkai.242 setzte die Einheit der Zielwerte auf `display: inline-block`, damit sie beim Umbruch
 * zusammenbleibt. Das Leerzeichen steht aber IM Span (`<span class="u"> kPa</span>`) — am Anfang eines Inline-Blocks
 * wird es verschluckt, und die Live-Kacheln zeigten „1,4kPa“ und „400–450mV“. Die Gerätematrix und die Umbruchprüfung
 * messen Kästen und Zeilen, nicht den Abstand dazwischen; aufgefallen ist es erst beim Ansehen.
 *
 * Gemessen wird der Abstand zwischen dem letzten Zeichen der Zahl und der linken Kante der Einheit.
 */
test('Zielwerte der Live-Kacheln: Zahl und Einheit stehen nicht aneinander', async ({ page }) => {
  await page.goto('/', { waitUntil: 'networkidle' })
  await page.waitForTimeout(500)
  const luecken = await page.evaluate(() => {
    const raus: Array<{ text: string, luecke: number }> = []
    for (const u of Array.from(document.querySelectorAll<HTMLElement>('.gos-metric-zv .u'))) {
      const vor = u.previousSibling
      if (!vor || vor.nodeType !== Node.TEXT_NODE || !vor.textContent) continue
      const r = document.createRange()
      r.setStart(vor, vor.textContent.length - 1)
      r.setEnd(vor, vor.textContent.length)
      const zahl = r.getBoundingClientRect()
      raus.push({ text: `${vor.textContent}${u.textContent}`, luecke: Math.round((u.getBoundingClientRect().left - zahl.right) * 10) / 10 })
    }
    return raus
  })
  // Mengenwächter: ohne Backend gibt es keine Kacheln, und eine leere Liste wäre grün, ohne etwas zu messen.
  darfUeberspringen(luecken.length === 0, 'Live-Kacheln mit Zielwert brauchen das Backend (Demobestand)')
  // Mindestens zwei, nicht drei: nach Licht-aus (Demobestand: 20:00) zeigen die Luftkacheln Tag- und Nachtband statt eines
  // Zielwerts, übrig bleiben pH-Einheit-lose Kacheln, EC und ORP. Mit „drei“ wurde die Prüfung nach 20 Uhr rot.
  expect(luecken.length, 'Die Live-Seite zeigt keine Zielwerte mit Einheit').toBeGreaterThanOrEqual(2)
  expect(luecken.filter((x) => x.luecke < 1.5), 'Einheit klebt an der Zahl').toEqual([])
})

/**
 * Kein rohes „\u00a0“ im sichtbaren Text.
 *
 * <b>Der Anlass.</b> Auf der Plan-Seite stand „Mengen rechts für 208 + geschütztes Leerzeichen + L“ — ein Escape im JSX-Text (außerhalb von Klammern) wird
 * nicht aufgelöst, sondern als Zeichenfolge gezeigt. Aufgefallen beim Ansehen im dunklen Thema; keine Prüfung maß den Text.
 */
for (const pfad of ['/plan', '/wasserwechsel?growId=1&schritt=2', '/addback', '/kosten', '/messung', '/dosierung']) {
  test(`${pfad}: kein rohes \\u-Zeichen im Text`, async ({ page }) => {
    await page.goto(pfad, { waitUntil: 'networkidle' })
    await page.waitForTimeout(300)
    const text = await page.evaluate(() => document.body.innerText)
    // Mengenwächter: ohne Backend zeigt die Seite nur Lade- oder Fehlerzustand, und ein Text ohne Inhalt wäre grün.
    darfUeberspringen(text.length < 400, 'Seiteninhalt braucht das Backend (Demobestand)')
    expect(text.match(/\\u[0-9a-fA-F]{4}/g) ?? [], 'Escape-Folge im sichtbaren Text').toEqual([])
  })
}
