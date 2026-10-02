import { test, expect, type Page } from '@playwright/test'
import { darfUeberspringen } from './pflicht'
import { nimmSchloss, gibSchloss } from './schloss'

/**
 * Ein Grow, der gerade im Wachstum steht, heißt auf dem Schirm nicht „Veg".
 *
 * <b>Der Anlass (02.10.2026).</b> Nach einem Lauf von
 * `formularfelder-kommen-an.spec.ts` fand `rohe-enums.spec.ts` auf `/` und
 * `/grows` den rohen Wert „Veg (GrowStage)". Die Ursache war nicht das
 * Startdatum, sondern das <b>Flipdatum in der Zukunft</b>: ab da steht der
 * Grow wieder in der vegetativen Phase, und der Kartenkopf schrieb
 * „Veg Tag 64". Der Zeitstrahl führte für diese Phase einen eigenen Namen,
 * an `phaseName` vorbei — dort heißt sie „Wachstum".
 *
 * <b>Warum der Demobestand das nie gezeigt hat.</b> Kein Grow darin steht
 * gerade im Wachstum: einer blüht, einer ist im Sämling, einer geplant. Der
 * Zustand entstand nur zufällig als Rest eines anderen Tests. Dieser Fall
 * stellt ihn deshalb gezielt her — und räumt danach auf.
 *
 * <b>Auch der Balken.</b> Die Kurzfassung im Zeitstrahl steht in
 * Großbuchstaben (CSS); `innerText` liefert dann „VEG", und die Suche in
 * `rohe-enums` sieht es nicht. Hier wird zusätzlich `textContent` gelesen —
 * der Text, wie er im Baum steht.
 */
test.beforeEach(async () => { await nimmSchloss() })
test.afterEach(() => { gibSchloss() })
test.describe.configure({ mode: 'serial' })

const ROH_VEG = /(?<![\w-])Veg(?![\w-])/

async function texte(page: Page): Promise<{ sichtbar: string, baum: string }> {
  return page.evaluate(() => {
    const main = document.querySelector('main') as HTMLElement | null
    return { sichtbar: main?.innerText ?? '', baum: main?.textContent ?? '' }
  })
}

function umgebung(text: string, muster: RegExp): string {
  const treffer = muster.exec(text)
  if (!treffer) return ''
  return text.slice(Math.max(0, treffer.index - 60), treffer.index + 40).replace(/\s+/g, ' ')
}

test('ein Grow im Wachstum zeigt auf Live und in der Grow-Liste keinen rohen Phasennamen', async ({ page, request }) => {
  const antwort = await request.get('/api/grows/1').catch(() => null)
  darfUeberspringen(!antwort?.ok(), 'Kein Backend oder kein Grow 1 — ohne laufenden Grow gibt es keinen Kartenkopf.')

  const vorher = await antwort!.json()
  darfUeberspringen(vorher.status !== 'Running',
    'Grow 1 läuft nicht — der Demobestand legt ihn als laufenden Grow an.')

  try {
    // Den Zustand gezielt herstellen: Flip erst in einem halben Jahr. Damit
    // steht der Grow heute im Wachstum, egal wann der Test läuft.
    const flip = new Date(Date.now() + 180 * 86_400_000).toISOString().slice(0, 10)
    const gesetzt = await request.put('/api/grows/1', {
      data: {
        ...vorher,
        startDate: String(vorher.startDate ?? '').slice(0, 10),
        flipDate: flip,
      },
    })
    expect(gesetzt.ok(), `Flipdatum setzen schlug fehl (${gesetzt.status()}).`).toBe(true)
    const jetzt = await (await request.get('/api/grows/1')).json()
    // Selbsttest: steht der Grow wirklich im Wachstum? Sonst prüft der Rest nichts.
    expect(jetzt.currentStage, 'Grow 1 steht nach dem Setzen nicht in der vegetativen Phase.').toBe('Veg')

    for (const pfad of ['/', '/grows']) {
      await page.goto(pfad, { waitUntil: 'networkidle' })
      const { sichtbar, baum } = await texte(page)

      expect(umgebung(sichtbar, ROH_VEG), `${pfad}: rohes „Veg" auf dem Schirm`).toBe('')
      expect(umgebung(baum, ROH_VEG), `${pfad}: rohes „Veg" im Text (auch in Großbuchstaben gesetzt)`).toBe('')

      // Gegenprobe: der Kartenkopf mit der laufenden Phase steht wirklich da.
      expect(sichtbar, `${pfad}: kein „Wachstum Tag …" — steht der Grow im Wachstum auf dem Schirm?`)
        .toMatch(/Wachstum Tag \d+/)
    }
  } finally {
    await request.put('/api/grows/1', {
      data: {
        ...vorher,
        startDate: String(vorher.startDate ?? '').slice(0, 10),
        flipDate: String(vorher.flipDate ?? '').slice(0, 10),
      },
    })
    const zurueck = await (await request.get('/api/grows/1')).json()
    expect(String(zurueck.flipDate ?? ''), 'Aufräumen fehlgeschlagen.').toBe(String(vorher.flipDate ?? ''))
  }
})
