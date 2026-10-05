import { test, expect } from '@playwright/test'
import { darfUeberspringen } from './pflicht'
import { nimmSchloss, gibSchloss } from './schloss'

/**
 * Ein Wasserwechsel lässt sich nachtragen — mit dem Tag, an dem er war.
 *
 * <b>Der Anlass (28.08.2026).</b> Gemeldet: „nachträglich einen wasserwechsel
 * einzutragen wäre schön und ein Datum fest zu legen, also wenn das vor tagen
 * passiert ist, dass man das nachtragen kann."
 *
 * <b>Was fehlte.</b> Das Formular unter „Wasserwechsel" fragte nach Art,
 * Anteil, Menge, EC, pH und Notiz — aber nach keinem Zeitpunkt. Jeder Eintrag
 * landete auf „jetzt". Wer sonntags wechselt und dienstags einträgt, hatte
 * danach einen Wechsel vom Dienstag in der Historie; die Berechnung „letzter
 * Wechsel vor N Tagen" zählte ab dem falschen Tag.
 *
 * Das Backend konnte es die ganze Zeit: <c>CreateChangeoutRequest</c> trägt
 * <c>PerformedAtUtc</c>. Es fragte nur niemand danach.
 */

/* <b>Das Schloss.</b> Dieser Fall schreibt an Grow 1 — und Playwright faehrt
   `fullyParallel: true`. Gefunden von `e2e-schloss-vollstaendig.node.test.ts`,
   der ueber ALLE Spec-Dateien zaehlt: der Kommentar in `schloss.ts` sagte
   „vier Dateien", und diese fuenfte war nicht darunter. Eine Zahl im
   Fliesstext altert; eine Zaehlung ueber die Grundmenge nicht. */
test.beforeEach(async () => { await nimmSchloss() })
test.afterEach(() => { gibSchloss() })

test('der Wasserwechsel steht im Hauptmenue', async ({ page, request }) => {
  darfUeberspringen(!(await request.get('/api/grows')).ok(), 'Kein Backend.')

  /* Ein direkter Aufruf von /wasserwechsel belegt NICHT, dass man dorthin
     findet — genau das war die Beschwerde: die Seite gab es (als Abschnitt),
     aber keinen Weg. Deshalb wird hier geklickt, nicht navigiert. */
  await page.goto('/', { waitUntil: 'networkidle' })
  const eintrag = page.getByRole('link', { name: 'Wasserwechsel', exact: true })
  await expect(eintrag, 'Im Hauptmenue steht kein Eintrag „Wasserwechsel". Wer den '
    + 'Wechsel eintragen will, muesste ihn wieder in einem anderen Abschnitt suchen.')
    .toBeVisible()

  await eintrag.click()
  await expect(page.locator('[data-audit="wasserwechsel-stand"]'),
    'Der Menuepunkt fuehrt nicht auf die Wasserwechsel-Seite.').toBeVisible()
})

test('ein Wasserwechsel laesst sich auf einen vergangenen Tag buchen', async ({ page, request }) => {
  darfUeberspringen(!(await request.get('/api/grows')).ok(), 'Kein Backend.')

  const vorher = await (await request.get('/api/grows/1/changeouts')).json() as
    Array<{ id: number; performedAtUtc: string }>

  /* Seit dem 31.08.2026 hat der Wasserwechsel eine eigene Seite. Vorher lag
     das Formular als dritter Abschnitt auf /addback und war nicht zu finden —
     „der User findet den Wasserwechsel nicht wirklich". */
  /* Grow 1 AUSDRUECKLICH waehlen.
     Vorher stand hier `/wasserwechsel` ohne Angabe, und die Seite nahm den
     ersten Grow der Liste. Sortiert wird nach StartDate DESC — sobald eine
     andere Pruefung sich einen eigenen Grow anlegt (ernte-rundweg,
     wasserwechsel-rundweg), kann der vorne stehen. Dieser Fall schrieb dann in
     den fremden Grow und zaehlte an /api/grows/1/changeouts nach: einmal in
     neun Laeufen rot, ohne dass sich am Code etwas geaendert haette. */
  await page.goto('/wasserwechsel?growId=1', { waitUntil: 'networkidle' })
  /* Seit A-006 (05.10.2026) ist das Formular der Ablauf in vier Schritten;
     „Wann" steht in Schritt 1, ganz oben. */
  const formular = page.locator('[data-audit="wasserwechsel-ablauf"]')
  await expect(formular).toBeVisible()

  /* Das Datumsfeld — der Kern dieser Prüfung. Ohne es landet jeder Eintrag
     auf „jetzt", und ein Nachtrag ist unmöglich. */
  const wann = formular.locator('input[type="date"], input[type="datetime-local"]').first()
  await expect(wann, 'Das Formular hat kein Feld für den Zeitpunkt — ein Wechsel von '
    + 'vorgestern lässt sich damit nicht nachtragen.').toBeVisible()

  /* Drei Tage zurück. Nicht „gestern": bei einem Lauf um Mitternacht wäre
     gestern womöglich heute, und der Fall bewiese nichts. */
  const dreiTage = new Date(Date.now() - 3 * 24 * 3600 * 1000)
  const iso = `${dreiTage.getFullYear()}-${String(dreiTage.getMonth() + 1).padStart(2, '0')}-${String(dreiTage.getDate()).padStart(2, '0')}`
  await wann.fill(`${iso}T12:00`)

  // Die Literzahl steht auf dem Anlagevolumen; ohne Zugaben speichern.
  await page.getByRole('button', { name: '2 · Ansetzen', exact: true }).click()
  await expect(formular.getByLabel('Neues Wasser in Litern')).not.toHaveValue('')
  await page.getByRole('button', { name: '4 · Speichern', exact: true }).click()
  await formular.locator('[data-audit="wasserwechsel-speichern"]').click()

  let angelegt: number | null = null
  try {
    await expect.poll(async () =>
      ((await (await request.get('/api/grows/1/changeouts')).json()) as unknown[]).length,
    { timeout: 15_000 }).toBe(vorher.length + 1)

    const nachher = await (await request.get('/api/grows/1/changeouts')).json() as
      Array<{ id: number; performedAtUtc: string }>
    const neu = nachher.find((c) => !vorher.some((v) => v.id === c.id))
    expect(neu, 'Der neue Eintrag ist nicht auffindbar.').toBeTruthy()
    angelegt = neu!.id

    const gebucht = new Date(neu!.performedAtUtc)
    const alterTage = (Date.now() - gebucht.getTime()) / (24 * 3600 * 1000)
    expect(alterTage, `Der Wechsel wurde auf ${gebucht.toISOString()} gebucht — das ist `
      + `${alterTage.toFixed(1)} Tage her, eingetragen war der Tag vor drei Tagen. Ein `
      + 'Nachtrag, der auf „jetzt" landet, verfälscht jede Rechnung „letzter Wechsel '
      + 'vor N Tagen".').toBeGreaterThan(2)
  } finally {
    /* Aufraeumen — und den Erfolg BELEGEN.
       Bis zum 31.08.2026 stand hier `DELETE /api/changeouts/{id}`: eine Route,
       die es nicht gab. Der Aufruf lief in ein 404, meldete nichts, und der
       Testbestand wuchs mit jedem Lauf um einen erfundenen Wasserwechsel. Eine
       Aufraeumzeile, von der niemand nachgeprueft hat, dass sie raeumt, ist
       keine. */
    if (angelegt != null) {
      // Seit A-006 nimmt dieser Weg den ganzen Vorgang mit (Messungen, Buchungen, Tagebuch).
      const weg = await request.delete(`/api/grows/1/changeouts/${angelegt}`)
      expect(weg.ok(),
        `Der Testeintrag ${angelegt} liess sich nicht entfernen (HTTP ${weg.status()}). `
        + 'Der Demobestand behaelt ihn — und der naechste Lauf misst gegen einen '
        + 'Bestand, den dieser hier verschmutzt hat.').toBe(true)
    }
  }
})
