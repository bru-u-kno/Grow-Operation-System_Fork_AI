import { test, expect, type Page } from '@playwright/test'
import { backendAntwortet, darfUeberspringen } from './pflicht'
import { gibSchloss, nimmSchloss } from './schloss'

/**
 * Zuluft › Rechenweg: Das zweite Glied heißt wie der Raum — nicht „Keller" (forkai.241).
 *
 * <b>Der Anlass.</b> Bis forkai.240 stand im Rechenweg der Zuluft fest „Keller", weil die Zuluft nur den Kellerfühler
 * kannte. Seit forkai.241 ist der Raum eine eigene Einheit mit eigenem Namen (<c>/api/steuerung/raum</c>), und der
 * Rechenweg soll ihn nennen. Belegt war das bisher nur einmal von Hand an der Demo-App („angesehen"); ein Rückfall auf
 * die feste Aufschrift hätte keine Prüfung gerötet.
 *
 * <b>Was hier geprüft wird.</b> Den Namen setzt der Fall über die echte Schnittstelle, die Seite lädt ihn aus der
 * echten Antwort der Zuluft (<c>raumName</c>), und im geöffneten Rechenweg steht er als Beschriftung des Glieds — nicht
 * „Keller". Mit leerem Namen gilt die Vorgabe („Aufstellraum"), ebenfalls nicht „Keller". Gerendert wird die GEBAUTE App,
 * nichts wird abgefangen.
 *
 * <b>Schreibt</b> den einen Raumnamen der geteilten App (Schloss, wie <c>raum-reiter.spec.ts</c>) und stellt ihn zurück.
 */
test.describe.configure({ mode: 'serial' })

test.beforeEach(async ({ request }) => {
  await nimmSchloss()
  darfUeberspringen(!(await backendAntwortet(request)), 'Kein Backend unter GROW_OS_URL — der Rechenweg braucht die laufende App.')
})

test.afterEach(() => { gibSchloss() })

async function rechenwegOeffnen(page: Page) {
  const antwort = await page.goto('/steuerung/zuluft', { waitUntil: 'networkidle' })
  darfUeberspringen(antwort == null || antwort.status() >= 400, '/steuerung/zuluft antwortet nicht — läuft die App unter GROW_OS_URL?')
  await page.getByRole('button', { name: /Rechenweg ansehen/ }).click()
  // Das Blatt „Rechenweg": vier Messwerte, daraus eine Stufe.
  const blatt = page.locator('.zl-kette')
  await expect(blatt).toBeVisible()
  return blatt
}

test('der Rechenweg nennt den Raum beim Namen — und nicht „Keller"', async ({ page, request }) => {
  const vorher = (await (await request.get('/api/steuerung/raum')).json()) as { name: string }
  try {
    const gesetzt = await request.put('/api/steuerung/raum', { data: { name: 'Werkstattraum' } })
    expect(gesetzt.status(), 'Raumname setzen').toBe(200)
    // Die Antwort der Zuluft trägt den Namen — die Seite zeigt, was das Backend liefert.
    expect((await (await request.get('/api/steuerung/zuluft')).json()).live.raumName).toBe('Werkstattraum')

    const kette = await rechenwegOeffnen(page)
    const beschriftungen = await kette.locator('.zl-glied .l').allTextContents()
    // Mengenwächter: ohne die Glieder prüfte der Fall nichts.
    expect(beschriftungen.length, `Glieder im Rechenweg: ${beschriftungen.join(' | ')}`).toBeGreaterThanOrEqual(5)
    expect(beschriftungen[1]).toBe('Werkstattraum')
    expect(beschriftungen).not.toContain('Keller')
    await expect(kette).not.toContainText('Keller')

    // Das zweite Mal unter erschwerten Umständen: Blatt zu, anderer Name, Seite NICHT neu laden.
    await page.keyboard.press('Escape')
    await request.put('/api/steuerung/raum', { data: { name: 'Dachboden' } })
    await page.reload({ waitUntil: 'networkidle' })
    await page.getByRole('button', { name: /Rechenweg ansehen/ }).click()
    await expect(page.locator('.zl-kette .zl-glied .l').nth(1)).toHaveText('Dachboden')
  } finally {
    // Zurück auf den alten Stand (leer = Vorgabe).
    const war = vorher.name === 'Aufstellraum' ? null : vorher.name
    await request.put('/api/steuerung/raum', { data: { name: war } })
  }
})

test('ohne eigenen Namen gilt die Vorgabe „Aufstellraum" — auch dann nicht „Keller"', async ({ page, request }) => {
  const vorher = (await (await request.get('/api/steuerung/raum')).json()) as { name: string }
  try {
    await request.put('/api/steuerung/raum', { data: { name: null } })
    const kette = await rechenwegOeffnen(page)
    const beschriftungen = await kette.locator('.zl-glied .l').allTextContents()
    expect(beschriftungen.length, `Glieder im Rechenweg: ${beschriftungen.join(' | ')}`).toBeGreaterThanOrEqual(5)
    expect(beschriftungen[1]).toBe('Aufstellraum')
    await expect(kette).not.toContainText('Keller')
  } finally {
    const war = vorher.name === 'Aufstellraum' ? null : vorher.name
    await request.put('/api/steuerung/raum', { data: { name: war } })
  }
})
