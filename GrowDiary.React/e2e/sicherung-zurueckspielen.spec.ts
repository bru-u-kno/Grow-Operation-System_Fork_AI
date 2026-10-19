import { test, expect } from '@playwright/test'
import { backendAntwortet, darfUeberspringen } from './pflicht'
import { gibSchloss, nimmSchloss } from './schloss'

/**
 * Einstellungen → Daten → „Zurückspielen …“: eine Sicherung hochladen, prüfen, zurückspielen.
 *
 * <b>Der Anlass (19.10.2026).</b> Beim Umzug vom alten auf das neue Add-on konnte die App Sicherungen nur erzeugen und
 * herunterladen. Den Server dafür (`restore-plan`, `restore`) gab es, einen Knopf nicht, und hochladen ging gar nicht.
 *
 * Der Rundweg läuft gegen die echte App: sichern, die Datei herunterladen, über die Oberfläche wieder hochladen, die Prüfung
 * lesen, zurückspielen. Zurückgespielt wird die Sicherung der App selbst, der Stand bleibt also derselbe. Die Prüfung ist
 * die Sicht des Nutzers — sie liest den Text auf dem Schirm, nicht den Zustand dahinter.
 */
const KEIN_BACKEND = 'Kein Backend unter der Basis-Adresse: Hochladen und Zurückspielen laufen nur mit der App.'

// Zurückspielen ersetzt die ganze Datenbank der App — kein anderer Test darf währenddessen schreiben.
test.beforeEach(async () => { await nimmSchloss() })
test.afterEach(() => { gibSchloss() })

test('Sicherung hochladen, Prüfung lesen und zurückspielen', async ({ page, request }) => {
  darfUeberspringen(!(await backendAntwortet(request)), KEIN_BACKEND)

  const erzeugt = await request.post('/api/system/backup')
  expect(erzeugt.ok()).toBeTruthy()
  const manifest = await erzeugt.json() as { fileName: string; downloadUrl: string }
  const datei = await request.get(manifest.downloadUrl)
  expect(datei.ok()).toBeTruthy()
  const inhalt = await datei.body()

  await page.goto('/einstellungen', { waitUntil: 'networkidle' })
  await page.getByRole('button', { name: /Zurückspielen/ }).click()

  const blatt = page.getByRole('dialog')
  await expect(blatt).toContainText('Sicherung zurückspielen')
  await expect(blatt.getByRole('button', { name: 'Jetzt zurückspielen' })).toBeDisabled()

  await blatt.locator('input[type=file]').setInputFiles({ name: 'grow-os-backup-umzug.zip', mimeType: 'application/zip', buffer: inhalt })

  const pruefung = blatt.locator('[data-audit="sicherung-pruefung"]')
  await expect(pruefung).toBeVisible()
  await expect(pruefung).toContainText('Datenbank enthalten')
  await expect(pruefung.locator('li', { hasText: 'Datenbank enthalten' })).toContainText('ja')
  await expect(pruefung.locator('li', { hasText: 'Datenbank-Stand passt zur App' })).toContainText('ja')
  await expect(pruefung.locator('li', { hasText: 'Zugangsdaten und unsichere Pfade' })).toContainText('keine')
  // Ohne Haken beim Sichern sind keine Bilder dabei — die Zeile sagt es, statt zu schweigen.
  await expect(pruefung.locator('li', { hasText: 'Fotos und Kamerabilder' })).toContainText('nicht enthalten')

  const zurueck = blatt.getByRole('button', { name: 'Jetzt zurückspielen' })
  await expect(zurueck).toBeEnabled()
  await zurueck.click()

  const ergebnis = blatt.locator('[data-audit="sicherung-ergebnis"]')
  await expect(ergebnis).toContainText('Zurückgespielt.')
  await expect(ergebnis).toContainText('grow-os-backup-')
  await expect(blatt.getByRole('button', { name: 'Seite neu laden' })).toBeVisible()
})

test('eine Datei, die keine Sicherung ist, wird abgewiesen und nichts wird freigegeben', async ({ page, request }) => {
  darfUeberspringen(!(await backendAntwortet(request)), KEIN_BACKEND)

  await page.goto('/einstellungen', { waitUntil: 'networkidle' })
  await page.getByRole('button', { name: /Zurückspielen/ }).click()
  const blatt = page.getByRole('dialog')

  await blatt.locator('input[type=file]').setInputFiles({ name: 'foto.zip', mimeType: 'application/zip', buffer: Buffer.from('das ist kein zip') })

  await expect(blatt).toContainText('keine gültige Sicherung')
  await expect(blatt.locator('[data-audit="sicherung-pruefung"]')).toHaveCount(0)
  await expect(blatt.getByRole('button', { name: 'Jetzt zurückspielen' })).toBeDisabled()
})

test('mit dem Haken „Fotos und Kamerabilder mitsichern“ geht die Sicherung mit Bildern los', async ({ page, request }) => {
  darfUeberspringen(!(await backendAntwortet(request)), KEIN_BACKEND)

  await page.goto('/einstellungen', { waitUntil: 'networkidle' })
  const zeile = page.locator('[data-audit="sicherung-bilder"]')
  await expect(zeile).toContainText('Fotos und Kamerabilder mitsichern')
  await expect(zeile).toContainText('Standard: aus')
  await expect(zeile.locator('input[type=checkbox]')).not.toBeChecked()

  await zeile.locator('input[type=checkbox]').check()
  const anfrage = page.waitForRequest((r) => r.method() === 'POST' && r.url().includes('/api/system/backup?bilder=true'))
  await page.getByRole('button', { name: 'Jetzt sichern' }).click()
  await anfrage
})
