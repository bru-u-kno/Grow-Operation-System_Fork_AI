import { test, expect, type Page } from '@playwright/test'
import { backendAntwortet, darfUeberspringen } from './pflicht'
import { nimmSchloss, gibSchloss } from './schloss'

/* Schreibt in den Plan von Grow 1 — dasselbe Schloss wie die übrigen Rundwege
   an diesem Grow, damit kein paralleler Fall dazwischen schreibt. */
test.beforeEach(async () => { await nimmSchloss() })
test.afterEach(() => { gibSchloss() })

type Spalte = { id: string; label: string; verlaengert?: boolean; felder: Array<{ feld: string; wert: number | null; plan: number | null }> }

const zahl = (wert: number) => wert.toLocaleString('de-DE', { maximumFractionDigits: 2 })

async function speichern(page: Page) {
  await page.locator('[data-audit="plan-speichern"]').click()
  await page.locator('[data-audit="plan-speichern-bestaetigen"]').click()
  await expect(page.locator('.v1-alert.tone-ok'), 'Nach dem Speichern fehlt die Bestätigung.').toBeVisible({ timeout: 15_000 })
}

/**
 * Angehängte Wochen im Plan: sichtbar markiert und bearbeitbar wie jede Woche.
 *
 * <b>Der Anlass (02.10.2026).</b> Läuft eine Phase länger als das Programm,
 * bekommt der Plan des Grows eigene Wochen (Entscheidung des Nutzers: „erstreckt
 * sich die Blüte über zehn Wochen, zeigt das Schema auch zehn Wochen"). Der
 * Demobestand hat sie für die White Widow — Bewurzelung 2 und Vegiwoche 5
 * (<c>DemobestandStimmigTests.Ein_Lauf_dauert_laenger_als_sein_Programm</c>).
 *
 * <b>Der Rundweg</b> laut CLAUDE.md: ausfüllen, speichern, neu laden, Wert
 * wiederfinden — an der ersten UND an der letzten verlängerten Woche. Die zweite
 * Woche wird ohne Neuladen über das Wochen-Blatt geöffnet (erschwerter zweiter
 * Durchgang). Danach „zurück": der Startwert einer angehängten Woche ist der
 * ihrer Programmwoche — vorher hätte „zurück" das Feld geleert.
 */
test('verlängerte Wochen: markiert, ändern, speichern, neu laden — erste und letzte', async ({ page, request }) => {
  darfUeberspringen(!(await backendAntwortet(request)), 'Kein Backend — ohne Plan gibt es keine Woche zu bearbeiten.')
  const ziel = await (await request.get('/api/zielwerte')).json() as { growId: number | null; eigenerPlan?: boolean }
  darfUeberspringen(ziel.growId == null || !ziel.eigenerPlan, 'Kein laufender Grow mit eigenem Plan im Bestand.')

  const werte = await (await request.get(`/api/wochenplan/werte/${ziel.growId}`)).json() as { spalten: Spalte[] }
  const verlaengert = werte.spalten.filter((s) => s.verlaengert)
  expect(verlaengert.length, 'Der Demobestand sollte mindestens zwei verlängerte Wochen haben (erste und letzte).').toBeGreaterThanOrEqual(2)

  const erste = verlaengert[0]
  const letzte = verlaengert[verlaengert.length - 1]

  // Wochen-Blatt: die verlängerten Wochen stehen als Wochen da, markiert.
  await page.goto('/plan', { waitUntil: 'networkidle' })
  await page.locator('[data-audit="wochen-zeile-oeffnen"]').click()
  const blatt = page.locator('[data-audit="wochen-blatt"]')
  await expect(blatt.getByRole('button', { name: new RegExp(`${erste.label}.*verlängert`) })).toBeVisible()
  await expect(blatt.locator('[data-audit="wochen-blatt-verlaengert"]')).toHaveCount(verlaengert.length)
  await page.keyboard.press('Escape')

  try {
    for (const [durchgang, woche] of [[1, erste], [2, letzte]] as const) {
      if (durchgang === 1) {
        await page.goto(`/plan?woche=${woche.id}`, { waitUntil: 'networkidle' })
      } else {
        // Ohne Neuladen: über das Wochen-Blatt zur letzten verlängerten Woche.
        await page.locator('[data-audit="wochen-zeile-oeffnen"]').click()
        await blatt.getByRole('button', { name: new RegExp(woche.label) }).click()
      }

      const titel = page.locator('.wp-blatt-titel')
      await expect(titel).toContainText(woche.label)
      await expect(titel, `${woche.label}: die Markierung „verlängert" fehlt.`).toContainText('verlängert')
      await expect(page.locator('[data-audit="plan-verlaengert"]')).toBeVisible()

      const feld = woche.felder.find((f) => f.feld === 'ecTarget')!
      const neu = Math.round(((feld.wert ?? 1) + 0.1 * durchgang) * 100) / 100
      const eingabe = page.locator('[data-audit="plan-ecTarget"]')
      await eingabe.fill(zahl(neu))
      await speichern(page)

      // Nicht der Antwort glauben — neu laden und nachsehen.
      await page.goto(`/plan?woche=${woche.id}`, { waitUntil: 'networkidle' })
      await expect(page.locator('.wp-blatt-titel')).toContainText(woche.label)
      await expect(eingabe, `Durchgang ${durchgang}: ${woche.label} zeigt nach dem Neuladen nicht ${zahl(neu)}.`).toHaveValue(zahl(neu))

      // „zurück" auf den Startwert — bei einer angehängten Woche der ihrer Programmwoche.
      await page.locator('.wp-zelle.ist-eigen .wp-plan-zurueck').first().click()
      await speichern(page)
      await page.goto(`/plan?woche=${woche.id}`, { waitUntil: 'networkidle' })
      expect(feld.plan, `${woche.label} hat keinen Startwert — „zurück" hätte das Feld geleert.`).not.toBeNull()
      await expect(eingabe).toHaveValue(zahl(feld.plan!))
    }

    // Das Änderungsbuch nennt die angehängten Wochen mit ihrer Herkunft.
    await expect(page.locator('[data-audit="plan-buch"]')).toContainText('Woche angehängt · Werte aus')
  } finally {
    // Aufräumen auch nach einem Fehlschlag: alle Wochen zurück auf ihren Startwert.
    for (const woche of [erste, letzte]) {
      await request.post(`/api/wochenplan/werte/${ziel.growId}`, {
        data: { aenderungen: [{ spalteId: woche.id, feld: 'ecTarget', wert: null }] },
      })
    }
  }
})

/**
 * Wochennamen am Telefon: ein Name, eine Zeile — und überall dasselbe Wort.
 *
 * <b>Der Anlass (02.10.2026).</b> Die angehängte Anzucht-Woche hieß „Anzuchtwoche 2"
 * neben der Spalte „Bewurzelung" — zwei Namen für dieselbe Phase. Jetzt heißt sie
 * „Bewurzelung 2". Im Wochen-Blatt stand bei 360 px die Wochennummer allein in der
 * zweiten Zeile („Blütewoche / 3" bis „8"; beim Zwischenstand „Bewurzelungswoche 2"
 * auch dort). Gemessen wird der Text, nicht der Kasten (`Range.getClientRects()`):
 * zerfällt ein Wochenname auf zwei Zeilenhöhen, ragt er über den Rand?
 */
test('Wochen-Blatt: Wochennamen brechen am Telefon nicht um, keine „Anzuchtwoche"', async ({ page, request }) => {
  darfUeberspringen(!(await backendAntwortet(request)), 'Kein Backend — ohne Plan gibt es kein Wochen-Blatt.')
  const ziel = await (await request.get('/api/zielwerte')).json() as { growId: number | null; eigenerPlan?: boolean }
  darfUeberspringen(ziel.growId == null || !ziel.eigenerPlan, 'Kein laufender Grow mit eigenem Plan im Bestand.')

  const werte = await (await request.get(`/api/wochenplan/werte/${ziel.growId}`)).json() as { spalten: Spalte[] }
  const namen = werte.spalten.map((s) => s.label)
  expect(namen.filter((n) => n.startsWith('Anzuchtwoche')), 'Angehängte Anzucht-Wochen heißen „Bewurzelung N".').toEqual([])
  // Mengenwächter: ohne eine angehängte Bewurzelungs-Woche prüft der Name nichts.
  expect(namen.some((n) => /^Bewurzelung \d+$/.test(n)), 'Der Demobestand sollte eine angehängte Bewurzelungs-Woche haben.').toBe(true)

  for (const breite of [320, 360]) {
    await page.setViewportSize({ width: breite, height: 800 })
    await page.goto('/plan', { waitUntil: 'networkidle' })
    await page.locator('[data-audit="wochen-zeile-oeffnen"]').click()
    const blatt = page.locator('[data-audit="wochen-blatt"]')
    await expect(blatt).toBeVisible()
    const zeilen = await blatt.locator('.wp-zeile-l').count()
    expect(zeilen, 'Das Wochen-Blatt ist leer.').toBeGreaterThanOrEqual(10)
    const zerbrochen = await blatt.evaluate((el) => [...el.querySelectorAll('.wp-zeile-l')].filter((l) => {
      const text = l.firstChild
      if (!text) return false
      const r = document.createRange()
      r.selectNodeContents(text)
      return new Set([...r.getClientRects()].map((x) => Math.round(x.top))).size > 1
    }).map((l) => l.firstChild?.textContent ?? ''))
    expect(zerbrochen, `${breite} px: diese Wochennamen brechen um.`).toEqual([])
    // Die nowrap-Falle: ein Name, der nicht umbricht, kann an `.wp-liste`
    // (overflow: hidden) abgeschnitten werden — gemessen am Text, nicht am Kasten.
    const abgeschnitten = await blatt.evaluate((el) => {
      const rand = el.getBoundingClientRect().right
      return [...el.querySelectorAll('.wp-zeile-l, .wp-zeile-w')].filter((x) => {
        const r = document.createRange()
        r.selectNodeContents(x)
        return [...r.getClientRects()].some((q) => q.right > rand + 0.5)
      }).map((x) => x.textContent ?? '')
    })
    expect(abgeschnitten, `${breite} px: dieser Text ragt über den Rand des Wochen-Blatts.`).toEqual([])
    await page.keyboard.press('Escape')
  }

  // Derselbe Name steht in der Plan-Tabelle am Grow (PlanAuswertung). Dort brach
  // „Bewurzelungswoche 2" bei 390 px mitten im Wort („Bewurzelungswoc / he 2") —
  // Befund des Prüfers, 02.10.2026. Deshalb heißt die Woche „Bewurzelung 2".
  await page.setViewportSize({ width: 390, height: 800 })
  await page.goto(`/grows/${ziel.growId}`, { waitUntil: 'networkidle' })
  const tabelle = page.locator('[data-audit="plan-auswertung"] .pa-tabelle')
  await expect(tabelle).toBeVisible()
  // Nur der Name (der erste Textknoten): „verlängert" steht absichtlich darunter.
  const namen390 = await tabelle.evaluate((el) => [...el.querySelectorAll('tbody td:first-child')].map((td) => {
    const name = [...td.childNodes].find((k) => k.nodeType === Node.TEXT_NODE && (k.textContent ?? '').trim())
    if (!name) return { text: '', zeilen: 0 }
    const r = document.createRange()
    r.selectNodeContents(name)
    return { text: (name.textContent ?? '').trim(), zeilen: new Set([...r.getClientRects()].map((x) => Math.round(x.top))).size }
  }))
  expect(namen390.length, 'Die Plan-Tabelle am Grow ist leer.').toBeGreaterThanOrEqual(10)
  expect(namen390.some((n) => n.text.startsWith('Bewurzelung ')), 'Die angehängte Bewurzelungs-Woche fehlt in der Tabelle.').toBe(true)
  expect(namen390.filter((n) => n.zeilen > 1).map((n) => n.text), '390 px: diese Wochennamen brechen in der Plan-Tabelle um.').toEqual([])
})
