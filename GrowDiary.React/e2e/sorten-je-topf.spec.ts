import { test, expect } from '@playwright/test'
import { darfUeberspringen } from './pflicht'
import { nimmSchloss, gibSchloss } from './schloss'

/**
 * Ein Grow bekommt seine Sorten je Topf — schon beim Anlegen.
 *
 * <b>Der Anlass (31.08.2026).</b> Der Tester hat definiert, was ein Grow ist:
 * „ein Durchgang in einem RDWC/DWC, der N Pflanzen mit N verschiedenen
 * Sorten/Phenos beinhalten kann. In dem Grow sollten die ganzen Sorten im
 * RDWC-System stehen wie bei den Töpfen."
 *
 * <b>Was fehlte.</b> Nicht das Datenmodell — <c>PlantInstance</c> trägt
 * <c>StrainId</c> und <c>SiteIndex</c> je Pflanze, seit Monaten. Es fehlte das
 * Formular: es bot EIN Sortenfeld und schickte den Nutzer per Hinweis weiter
 * („Leg den Grow an und trag danach unter ‚Pflanzen &amp; Sorten' jede Pflanze
 * ein"). Ein Weg, der aus zwei Schritten besteht, weil einer davon fehlt, ist
 * kein Weg.
 *
 * <b>Warum diese Schicht.</b> Der Backend-Fall
 * (<c>GrowLegtPflanzenAnTests</c>) prüft, dass <c>NachAnlage</c> mit einer
 * Belegung die richtigen Pflanzen anlegt. Er kann nicht prüfen, ob das
 * Formular die Belegung überhaupt mitschickt — und genau dort saß der Fehler.
 *
 * <b>Eigener Grow.</b> Dieser Fall legt seinen eigenen an und räumt ihn weg.
 * Wer den geteilten Bestand umbaut, baut sich seinen eigenen — sonst hängt das
 * Ergebnis davon ab, welcher Test zuerst dran war.
 */

test.setTimeout(90_000)

/* <b>Dasselbe Schloss wie die anderen.</b> Dieser Fall legt zwar einen EIGENEN
   Grow an — er haengt ihn aber ans selbe Zelt und dasselbe Hydro-System wie
   Grow 1, und dessen Toepfe sind eine geteilte Menge. Beim zweiten vollen Lauf
   hintereinander fiel prompt  um („die eben angelegte Pflanze
   war in der Liste nicht zu finden"), waehrend derselbe Fall allein dreimal
   gruen lief. Ein Test, dessen Ausgang vom Zeitpunkt abhaengt, hat nichts
   geprueft. */
test.beforeEach(async () => { await nimmSchloss() })
test.afterEach(() => { gibSchloss() })

type Sorte = { id: number; name: string }
type Hydro = { id: number; potCount: number | null; tentId: number | null }

test('eine Sorte je Topf: eingetragen, gespeichert, nach dem Neuladen wieder da', async ({ page, request }) => {
  darfUeberspringen(!(await request.get('/api/grows')).ok(), 'Kein Backend.')

  const sorten = await (await request.get('/api/strains')).json() as Sorte[]
  darfUeberspringen(sorten.length < 2,
    'Weniger als zwei Sorten in der Bibliothek — ein Mehrsorten-Grow ist damit '
    + 'nicht darstellbar. Gegen den Demobestand fahren: GROW_OS_DEMO=1.')

  const aufbauten = await (await request.get('/api/hydro-setups')).json() as Hydro[]
  const aufbau = aufbauten.find((a) => (a.potCount ?? 0) >= 2)
  darfUeberspringen(!aufbau, 'Kein Hydro-System mit mindestens zwei Töpfen.')

  const [ersteSorte, zweiteSorte] = sorten
  let growId: number | null = null

  try {
    await page.goto('/grows/new', { waitUntil: 'networkidle' })
    await page.setViewportSize({ width: 1440, height: 1100 })

    await page.getByPlaceholder('Purple Lemonade RDWC').fill('E2E Sorten je Topf')

    // Zelt und System wählen — erst danach kennt das Formular die Töpfe.
    // Die Zelt-Karte, nicht „der erste Knopf": davor stehen seit forkai.227 Knöpfe des Run-Abschnitts
    // („Eigene Werte eintragen", „1 Woche"/„2 Wochen") — der erste Knopf wählte kein Zelt mehr.
    await page.locator('[data-audit="grow-wizard"] button.grow-select-card').first().click()
    const hydroKarte = page.locator('button').filter({ hasText: /Sites/ }).first()
    await hydroKarte.click()

    const abschnitt = page.locator('[data-audit="grow-toepfe"]')
    await expect(abschnitt,
      'Der Abschnitt „Töpfe & Sorten" fehlt, obwohl ein Hydro-System gewählt ist. '
      + 'Damit kann der Nutzer beim Anlegen nur EINE Sorte angeben — genau die '
      + 'Beschwerde des Testers.').toBeVisible()

    const felder = abschnitt.locator('.gw-topf select')
    const anzahlToepfe = await felder.count()
    expect(anzahlToepfe,
      'Der Abschnitt zeigt keine Töpfe, obwohl das System welche hat.')
      .toBeGreaterThanOrEqual(2)

    // Topf 1 und Topf 2 bekommen VERSCHIEDENE Sorten — der ganze Punkt.
    await felder.nth(0).selectOption(String(ersteSorte.id))
    await felder.nth(1).selectOption(String(zweiteSorte.id))

    await expect(abschnitt.locator('.gw-toepfe-zahl'),
      'Die Zählung „N von M Töpfen belegt" folgt der Auswahl nicht.')
      .toContainText('2 von')

    await page.getByRole('button', { name: 'Grow starten' }).click()
    await page.waitForURL(/\/grows\/\d+$/, { timeout: 30_000 })

    const treffer = /\/grows\/(\d+)$/.exec(page.url())
    expect(treffer, 'Nach dem Speichern steht keine Grow-Id in der Adresse.').not.toBeNull()
    growId = Number(treffer![1])

    /* Der Rundweg endet nicht am Bildschirm, sondern nach dem Neuladen: eine
       Ansicht, die den eben getippten Wert aus ihrem eigenen Zustand zeigt,
       beweist nichts über das, was gespeichert wurde. */
    await page.reload({ waitUntil: 'networkidle' })

    const pflanzen = await (await request.get(`/api/plants?growId=${growId}`)).json() as
      Array<{ siteIndex: number | null; strainId: number | null }>

    const topf1 = pflanzen.find((p) => p.siteIndex === 1)
    const topf2 = pflanzen.find((p) => p.siteIndex === 2)

    expect(topf1?.strainId,
      `Topf 1 trägt nicht die gewählte Sorte (${ersteSorte.name}).`).toBe(ersteSorte.id)
    expect(topf2?.strainId,
      `Topf 2 trägt nicht die gewählte Sorte (${zweiteSorte.name}).`).toBe(zweiteSorte.id)

    // Und die Seite sagt es auch: bei zwei Sorten steht „gemischt", nicht eine.
    await expect(page.locator('[data-audit="grow-detail-summary"]'),
      'Die Detailseite nennt bei zwei Sorten trotzdem nur eine.').toContainText('gemischt')
  } finally {
    if (growId != null) {
      await request.delete(`/api/grows/${growId}`)

      /* NACHPRUEFEN, dass wirklich nichts liegen bleibt.
         Der Kommentar oben sagte „legt seinen eigenen an und raeumt ihn weg" —
         geloescht wurde aber nur der Grow, und seine Pflanzen blieben stehen.
         Der Pruefer hat 92 solche Leichen im Testbestand gezaehlt, zwei je
         vollem Lauf. Eine Aufraeumzeile, von der niemand nachgeprueft hat,
         dass sie raeumt, ist keine. */
      const uebrig = await (await request.get(`/api/plants?growId=${growId}`)).json() as unknown[]
      expect(uebrig.length,
        `Nach dem Loeschen von Grow ${growId} haengen noch ${uebrig.length} Pflanzen daran. `
        + 'Der naechste Lauf misst dann gegen einen Bestand, den dieser hier verschmutzt hat.')
        .toBe(0)
    }
  }
})

/**
 * Die Grow-Liste nennt bei zwei Sorten nicht mehr eine davon.
 *
 * Fünf Ansichten gaben <c>grow.strain</c> aus — ein Feld, ein Name. Bei zwei
 * Sorten im selben Becken war das eine Falschaussage, und zwar an der Stelle,
 * die man am häufigsten ansieht.
 */
test('die Grow-Liste sagt bei mehreren Sorten „gemischt"', async ({ page, request }) => {
  const antwort = await request.get('/api/grows?archived=false')
  darfUeberspringen(!antwort.ok(), 'Kein Backend.')

  const grows = await antwort.json() as Array<{ id: number; name: string; pflanzenSorten?: string[] }>
  const gemischt = grows.find((g) => (g.pflanzenSorten?.length ?? 0) > 1)
  darfUeberspringen(!gemischt,
    'Kein Grow mit mehreren Sorten im Bestand — der Demobestand legt einen an '
    + '(drei White Widow, eine Gorilla Glue). Gegen ihn fahren: GROW_OS_DEMO=1.')

  await page.goto('/grows', { waitUntil: 'networkidle' })

  /* Die Karte ist ein <article>, kein Link — die erste Fassung dieser Pruefung
     suchte ein <a> und fand nichts. Bezeichner aus dem laufenden Baum holen,
     nicht aus dem Kopf. */
  const karte = page.locator('article').filter({ hasText: gemischt!.name }).first()
  await expect(karte).toBeVisible()

  const text = (await karte.innerText()).toLowerCase()
  expect(text,
    `„${gemischt!.name}" führt ${gemischt!.pflanzenSorten!.length} Sorten `
    + `(${gemischt!.pflanzenSorten!.join(', ')}), die Liste nennt aber keine Mischung. `
    + 'Eine einzelne Sorte dort ist eine Falschaussage.')
    .toContain('gemischt')
})

/**
 * Beim Bearbeiten steht in „Töpfe &amp; Sorten", was wirklich drin ist.
 *
 * <b>Der Anlass (01.09.2026).</b> Der erste Anlauf lud die Pflanzen in einem
 * eigenen Effekt — und der lief gegen den Hauptlader, dessen `setForm` die eben
 * gesetzte Belegung wieder löschte. Auf `/grows/1/setup` stand „0 von 4 Töpfen
 * belegt", während vier Pflanzen mit ihren Sorten in der Datenbank lagen. Das
 * GEGENTEIL von dem, was der Kommentar an der Stelle behauptete.
 *
 * <b>Warum dieser Fall existiert.</b> Der Prüfer hat den Fehler danach wieder
 * eingebaut und den ganzen Bestand laufen lassen: 293 Unit-Tests grün, 600
 * End-to-End-Fälle grün. <b>Nichts</b> wurde rot. Keine einzige Prüfung lud
 * `/grows/{id}/setup` und sah die Belegung an — der Fix war unbelegt. Nach
 * Regel 5 ist eine Reparatur, von der niemand gezeigt hat, dass eine Prüfung
 * sie hält, keine Reparatur.
 */
test('beim Bearbeiten steht die Belegung des Grows im Formular', async ({ page, request }) => {
  darfUeberspringen(!(await request.get('/api/grows')).ok(), 'Kein Backend.')

  const grows = await (await request.get('/api/grows?archived=false')).json() as
    Array<{ id: number; systemId: number | null }>
  const grow = grows.find((g) => g.systemId != null)
  darfUeberspringen(!grow, 'Kein Grow mit Hydro-System.')

  const pflanzen = await (await request.get(`/api/plants?growId=${grow!.id}`)).json() as
    Array<{ siteIndex: number | null; strainId: number | null }>
  const belegt = pflanzen.filter((p) => p.siteIndex != null && p.strainId != null)
  darfUeberspringen(belegt.length === 0,
    'Der Grow hat keine Pflanze mit Topf und Sorte — dann prueft der Fall etwas anderes. '
    + 'Der Demobestand legt vier an: GROW_OS_DEMO=1.')

  await page.setViewportSize({ width: 1440, height: 1100 })
  await page.goto(`/grows/${grow!.id}/setup`, { waitUntil: 'networkidle' })

  const abschnitt = page.locator('[data-audit="grow-toepfe"]')
  await expect(abschnitt).toBeVisible()

  await expect(abschnitt.locator('.gw-toepfe-zahl'),
    `Der Grow hat ${belegt.length} Pflanzen in Toepfen, das Formular zeigt eine andere Zahl. `
    + 'Wer das sieht, haelt seine Zuordnung fuer verloren.')
    .toContainText(`${belegt.length} von`)

  /* Und die SORTEN stimmen, nicht nur die Anzahl. Eine Zahl kann zufaellig
     passen; vier Auswahlfelder auf „— leer —" bei vier Pflanzen nicht. */
  const gewaehlt = await abschnitt.locator('.gw-topf select').evaluateAll((felder) =>
    (felder as HTMLSelectElement[]).map((f) => f.value))
  const gefuellt = gewaehlt.filter((wert) => wert !== '').length
  expect(gefuellt,
    `${belegt.length} Toepfe sind belegt, aber nur ${gefuellt} Auswahlfelder tragen eine Sorte. `
    + 'Das Formular zeigt das Gegenteil dessen, was in der Datenbank steht.')
    .toBe(belegt.length)
})
