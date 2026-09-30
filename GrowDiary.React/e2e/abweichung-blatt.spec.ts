import { expect, test, type Page, type Route } from '@playwright/test'

/**
 * Fork AI (forkai.154): „Erlaubte Abweichung ± K" im Grenzwerte-Blatt der
 * Lufttemperatur — vom Tippen bis zum Speicher-Aufruf.
 *
 * <b>Der Anlass.</b> Bru, 30.09.2026, Blütewoche 6: Abweichung auf 4 gestellt, Tag
 * blieb 20–26 °C, Nacht 16–22 °C — die Zahl sah wirkungslos aus. Die erste
 * Reparatur rechnete die Zeilen live mit, schickte die Vorschau aber auch per PUT;
 * der Server hielt 19/27 dann für eine Handänderung und hätte die Grenzen nie
 * wieder mit dem Plan nachgezogen. Beides sitzt im BLATT (WertBlatt.speichern) —
 * `tag-nacht.test.ts` prüft nur die Rechnung, nicht, dass das Blatt sie benutzt.
 *
 * <b>Warum mit eigenen Antworten statt gegen den Demobestand.</b> Der Fall braucht
 * eine Lufttemperatur-Regel, die dem Plan folgt, mit bekannten Zahlen — und der
 * Speicher-Aufruf darf den gemeinsamen Bestand der anderen Dateien nicht
 * verändern. Die Antworten sind die echten des Add-ons vom 30.09.2026, auf die
 * Lufttemperatur beschränkt. Alle /api-Aufrufe bleiben im Browser; diese Datei
 * schreibt nichts in den Bestand und läuft ohne und mit Backend gleich.
 */

const folgt = (rolle: string, wert: string) => ({ rolle, name: rolle, wert, zustand: 'folgt dem Plan' })

const ZIELWERTE = {
  growId: 1, growName: 'Grow', phase: 'Flower', woche: 'Blütewoche 6', programm: 'SKX Canna Aqua',
  hinweise: [], gruppen: [], letzteUebergabe: null, zeltId: 1, spalteId: 'flower-w6', eigenerPlan: true,
  uebergabe: [folgt('luft-unten', '20'), folgt('luft-oben', '26'), folgt('luft-nacht-unten', '16'), folgt('luft-nacht-oben', '22')],
  werte: [{
    key: 'temperature', name: 'Lufttemperatur', einheit: '°C', ist: '22,8', istZahl: 22.8, min: 19, max: 19, band: '19',
    quelle: 'Plan', quelleZusatz: 'Blütewoche 6 · ±3 K', lage: 'darüber', alarm: '20 – 26',
    kette: [{ name: 'Zelt-Grenze · Fest', hinweis: 'vom Plan nachgezogen', wert: '20 – 26', gilt: true, weg: false }],
    regel: { quelle: 'Fest', min: 20, max: 26, nachtMin: 16, nachtMax: 22, toleranz: null, standardToleranz: 0, karenzMinuten: 10, aktiv: true, planMoeglich: false },
    alarmVon: 16, alarmBis: 22, meldet: true,
    planFelder: [{ feld: 'airTempC', bezeichnung: 'Luft', einheit: '°C', min: 10, max: 40, schritt: 0.5, wert: 23, startwert: 23, herkunft: 'programm' }],
  }],
}

const REGELN = {
  tentId: 1,
  rules: [
    { metricKey: 'temperature', minValue: 20, maxValue: 26, notifyService: '', enabled: true, cooldownMinutes: 10, quelle: 'Fest', nightMinValue: 16, nightMaxValue: 22 },
  ],
}

type Gesendet = { methode: string; pfad: string; body: string | null }

async function antwortenUmleiten(page: Page, gesendet: Gesendet[]) {
  await page.route('**/api/**', async (route: Route) => {
    const anfrage = route.request()
    const pfad = new URL(anfrage.url()).pathname
    const json = (body: unknown, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) })
    if (pfad === '/api/zielwerte') return json(ZIELWERTE)
    if (pfad === '/api/alerts/tents/1' && anfrage.method() === 'GET') return json(REGELN)
    if (anfrage.method() !== 'GET') {
      gesendet.push({ methode: anfrage.method(), pfad, body: anfrage.postData() })
      return json({})
    }
    return json({ fehler: 'nicht Teil dieses Falls' }, 404)
  })
}

const zeile = (page: Page, titel: 'Tag' | 'Nacht') => ({
  unter: page.getByLabel(`${titel}: melden unter`),
  ueber: page.getByLabel(`${titel}: melden über`),
  kasten: page.locator(`[data-audit="alarm-zeile-${titel}"]`),
})

test('Abweichung rechnet Tag und Nacht live mit — gespeichert werden die bisherigen Grenzen', async ({ page }) => {
  const gesendet: Gesendet[] = []
  await antwortenUmleiten(page, gesendet)
  await page.goto('/grenzwerte')
  await page.locator('[data-audit="zielwert-karte"]', { hasText: 'Lufttemperatur' }).click()

  const abweichung = page.getByLabel('Erlaubte Abweichung ± K')
  const tag = zeile(page, 'Tag')
  const nacht = zeile(page, 'Nacht')
  await expect(tag.unter).toHaveValue('20')
  await expect(nacht.ueber).toHaveValue('22')

  // Erste Änderung: aus Planwert 23 / 19 °C wird ± 4 K.
  await abweichung.fill('4')
  await expect(tag.unter).toHaveValue('19')
  await expect(tag.ueber).toHaveValue('27')
  await expect(nacht.unter).toHaveValue('15')
  await expect(nacht.ueber).toHaveValue('23')
  await expect(tag.kasten).toContainText('folgt dem Plan')
  await expect(tag.kasten).toContainText('Planwert 23 °C ± 4 K')
  await expect(nacht.kasten).toContainText('Planwert 19 °C ± 4 K')

  // Zweite Änderung, ohne neu zu laden (CLAUDE.md: die Reparatur einmal wiederholen).
  await abweichung.fill('4,5')
  await expect(tag.unter).toHaveValue('18,5')
  await expect(nacht.ueber).toHaveValue('23,5')

  // Leeres Feld heißt: der Server nimmt ± 3 — die Vorschau zeigt genau das.
  await abweichung.fill('')
  await expect(tag.unter).toHaveValue('20')
  await expect(nacht.ueber).toHaveValue('22')
  await expect(tag.kasten).toContainText('± 3 K')

  await abweichung.fill('4,5')
  await page.getByRole('button', { name: /^(Trotzdem )?[Ss]peichern$/ }).click()

  // Die Zeilen folgen dem Plan: raus gehen die BISHERIGEN Grenzen mit der neuen
  // Abweichung; neu rechnet der Server (danach /api/wochenplan/uebergeben).
  // Gingen hier 18,5/27,5 raus, hielte der Server das für „von dir".
  await expect.poll(() => gesendet.map((g) => `${g.methode} ${g.pfad}`)).toEqual([
    'PUT /api/alerts/tents/1',
    'POST /api/wochenplan/uebergeben',
  ])
  const regel = JSON.parse(gesendet[0].body ?? '{}').rules.find((r: { metricKey: string }) => r.metricKey === 'temperature')
  expect(regel).toMatchObject({ minValue: 20, maxValue: 26, nightMinValue: 16, nightMaxValue: 22, toleranz: 4.5, quelle: 'Fest' })
})
