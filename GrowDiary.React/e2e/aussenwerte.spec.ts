import { test, expect } from '@playwright/test'
import { backendAntwortet, darfUeberspringen } from './pflicht'

/**
 * Die Außenwerte — die Luft um das Zelt, z. B. der eingebaute Fühler eines
 * AC-Infinity-Controllers („Aussen Temp / Feucht / VPD" in dessen App).
 *
 * **Was hier geprüft wird.** Was erst an der laufenden App entsteht: dass die
 * drei Werte im Verlaufsdiagramm als Karten mit deutschem Namen und deutscher
 * Zahl stehen, dass ein Tipp ihre Kurve ein- und wieder ausblendet, und dass
 * das Zuordnungsformular sie verständlich anbietet — mit dem Satz, dass es
 * nicht um die Luft von draußen geht (die liest die Zuluft-Steuerung).
 *
 * Nur lesend und klickend: die Zuordnung selbst ändert hier niemand, der
 * Demobestand legt sie an (`Demobestand.SensorenZuordnen` über
 * `DemoData.StatesFor`).
 */

const AUSSEN = [
  { key: 'outside-temperature', name: 'Außen Temp.', einheit: '°C' },
  { key: 'outside-humidity', name: 'Außen RLF', einheit: '%' },
  { key: 'outside-vpd', name: 'Außen VPD', einheit: 'kPa' },
] as const

test.describe('Außenwerte', () => {
  test('stehen im Verlauf als Karten und blenden ihre Kurve ein und aus — zweimal (390 px)', async ({ page, request }) => {
    darfUeberspringen(!(await backendAntwortet(request)), 'Kein Backend — die Außenwerte brauchen die laufende App mit Demobestand.')
    await page.setViewportSize({ width: 390, height: 900 })
    await page.goto('/', { waitUntil: 'networkidle' })
    const verlauf = page.locator('[data-audit="verlauf"]').first()
    darfUeberspringen(await verlauf.count() === 0, 'Keine Verlaufs-Kachel, obwohl der Demobestand eine anlegt.')
    await verlauf.scrollIntoViewIfNeeded()

    for (const { key, name, einheit } of AUSSEN) {
      const karte = verlauf.locator(`[data-audit="verlauf-karte-${key}"]`)
      await expect(karte, `Keine Karte für ${key} — Liste, Abruf-Obergrenze oder Demoverlauf?`).toHaveCount(1)
      await expect(karte.locator('.vd-wert-name')).toHaveText(name)
      // Eine deutsche Zahl mit Einheit, nicht der Schlüssel und kein Strich.
      await expect(karte.locator('[data-audit="verlauf-kartenwert"]')).toHaveText(new RegExp(`^\\d+(,\\d+)?\\s*${einheit}$`))

      const kurve = verlauf.locator(`[data-audit="verlauf-kurve-${key}"]`)
      const anfangs = await karte.getAttribute('aria-pressed')
      for (let runde = 0; runde < 2; runde++) {
        await karte.click()
        await expect(kurve, `Runde ${runde + 1}: Der Tipp auf ${name} schaltet die Kurve nicht um.`)
          .toHaveCount(anfangs === 'true' ? 0 : 1)
        await karte.click()
        await expect(kurve, `Runde ${runde + 1}: Der zweite Tipp auf ${name} stellt die Kurve nicht zurück.`)
          .toHaveCount(anfangs === 'true' ? 1 : 0)
      }
    }
  })

  test('das Zuordnungsformular bietet sie an und sagt, was „Außen" heißt', async ({ page, request }) => {
    darfUeberspringen(!(await backendAntwortet(request)), 'Kein Backend — das Formular braucht die laufende App.')
    await page.setViewportSize({ width: 390, height: 900 })
    await page.goto('/home-assistant', { waitUntil: 'networkidle' })
    const zeilen = page.locator('[data-audit="ha-entity-row"]')
    expect(await zeilen.count(), 'Keine Zuordnungszeilen — die Prüfung sähe nichts.').toBeGreaterThanOrEqual(6)

    for (const { name } of AUSSEN) {
      const zeile = zeilen.filter({ has: page.locator('.ha-metric', { hasText: name }) })
      await expect(zeile, `Keine Zeile „${name}" in der Gruppe Zelt.`).toHaveCount(1)
      await expect(zeile.locator('.ha-metric-hinweis')).toContainText('außerhalb des Zelts')
      await expect(zeile.locator('.ha-metric-hinweis')).toContainText('nicht die Luft von draußen')
      // Der Demobestand ordnet sie zu — die Zeile zeigt den Livewert, nicht „nicht gemappt".
      await expect(zeile.locator('.co-row-value')).not.toHaveText(/nicht gemappt/i)
    }
  })
})
