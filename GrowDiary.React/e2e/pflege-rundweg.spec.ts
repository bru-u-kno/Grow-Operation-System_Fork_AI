import { test, expect } from '@playwright/test'
import { backendAntwortet, darfUeberspringen } from './pflicht'

/**
 * Der Pflege-Bereich: kalibriert melden, mit Punkten — und wiederfinden.
 *
 * **Der Anlass (02.09.2026).** Der Prüfer sah, dass der neue Hardware-Rundweg
 * nur das *Geräte*formular abdeckt. Der Pflege-Bereich daneben
 * (`data-audit="care-form"`) ist ein `<div>` und kein `<form onSubmit>` — die
 * Rundweg-Zählung sieht ihn also gar nicht, und weil dieselbe Datei über das
 * Geräteformular in die Grundmenge kommt, fiel das Fehlen nicht auf.
 *
 * Das ist die Stelle, an der der Nutzer sagt „kalibriert" — und genau das, was
 * in beta.63 um die **Mehrpunkt-Kalibrierung** erweitert wurde. Eine neue
 * Funktion ohne Rundweg ist eine Behauptung.
 *
 * **Was hier geprüft wird**, in dieser Reihenfolge: Termin anlegen (über die
 * Schnittstelle, damit der Bestand sauber bleibt), auf der Seite „Kalibriert"
 * drücken, zwei Messpunkte eintragen, absenden, **neu laden**, und die Punkte
 * über `/api/calibration-events` wiederfinden. Und die Steilheit muss schon
 * beim Tippen dastehen — sie ist die eigentliche Auskunft der Zweipunkt-
 * Kalibrierung.
 */

const MARKE = `Rundweg-Pflege ${new Date().toISOString().slice(11, 19)}`

let geraetId: number | null = null
let terminId: number | null = null

test.describe.configure({ mode: 'serial' })

test.describe('Pflege-Rundweg', () => {
  test.beforeAll(async ({ playwright, baseURL }) => {
    const api = await playwright.request.newContext({ baseURL })
    try {
      if (!(await backendAntwortet(api))) return

      // Ein EIGENES Geraet, ohne Grow: dann entsteht keine Erinnerung in der
      // Aufgabenliste, gegen die andere Pruefungen laufen. Genau das war der
      // Grund, aus dem diese Seite in OHNE_RUNDWEG stand.
      const geraet = await api.post('/api/hardware-items', {
        data: { name: MARKE, category: 'Sensor', deviceKind: 'HandheldMeter', status: 'Active', criticality: 'Medium' },
      })
      if (!geraet.ok()) return
      geraetId = (await geraet.json()).id

      const morgen = new Date(Date.now() + 24 * 3600 * 1000).toISOString()
      const termin = await api.post('/api/calibration-events', {
        data: {
          hardwareItemId: geraetId,
          calibrationType: 'Ph',
          status: 'Planned',
          result: 'Unknown',
          title: 'Zweipunkt 4,01 / 7,00',
          dueAtUtc: morgen,
        },
      })
      if (termin.ok()) terminId = (await termin.json()).id
    } finally {
      await api.dispose()
    }
  })

  test.afterAll(async ({ playwright, baseURL }) => {
    const api = await playwright.request.newContext({ baseURL })
    try {
      // Das Geraet nimmt seine Vorgaenge mit — und seit heute auch deren
      // Erinnerungen (WerDieKalibrierungLoeschtLoeschtDieAufgabeTests).
      if (geraetId != null) await api.delete(`/api/hardware-items/${geraetId}`)
    } finally {
      await api.dispose()
    }
  })

  test('Rundweg: care-form — kalibriert melden, Punkte eintragen, wiederfinden', async ({ page }) => {
    darfUeberspringen(
      terminId == null,
      'Kein eigener Kalibriertermin anlegbar — laeuft die App unter GROW_OS_URL?',
    )

    /* Telefon-Hoehe: auf dem Desktop passt das ganze Formular ins Bild, und ob
       eine Meldung ueber „Eintragen" zu sehen ist, waere dort nie die Frage.
       Bei 360 × 740 lag sie unter der Bildkante (Pruefer, 03.10.2026). */
    await page.setViewportSize({ width: 360, height: 740 })
    await page.goto('/hardware', { waitUntil: 'networkidle' })

    // „Kalibriert" oeffnet den Pflege-Bereich; er ist bis dahin gar nicht da.
    await page.getByRole('row', { name: MARKE })
      .getByRole('button', { name: 'Kalibriert' }).click()

    const formular = page.locator('[data-audit="care-form"]')
    await expect(formular).toBeVisible()

    // Zwei Punkte: 4,01 und 7,00 — der Fall aus der Meldung des Nutzers.
    await formular.getByLabel('Lösung 1').fill('pH 4,01')
    await formular.getByLabel('Sollwert 1').fill('4,01')
    await formular.getByLabel('Vorher 1').fill('4,10')
    await formular.getByLabel('Nachher 1').fill('4,01')

    await formular.getByRole('button', { name: 'Punkt ergänzen' }).click()
    await formular.getByLabel('Lösung 2').fill('pH 7,00')
    await formular.getByLabel('Sollwert 2').fill('7,00')
    await formular.getByLabel('Nachher 2').fill('7,00')

    /* Erst vertippt (offene Punkte 03.10.2026, B9): „6,8x" wurde still zu
       null, der Punkt fiel weg, und mit ihm die Steilheit — eingetragen wurde
       trotzdem. Jetzt nennt die Seite das Feld und trägt NICHT ein. */
    await formular.getByLabel('Vorher 2').fill('6,8x')
    await formular.getByRole('button', { name: 'Eintragen' }).click()
    const meldung = page.getByText(/„Vorher 2" ist keine Zahl/)
    await expect(meldung, 'Ein unlesbarer Kalibrierwert verschwand ohne Meldung.').toBeVisible()
    // Sichtbar heisst hier: IM BILD. `toBeVisible` ist auch fuer Text unter der
    // Bildkante wahr — genau so blieb der erste Fix unbemerkt halb.
    await expect(meldung, 'Die Meldung steht ausserhalb des Bildes.').toBeInViewport({ ratio: 1 })

    // Dieselbe Handlung ein zweites Mal, erschwert: weggescrollt, dann wieder
    // „Eintragen". Dieselbe Meldung aendert keinen Zustand, der Scroll-Effekt
    // laeuft also nicht noch einmal — im Bild ist sie trotzdem, weil sie direkt
    // ueber dem Knopf steht, den man zum Tippen sehen muss.
    await page.evaluate(() => window.scrollTo(0, 0))
    await formular.getByRole('button', { name: 'Eintragen' }).click()
    await expect(meldung, 'Beim zweiten Versuch steht die Meldung ausserhalb des Bildes.').toBeInViewport({ ratio: 1 })
    await expect(formular, 'Das Formular schloss sich trotz unlesbarem Wert.').toBeVisible()
    const vorher = await page.request.get('/api/calibration-events')
    const nochGeplant = ((await vorher.json()) as Array<{ id: number; status: string }>)
      .find((e) => e.id === terminId)
    expect(nochGeplant?.status, 'Mit unlesbarem Wert wurde trotzdem eingetragen.').toBe('Planned')

    await formular.getByLabel('Vorher 2').fill('6,82')

    /* Die Steilheit ist die eigentliche Auskunft: ein einzelner Abgleich gegen
       7,00 verraet ueber die Sonde nichts. Sie muss schon beim Tippen dastehen,
       nicht erst nach dem Speichern — sonst hilft sie bei der Entscheidung
       „Sonde tauschen?" nicht. Gerechnet aus den VORHER-Werten:
       (6,82 − 4,10) / (7,00 − 4,01) = 91,0 %. */
    await expect(
      formular.locator('.hw-steilheit'),
      'Die Steilheit steht nicht da, obwohl zwei Punkte eingetragen sind.',
    ).toContainText('91')

    await formular.getByRole('button', { name: 'Eintragen' }).click()

    // Nachlesen heisst NEU LADEN.
    await page.goto('/hardware', { waitUntil: 'networkidle' })

    const antwort = await page.request.get('/api/calibration-events')
    expect(antwort.ok()).toBeTruthy()
    const alle = (await antwort.json()) as Array<{
      id: number
      status: string
      pointsJson?: string | null
      performedAtUtc?: string | null
    }>
    const meiner = alle.find((e) => e.id === terminId)

    expect(meiner, `Der Kalibriervorgang ${terminId} ist verschwunden.`).toBeTruthy()
    expect(meiner!.status, 'Der Vorgang steht nach dem Eintragen noch als geplant da.').not.toBe('Planned')
    expect(
      meiner!.performedAtUtc,
      'Es steht kein Durchfuehrungszeitpunkt drin — dann rechnet der Waechter die naechste '
      + 'Faelligkeit weiter vom alten Datum.',
    ).toBeTruthy()

    const punkte = meiner!.pointsJson ?? ''
    expect(
      punkte,
      'Die Messpunkte sind still verlorengegangen: der Vorgang ist erledigt, aber pointsJson '
      + 'ist leer. Genau dafuer wurde die Mehrpunkt-Kalibrierung gebaut.',
    ).toContain('6,82'.replace(',', '.'))
    expect(punkte, 'Der zweite Punkt fehlt.').toContain('4.1')
  })
})
