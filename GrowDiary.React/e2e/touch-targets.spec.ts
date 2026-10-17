import { test, expect } from '@playwright/test'
import { einblendungenAbwarten, zuKleineZiele } from './oberflaeche-messung'

/**
 * Was man antippen kann, muss man auch treffen können.
 *
 * 44px ist die Größe, die Apple und Google unabhängig voneinander als Minimum
 * angeben. Grow OS läuft am Wandtablet und am Telefon im Gewächshaus, oft mit
 * feuchten Händen — hier ist das keine Formalie.
 *
 * Gemessen wird die *Trefferfläche*, nicht die Kastengröße — wie, steht in
 * `oberflaeche-messung.ts` (`zuKleineZiele`).
 */
const ROUTES = ['/', '/zelte', '/hydro', '/grows/1', '/sensoren', '/regeln', '/aufgaben', '/messung', '/messungen', '/wissen', '/home-assistant']
const MIN = 44

// Mit Touch, nicht nur schmal: die Vergrößerung hängt an `@media (pointer: coarse)`,
// und ein schmales Fenster mit Maus ist kein grober Zeiger. Ohne diese Zeile meldet
// der Test 32px-Knöpfe, die auf einem Telefon längst 44px sind.
test.use({ hasTouch: true, isMobile: true })

for (const route of ROUTES) {
  test(`Touch-Ziele auf ${route}`, async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 })
    await page.goto(route, { waitUntil: 'networkidle' })

    await einblendungenAbwarten(page)
    const small = await zuKleineZiele(page, MIN)

    expect(small, `Zu kleine Bedienelemente auf ${route}:\n${small.join('\n')}`).toEqual([])
  })
}
