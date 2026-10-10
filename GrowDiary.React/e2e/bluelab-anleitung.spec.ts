import { test, expect, type Page } from '@playwright/test'
import { backendAntwortet, darfUeberspringen } from './pflicht'

/**
 * A-016 (Etappe 6): Die Bluelab-Anleitung oben im Reiter „Rollen".
 *
 * Der erste Fall läuft gegen die echte Demo-App (frisch: nichts zugeordnet). Die übrigen bekommen die Rollen-Seite und
 * den Stand der Übertragung vorgegeben (`page.route`): die Demo hat kein Bluelab-Gerät, und geprüft wird, was die
 * Anleitung aus dem Stand macht — Häkchen, Warnung, Zuklappen.
 */

type Json = Record<string, unknown>
const ALARME = ['ph_low', 'ph_high', 'ec_low', 'ec_high', 'temp_low', 'temp_high']

function zeile(rolle: string, eingetragen: string, gefunden: boolean): Json {
  return {
    rolle, label: rolle === 'skript' ? 'Grenze setzen · Skript' : `Alarm ${rolle}`, gruppe: rolle === 'skript' ? 'schalten' : 'messen',
    einheit: null, hinweis: null, pflicht: false, domains: rolle === 'skript' ? ['script'] : ['number'], vorgabe: '',
    eingetragen, entityId: eingetragen || null, livewert: gefunden ? '5.6' : null, gefunden,
  }
}

async function seite(page: Page, zeilen: Json[], stand: Json | null) {
  await page.route(/\/api\/steuerung\/geraete$/, (route) => route.fulfill({
    json: { haErreichbar: true, module: [{ modul: 'bluelab', titel: 'Bluelab · Gerätealarm', zeilen }], eigene: [] },
  }))
  if (stand) await page.route(/\/api\/steuerung\/bluelab$/, (route) => route.fulfill({ json: stand }))
  await page.goto('/geraete?reiter=rollen&modul=bluelab')
}

const anleitung = (page: Page) => page.locator('[data-audit="bluelab-anleitung"]')

test('frisch: die Anleitung ist offen und nennt, was fehlt (echte Demo)', async ({ page, request }) => {
  darfUeberspringen(!(await backendAntwortet(request)), 'Kein Backend unter GROW_OS_URL — die Rollen-Seite braucht die laufende App.')
  await page.goto('/geraete?reiter=rollen&modul=bluelab')
  await expect(anleitung(page)).toBeVisible()
  await expect(anleitung(page).locator('ol li')).toHaveCount(4)
  await expect(anleitung(page)).toContainText('Zugeordnet 0 von 6')
  await expect(anleitung(page)).toContainText('setting_key')
  await expect(anleitung(page)).not.toContainText('eingerichtet ✓')
})

test('frisch (vorgegeben): kein Häkchen, das Skript-Detail lässt sich öffnen — die sechs Schlüssel stehen drin', async ({ page }) => {
  await seite(page, [...ALARME.map((r) => zeile(r, '', false)), zeile('skript', '', false)], { eingerichtet: false, zuletztUtc: null, fehler: null })
  await expect(anleitung(page).locator('li.is-ok')).toHaveCount(0)
  await anleitung(page).getByText('Was das Skript tun muss').click()
  for (const k of ['ph_low', 'ph_high', 'ec_low', 'ec_high', 'temp_low', 'temp_high']) {
    await expect(anleitung(page)).toContainText(`setting.${k}_alarm`)
  }
  await expect(anleitung(page)).toContainText('sieht und speichert sie nie')
})

test('eingerichtet: vier Häkchen, „eingerichtet ✓" und zugeklappt; Aufklappen und wieder Zuklappen geht — zweimal', async ({ page }) => {
  await seite(page, [...ALARME.map((r) => zeile(r, `number.bluelab_guardian_${r}_alarm`, true)), zeile('skript', 'script.edenic_set_alarm', true)],
    { eingerichtet: true, zuletztUtc: '2026-10-09T20:43:00Z', fehler: null })
  const kopf = anleitung(page).locator('.st-anleitung-kopf')
  await expect(kopf).toContainText('eingerichtet ✓')
  await expect(kopf).toHaveAttribute('aria-expanded', 'false')
  await expect(anleitung(page).locator('ol')).toHaveCount(0)

  for (let i = 0; i < 2; i++) {
    await kopf.click()
    await expect(anleitung(page).locator('li.is-ok')).toHaveCount(4)
    await expect(anleitung(page)).toContainText('6 von 6 zugeordnet und gefunden')
    await expect(anleitung(page)).toContainText('script.edenic_set_alarm gefunden')
    await expect(anleitung(page)).toContainText('keine Störung')
    await kopf.click()
    await expect(anleitung(page).locator('ol')).toHaveCount(0)
  }
})

test('Störung: Warnung mit der Meldung, Anleitung bleibt offen', async ({ page }) => {
  await seite(page, [...ALARME.map((r) => zeile(r, `number.${r}`, true)), zeile('skript', 'script.weg', false)],
    { eingerichtet: true, zuletztUtc: '2026-10-09T20:43:00Z', fehler: 'kein Access-Token' })
  await expect(anleitung(page).locator('li.is-warn')).toHaveCount(2)
  await expect(anleitung(page)).toContainText('script.weg ist zugeordnet, aber in Home Assistant nicht gefunden')
  await expect(anleitung(page)).toContainText('Übertragung gestört: kein Access-Token')
  await expect(anleitung(page).locator('.st-anleitung-kopf')).toHaveAttribute('aria-expanded', 'true')
})

test('Handy 320 px: nichts ragt über den Rand', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 800 })
  await seite(page, [...ALARME.map((r) => zeile(r, `number.bluelab_guardian_${r}_alarm`, true)), zeile('skript', 'script.sehr_langer_name_fuer_ein_skript_das_eine_grenze_setzt', false)],
    { eingerichtet: true, zuletztUtc: null, fehler: 'Wert 7.1 liegt ausserhalb von setting.ph_high_alarm — Home Assistant hat es abgelehnt' })
  await anleitung(page).getByText('Was das Skript tun muss').click()
  const ragtRaus = await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth)
  expect(ragtRaus, 'Die Seite ist breiter als der Bildschirm').toBe(false)
})
