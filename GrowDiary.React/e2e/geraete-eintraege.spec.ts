import { test, expect } from '@playwright/test'

/**
 * Eintraege unter einem Geraet: nach Art gruppiert, Zweck gross, ID klein,
 * Verschieben im ⋯-Menue je Eintrag (forkai.195). Nur die Geraete-Antwort ist gemockt.
 */
const mk = (entityId: string, ...zwecke: string[]) => ({
  entityId,
  verwendungen: zwecke.map((zweck) => ({ zweck, quelle: zweck.startsWith('Steuerung') ? 'steuerung' : 'messgroesse' })),
  verschoben: false,
  herkunftName: null,
})

const SEITE = {
  anzahlGeraete: 1, anzahlEntitaeten: 5, anzahlUnzugeordnet: 0, anzahlVerschoben: 0, hinweise: [],
  geraete: [{
    schluessel: 'ha:g', name: 'Bluelab Guardian', elternSchluessel: null, anschluss: null, istController: false, istRubrik: false,
    istUnzugeordnet: false, elternVomNutzer: false, nameVomNutzer: false, abgeleiteterEltern: null, modell: 'Bluelab Guardian',
    tentId: 1, hardwareItemId: 11,
    entitaeten: [
      mk('number.bluelab_guardian_ph_high_alarm', 'Steuerung BLUELAB · pH · oben'),
      mk('script.edenic_set_alarm', 'Steuerung BLUELAB · Grenze setzen · Skript'),
      mk('number.bluelab_guardian_ec_high_alarm', 'Steuerung BLUELAB · EC · oben'),
      mk('sensor.bluelab_guardian_ph', 'Messgröße ReservoirPh'),
      mk('sensor.bluelab_guardian_temperature', 'Messgröße ReservoirWaterTemp', 'Steuerung CHILLER · Wasserfühler'),
    ],
  }],
}

for (const breite of [360, 1280]) {
  test(`Eintraege stehen nach Art geordnet (${breite} px)`, async ({ page }) => {
    await page.setViewportSize({ width: breite, height: 900 })
    await page.route(/\/api\/geraete$/, (route) => route.fulfill({ json: SEITE }))
    await page.goto('/geraete')
    await page.locator('.gr-kopf').first().click()

    // Gruppen in fester Reihenfolge mit ihrer Zahl.
    await expect(page.locator('.gr-gruppe-kopf')).toHaveText(['Messwerte2', 'Einstellungen2', 'Skripte1'])

    // Darin nach Zweck sortiert, Zweck gross, ID klein darunter.
    await expect(page.locator('.gr-gruppe').nth(0).locator('.gr-eintrag-titel')).toHaveText(['ReservoirPh', 'ReservoirWaterTemp'])
    await expect(page.locator('.gr-gruppe').nth(1).locator('.gr-eintrag-titel')).toHaveText(['EC · oben ›', 'pH · oben ›'])
    await expect(page.locator('.gr-gruppe').nth(1).locator('li').first().locator('code')).toHaveText('number.bluelab_guardian_ec_high_alarm')

    // Das grosse „Gehoert zu"-Feld steht nicht mehr bei jedem Eintrag, sondern erst nach ⋯.
    await expect(page.locator('.gr-entitaeten .v1-select')).toHaveCount(0)
    await page.locator('.gr-eintrag .gr-mehr').first().click()
    await expect(page.locator('.gr-entitaeten .v1-select')).toHaveCount(1)

    // Nichts ragt ueber den Bildschirm.
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
  })
}
