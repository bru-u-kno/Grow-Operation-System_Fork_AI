import { test, expect } from '@playwright/test'

/**
 * Was man antippen kann, muss man auch treffen können.
 *
 * 44px ist die Größe, die Apple und Google unabhängig voneinander als Minimum
 * angeben. Grow OS läuft am Wandtablet und am Telefon im Gewächshaus, oft mit
 * feuchten Händen — hier ist das keine Formalie.
 *
 * Gemessen wird die *Trefferfläche*, nicht die Kastengröße: ein 24px-Kreis mit
 * einem ::after, das auf 44px aufpolstert, ist in Ordnung. Deshalb prüft der Test
 * über elementFromPoint, ob der Rand des 44px-Quadrats um die Mitte noch dasselbe
 * Bedienelement trifft.
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

    // Ein Element, das gerade noch einblendet, ist beim Messen zu klein. Unter
    // Last hat das den Live-Bildschirm gelegentlich rot gemeldet, allein aber nie.
    //
    // Nur auf *endliche* Animationen warten: der Live-Bildschirm hat einen
    // Dauerpuls, und dessen `finished` wird nie erfüllt — die erste Fassung
    // dieser Zeile lief deshalb zuverlässig in den Timeout und sah aus wie ein
    // Layoutfehler.
    await page.evaluate(() => Promise.all(
      document.getAnimations()
        .filter((animation) => {
          const timing = (animation.effect as KeyframeEffect | null)?.getComputedTiming()
          return timing != null && Number.isFinite(timing.iterations ?? Infinity) && Number.isFinite(timing.endTime ?? Infinity)
        })
        .map((animation) => animation.finished.catch(() => undefined)),
    ))

    const small = await page.evaluate((min) => {
      const out: string[] = []
      const controls = document.querySelectorAll('button, a[href], select, input:not([type=hidden]), [role=button]')
      for (const element of Array.from(controls)) {
        if (!(element instanceof HTMLElement)) continue
        const box = element.getBoundingClientRect()
        if (box.width === 0 || box.height === 0) continue
        // Unsichtbares zählt nicht.
        const style = getComputedStyle(element)
        if (style.visibility === 'hidden' || style.display === 'none' || style.pointerEvents === 'none') continue
        // Absichtlich versteckte Eingaben — etwa das native file-Input hinter
        // einem eigenen Knopf — bedient niemand direkt.
        if (Number(style.opacity) === 0 || style.clipPath.startsWith('inset(50%')) continue
        if (box.width <= 4 || box.height <= 4) continue
        // Eine Checkbox in einem <label> tippt man ueber das Label — das ist der
        // dokumentierte Fehlalarm des Juni-Audits (label.v1-switch = 344x47).
        const wrappingLabel = element.closest('label')
        if (wrappingLabel && wrappingLabel !== element && wrappingLabel.getBoundingClientRect().height >= min - 2) continue

        const centreX = box.left + box.width / 2
        const centreY = box.top + box.height / 2
        // Erreicht die Trefferfläche oben und unten die halbe Mindesthöhe?
        const reaches = (dy: number) => {
          const hit = document.elementFromPoint(centreX, centreY + dy)
          return hit === element || element.contains(hit) || (hit instanceof HTMLElement && hit.contains(element))
        }
        const tall = box.height >= min || (reaches(-min / 2 + 1) && reaches(min / 2 - 1))
        if (!tall) {
          const label = (element.textContent ?? '').trim().slice(0, 24) || element.getAttribute('aria-label') || element.className
          out.push(`${element.tagName.toLowerCase()}.${element.className.split(' ')[0]} "${label}" ${Math.round(box.width)}x${Math.round(box.height)}`)
        }
      }
      return out
    }, MIN)

    expect(small, `Zu kleine Bedienelemente auf ${route}:\n${small.join('\n')}`).toEqual([])
  })
}
