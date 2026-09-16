import { test, expect, type Locator } from '@playwright/test'
import { darfUeberspringen } from './pflicht'
import { nimmSchloss, gibSchloss } from './schloss'

/**
 * NACHEINANDER, nicht parallel.
 *
 * Alle Faelle in dieser Datei aendern die Pflanzen desselben Grows am
 * laufenden Stand — Topf, Sorte, Anzahl. `fullyParallel` liesse sie
 * gleichzeitig laufen; dann raeumt der eine auf, waehrend der andere
 * misst, und das Ergebnis haengt am Zufall. Ein Test, dessen Ausgang vom
 * Zeitpunkt abhaengt, hat nichts geprueft.
 */
test.describe.configure({ mode: 'serial' })

/**
 * Scheitert nach Sekunden mit Klartext, wenn ein Bedienelement fehlt.
 *
 * Ohne diese Vorprüfung wartet `selectOption` oder `click` bis zum Zeitablauf
 * des ganzen Falls (90 s) — und danach läuft das Aufräumen im `finally` ins
 * Leere, weil Playwright die Seite schon geschlossen hat. Genau so ist dieser
 * Fall von forkai.105 bis .113 gescheitert: die Meldung nannte eine
 * Zeitüberschreitung, nicht das fehlende Feld.
 */
async function vorhanden(ort: Locator, meldung: string) {
  await expect(ort.first(), meldung).toBeVisible({ timeout: 10_000 })
}

/* <b>Mehr Zeit als die 30 Sekunden der Vorgabe.</b> Die Faelle hier schreiben
   ueber die Oberflaeche und lesen nach jedem Schritt zurueck; dazu kommt das
   Warten auf das Schloss, wenn eine andere Datei gerade an Grow 1 arbeitet.
   Beim Zeitablauf schliesst Playwright die Seite — und dann laeuft das
   `finally` ins Leere („Target page has been closed"), das den Bestand
   zuruecksetzen soll. Der naechste Lauf scheiterte daraufhin an Daten, die
   dieser hier hinterlassen hat. */
test.setTimeout(90_000)

/**
 * Die Ausgangslage wird HERGESTELLT, nicht gefordert.
 *
 * Die Fälle hier verschieben Töpfe und legen Pflanzen an; bricht einer ab —
 * etwa im Zeitablauf, dann ist seine Seite schon zu und das `finally` läuft
 * ins Leere —, bleibt eine Pflanze ohne Topf liegen. Der nächste Lauf
 * scheiterte daraufhin an „Nicht jede Pflanze hat einen Topf", ohne dass an
 * der App etwas falsch war: eine Meldung über den Vorgänger, nicht über die
 * Sache. Am 28.08.2026 dreimal hintereinander so passiert.
 *
 * Aufgeräumt wird trotzdem weiter — aber kein Fall hängt mehr davon ab.
 */
test.beforeEach(async ({ request }) => {
  /* <b>Das Schloss zuerst.</b> Vier Dateien schreiben an Grow 1, und
     `fullyParallel: true` laesst sie gleichzeitig laufen. Ohne das Schloss
     schrieb das Aufraeumen hier, waehrend `flipdatum-rundweg` sein eben
     gespeichertes Datum zurueckliest — gemessen: erster Lauf gruen, zweiter
     rot mit „eingetragen 2026-06-24, im Formular steht 2026-06-10". */
  await nimmSchloss()

  const antwort = await request.get('/api/plants?growId=1')
  if (!antwort.ok()) return
  const pflanzen = await antwort.json() as Array<{ id: number; siteIndex: number | null; strainId: number | null }>

  // Jede Pflanze hat einen Topf.
  const belegt = new Set(pflanzen.map((p) => p.siteIndex).filter((n): n is number => n != null))
  for (const p of pflanzen) {
    if (p.siteIndex != null) continue
    let frei = 1
    while (belegt.has(frei)) frei += 1
    belegt.add(frei)
    await request.put(`/api/plants/${p.id}`, { data: { ...p, siteIndex: frei } })
  }

  /* Und jeder Topf hat eine Pflanze. Ein Fall, der eine entfernt und im
     Aufräumen scheitert, hinterlässt sonst „3 Pflanzen bei 4 Töpfen" — und
     der nächste, der einen VOLLEN Bestand braucht, übersprang sich mit einer
     Meldung über den Vorgänger. */
  const grow = await (await request.get('/api/grows/1')).json()
  if (grow.systemId == null) return
  const system = await (await request.get(`/api/hydro-setups/${grow.systemId}`)).json()
  const toepfe: number = system.potCount ?? 0
  const sorte = pflanzen.find((p) => p.strainId != null)?.strainId ?? null
  for (let topf = 1; topf <= toepfe; topf += 1) {
    if (belegt.has(topf)) continue
    await request.post('/api/plants', {
      data: {
        growId: 1, strainId: sorte, label: `Pflanze ${topf}`, siteIndex: topf,
        plantRole: 'Production', plantStatus: 'Active',
      },
    })
    belegt.add(topf)
  }
})

test.afterEach(() => { gibSchloss() })

/**
 * Je Topf eine eigene Sorte — und kein Feld geht dabei verloren.
 *
 * <b>Der Anlass.</b> Ein Nutzer fährt im RDWC in jedem Topf eine andere Sorte
 * und konnte das nicht angeben. Die Sorte je Pflanze gab es längst; es fehlte
 * der <i>Ort</i> — und die Auffindbarkeit.
 *
 * <b>Warum hier auch der zweite Klick zählt.</b> Das PUT auf eine Pflanze
 * überschreibt <b>alle</b> Felder. Die alte Fassung zählte sie von Hand auf;
 * ein neues Feld wäre dabei stillschweigend genullt worden, sobald jemand nur
 * die Sorte wechselt. Genau diese Klasse — „speichern, dann NOCHMAL speichern"
 * aus CLAUDE.md — prüft dieser Fall: erst die Sorte ändern, dann den Topf, und
 * nach jedem Schritt nachsehen, ob das andere Feld noch steht.
 */
test('je Pflanze Sorte UND Topf — beides überlebt die Änderung des anderen', async ({ page, request }) => {
  darfUeberspringen(!(await request.get('/api/grows')).ok(),
    'Kein Backend — ohne Pflanzen gibt es nichts zu ändern.')

  const stand = async () => (await request.get('/api/plants?growId=1')).json()

  const vorher = await stand()
  darfUeberspringen(vorher.length < 2,
    'Weniger als zwei Pflanzen im Bestand — Demobestand.cs sollte vier anlegen.')

  // Die Grundmenge muss den Fall wirklich zeigen: mehrere Sorten, eigene Töpfe.
  const sorten = new Set(vorher.map((p: { strainId: number | null }) => p.strainId))
  expect(sorten.size, 'Alle Pflanzen tragen dieselbe Sorte — der Fall ist unsichtbar.')
    .toBeGreaterThan(1)
  expect(vorher.every((p: { siteIndex: number | null }) => p.siteIndex != null),
    'Nicht jede Pflanze hat einen Topf.').toBe(true)

  const zweite = vorher[1]
  const andereSorte = vorher.find(
    (p: { strainId: number | null }) => p.strainId !== zweite.strainId)
  expect(andereSorte, 'Keine zweite Sorte gefunden.').toBeTruthy()

  /* VOR dem `try` — das `finally` unten stellt sie zurück, und eine Konstante
     aus dem `try`-Block ist dort nicht sichtbar. */
  const dritte = vorher.find(
    (p: { id: number, siteIndex: number | null }) => p.id !== zweite.id && p.siteIndex != null)

  try {
    // --- Schritt 1: die SORTE ändern. Der Topf muss stehen bleiben.
    /* <b>Im Grow-Formular, nicht auf der Karte.</b> Seit forkai.105 zeigt die
       Pflanzenkarte die Sorte nur noch an; gewählt wird sie unter „Töpfe &
       Sorten“ im Formular und beim Speichern übernommen
       (`GrowPflanzen.SortenSetzen`). Diese Datei suchte danach weiter das
       Auswahlfeld `.gp-sorte` auf der Karte — `selectOption` wartete 90
       Sekunden auf ein Element, das es nicht mehr gab, und riss das Aufräumen
       mit. Neun Versionen lang rot (forkai.105 bis .113). */
    await page.goto('/grows/1/setup', { waitUntil: 'networkidle' })
    const sortenwahl = page.getByLabel(`Sorte in Topf ${zweite.siteIndex}`, { exact: true })
    await vorhanden(sortenwahl, `Im Formular fehlt das Auswahlfeld „Sorte in Topf ${zweite.siteIndex}“.`)
    await sortenwahl.selectOption(String(andereSorte.strainId))

    const speichern = page.locator('[data-audit="grow-wizard-actions"]')
      .getByRole('button', { name: 'Speichern', exact: true })
    await vorhanden(speichern, 'Der Speichern-Knopf des Grow-Formulars fehlt.')
    await expect(speichern, 'Das Formular sperrt das Speichern — die Planprüfung meldet einen Befund.')
      .toBeEnabled({ timeout: 5_000 })
    await speichern.click()
    await page.waitForURL(/\/grows\/1$/, { timeout: 15_000 })

    const nachSorte = (await stand()).find((p: { id: number }) => p.id === zweite.id)
    expect(nachSorte.strainId, 'Die Sorte wurde nicht übernommen.').toBe(andereSorte.strainId)
    expect(nachSorte.siteIndex,
      `Beim Sortenwechsel ging der Topf verloren (war ${zweite.siteIndex}, ist ${nachSorte.siteIndex}). `
      + 'Das PUT überschreibt alle Felder — es muss das ganze DTO mitschicken.')
      .toBe(zweite.siteIndex)

    // --- Schritt 2, erschwert: jetzt den TOPF ändern. Die Sorte muss stehen.
    //
    // Seit dem 25.08.2026 gibt es nur Töpfe, die das System auch hat, und
    // jeden höchstens einmal. Ein frei erfundener Topf 9 wird abgelehnt —
    // richtig so. Also wird erst einer frei gemacht.
    expect(dritte, 'Keine zweite Pflanze mit Topf gefunden.').toBeTruthy()
    await request.put(`/api/plants/${dritte.id}`, { data: { ...dritte, siteIndex: null } })

    // Die Karte neu laden, damit sie den frei gewordenen Topf kennt.
    await page.goto('/grows/1', { waitUntil: 'networkidle' })
    const karte = page.locator('[data-audit="grow-plants"]')
    await expect(karte).toBeVisible()
    await karte.scrollIntoViewIfNeeded()

    const neuerTopf = dritte.siteIndex
    /* Die Zeile über ihre eigene Pflanze finden, nicht über die Position: nach
       dem Freimachen steht die dritte Pflanze ohne Topf in der Liste, und die
       Reihenfolge ist nicht mehr die von vorher. `selectOption`, nicht `fill`:
       der Topf ist seit dem 28.08.2026 ein Auswahlfeld. */
    const topfwahl = karte.locator('.gp-topf select')
      .filter({ has: page.locator(`option[value="${zweite.siteIndex}"]:checked`) })
    await vorhanden(topfwahl, `Auf der Karte fehlt das Topf-Auswahlfeld der Pflanze in Topf ${zweite.siteIndex}.`)
    await topfwahl.first().selectOption(String(neuerTopf))
    await page.waitForTimeout(900)

    const nachTopf = (await stand()).find((p: { id: number }) => p.id === zweite.id)
    expect(nachTopf.siteIndex, 'Der Topf wurde nicht übernommen.').toBe(neuerTopf)
    expect(nachTopf.strainId,
      'Beim Topfwechsel ging die Sorte verloren — dieselbe Falle, andere Richtung.')
      .toBe(andereSorte.strainId)

    await request.put(`/api/plants/${zweite.id}`, { data: { ...zweite, siteIndex: null } })
    await request.put(`/api/plants/${dritte.id}`, { data: dritte })
  } finally {
    /* Aufräumen: BEIDE angefassten Pflanzen, nicht nur die zweite. Der Fall
       macht unterwegs den Topf der dritten frei; blieb sie ohne, scheiterte
       der nächste Lauf an einer Meldung über den Vorgänger.

       Schlank gehalten — vier PUTs über alle Pflanzen brachten den Fall über
       sein Zeitlimit, und dann läuft dieses `finally` gar nicht mehr. Was
       hier durchrutscht, richtet das `beforeEach` beim nächsten Mal. */
    await request.put(`/api/plants/${zweite.id}`, { data: zweite })
    await request.put(`/api/plants/${dritte.id}`, { data: dritte })
    const zurueck = (await stand()).find((p: { id: number }) => p.id === zweite.id)
    expect(zurueck.strainId, 'Aufräumen fehlgeschlagen.').toBe(zweite.strainId)
    expect(zurueck.siteIndex, 'Aufräumen fehlgeschlagen.').toBe(zweite.siteIndex)
  }
})

test('der Grow-Überblick behauptet bei mehreren Sorten keine einzelne', async ({ page, request }) => {
  darfUeberspringen(!(await request.get('/api/grows')).ok(), 'Kein Backend — siehe oben.')

  const pflanzen = await (await request.get('/api/plants?growId=1')).json()
  const sorten = new Set(
    pflanzen.map((p: { strainName: string | null }) => p.strainName).filter(Boolean))
  darfUeberspringen(sorten.size < 2, 'Nur eine Sorte im Bestand — dann prüft dieser Fall nichts.')

  await page.goto('/grows/1', { waitUntil: 'networkidle' })
  await page.waitForTimeout(600)

  const kachel = await page.locator('[data-audit="grow-detail-summary"]').innerText()
  expect(kachel,
    `Die Kachel nennt eine einzelne Sorte, obwohl ${sorten.size} im Zelt stehen: ${kachel.slice(0, 120)}`)
    .toContain('gemischt')
})

/**
 * Nicht mehr Pflanzen als Töpfe — und ein Weg zurück.
 *
 * <b>Der Anlass (25.08.2026).</b> „Du kannst mehr Sorten angeben, als es Töpfe
 * gibt." Am laufenden Stand belegt: acht Pflanzen in einem Vier-Topf-System,
 * Topf 1 doppelt, ein Topf 999 — jedes Mal HTTP 201. Dazu gab es keinen
 * Löschweg: wer eine zu viel anlegte, behielt sie.
 *
 * <b>Warum die Oberfläche und nicht nur die API.</b> Ein gesperrter Knopf ohne
 * Grund ist ein kaputter Knopf, und eine Ablehnung, die „Eingaben konnten
 * nicht validiert werden" sagt, hilft niemandem. Beides sieht nur ein Lauf am
 * gerenderten Stand.
 */
test('sind alle Töpfe belegt, sagt die Karte es — und das Entfernen macht wieder Platz', async ({ page, request }) => {
  darfUeberspringen(!(await request.get('/api/grows')).ok(), 'Kein Backend.')

  const grow = await (await request.get('/api/grows/1')).json()
  darfUeberspringen(grow.systemId == null, 'Der Grow hängt an keinem Hydro-System.')
  const system = await (await request.get(`/api/hydro-setups/${grow.systemId}`)).json()
  const toepfe: number = system.potCount
  darfUeberspringen(!toepfe, 'Das System kennt keine Töpfe.')

  const stand = async () => (await request.get('/api/plants?growId=1')).json()
  const vorher = await stand()
  darfUeberspringen(vorher.length !== toepfe,
    `Der Bestand hat ${vorher.length} Pflanzen bei ${toepfe} Töpfen — dann prüft der Fall etwas anderes.`)

  await page.goto('/grows/1', { waitUntil: 'networkidle' })
  const karte = page.locator('[data-audit="grow-plants"]')
  await karte.scrollIntoViewIfNeeded()

  // 1. Voll heisst voll — und der Grund steht daneben.
  await expect(karte.locator('.gp-bilanz')).toContainText(`${toepfe} von ${toepfe}`)
  await expect(karte.locator('.gp-neu button')).toBeDisabled()
  await expect(karte.locator('.gp-voll'),
    'Der Knopf ist gesperrt, aber niemand sagt warum.').toBeVisible()

  /* 2. Ein belegter Topf ist SICHTBAR belegt — man muss ihn nicht erst
        auswählen, um es zu erfahren.

     Bis zum 28.08.2026 war der Topf ein Zahlenfeld: wer eine belegte Nummer
     eintippte, bekam die Ablehnung danach. Jetzt steht sie in der Auswahl
     („Topf 1 · belegt (White Widow)"), und die Prüfung misst das statt der
     Fehlermeldung. Die Sperre im Backend bleibt und hat ihre eigenen Fälle. */
  const auswahl = karte.locator('.gp-topf select').nth(1)
  const belegteEintraege = await auswahl.locator('option').evaluateAll((os) =>
    os.map((o) => o.textContent?.trim() ?? '').filter((s) => s.includes('belegt')))
  expect(belegteEintraege.length, 'Kein einziger Topf ist als belegt gekennzeichnet — '
    + 'dann sieht man erst nach der Auswahl, dass er vergeben ist.').toBeGreaterThan(0)
  expect(belegteEintraege.some((s) => /\(.+\)/.test(s)),
    `Die Auswahl sagt „belegt", aber nicht von wem: ${belegteEintraege.join(' | ')}`)
    .toBe(true)

  // 3. Entfernen macht Platz — an einer Pflanze, die dieser Lauf selbst
  //    angelegt hat. Eine Demopflanze zu löschen und neu anzulegen gäbe ihr
  //    eine neue Id; alles, was daran hängt (Pheno-Bogen, Abstammung), wäre ab.
  //    Deshalb wird kurz ein Topf dazugegeben statt einer weggenommen.
  let angelegt: number | null = null
  try {
    await request.put(`/api/hydro-setups/${grow.systemId}`, {
      data: { ...system, potCount: toepfe + 1 },
    })
    await page.reload({ waitUntil: 'networkidle' })
    await karte.scrollIntoViewIfNeeded()
    await expect(karte.locator('.gp-neu button'),
      'Ein Topf mehr, und der Knopf lädt trotzdem nicht ein.').toBeEnabled()

    await karte.locator('.gp-neu button').click()
    await expect.poll(async () => (await stand()).length).toBe(toepfe + 1)
    angelegt = (await stand()).find(
      (p: { id: number }) => !vorher.some((v: { id: number }) => v.id === p.id))?.id ?? null
    expect(angelegt, 'Die neue Pflanze ist nicht auffindbar.').toBeTruthy()

    // Wieder voll, wieder gesperrt.
    await expect(karte.locator('.gp-neu button')).toBeDisabled()

    /* Und jetzt weg damit — über den Knopf, mit Rückfrage.

       GEZIELT die eben angelegte, nicht „die letzte in der Liste". Die
       Reihenfolge hängt an der Sortierung, und die kann sich ändern; getroffen
       wurde dann eine der ursprünglichen, und das Aufräumen meldete danach
       „3 Pflanzen statt 4". Am 28.08.2026 in jedem zweiten vollen Lauf. */
    /* Auf die LISTE warten, nicht nur auf die API.

       Die Zusicherung darueber pollt `/api/plants` — dort steht die neue
       Pflanze sofort. Die Karte rendert aber erst danach neu, und die Schleife
       unten liest den DOM. In etwa jedem dritten vollen Lauf kam sie eine Zeile
       zu frueh und meldete „die eben angelegte Pflanze war in der Liste nicht
       zu finden": ein Test, dessen Ausgang vom Zeitpunkt abhaengt, hat nichts
       geprueft. Gefunden am 01.09.2026 in vier Laeufen hintereinander. */
    await expect(karte.locator('.gp-liste li'),
      'Die Karte zeigt die neue Pflanze nicht — entweder rendert sie nicht neu, '
      + 'oder das Anlegen ist gar nicht angekommen.').toHaveCount(toepfe + 1)

    const zeilen = await karte.locator('.gp-liste li').count()
    let getroffen = false
    for (let i = 0; i < zeilen; i += 1) {
      const zeile = karte.locator('.gp-liste li').nth(i)
      const topf = await zeile.locator('.gp-topf select').inputValue()
      const dazu = (await stand()).find((p: { id: number }) => p.id === angelegt)
      if (topf !== String(dazu?.siteIndex ?? '')) continue
      page.once('dialog', (dialog) => void dialog.accept())
      await zeile.locator('.gp-weg').click()
      getroffen = true
      break
    }
    expect(getroffen, 'Die eben angelegte Pflanze war in der Liste nicht zu finden.')
      .toBe(true)
    await expect.poll(async () => (await stand()).length).toBe(toepfe)
  } finally {
    if (angelegt != null) await request.delete(`/api/plants/${angelegt}`)
    await request.put(`/api/hydro-setups/${grow.systemId}`, { data: system })

    /* Nachprüfen, dass nichts liegen bleibt — an der MENGE und den belegten
       Töpfen, nicht an den Ids.

       Die erste Fassung verglich Ids. Sobald ein anderer Fall eine Pflanze
       löscht und neu anlegt (was er tun muss, um genau das zu prüfen), trägt
       sie eine neue Id — und dieser Fall meldete „der Bestand hat andere
       Pflanzen als vorher", obwohl er vollständig war. Gemessen am
       28.08.2026: erster voller Lauf grün, zweiter rot.

       Was hier zählt, ist der Zustand: gleich viele Pflanzen, dieselben Töpfe
       belegt. Eine neue Id ist kein Schaden, eine fehlende Pflanze schon. */
    const zurueck = await stand()
    expect(zurueck.length,
      `Aufräumen fehlgeschlagen — ${zurueck.length} Pflanzen statt ${vorher.length}.`)
      .toBe(vorher.length)
    expect(zurueck.map((p: { siteIndex: number | null }) => p.siteIndex).sort(),
      'Aufräumen fehlgeschlagen — es sind andere Töpfe belegt als vorher.')
      .toEqual(vorher.map((p: { siteIndex: number | null }) => p.siteIndex).sort())
    const system2 = await (await request.get(`/api/hydro-setups/${grow.systemId}`)).json()
    expect(system2.potCount, 'Die Topfzahl wurde nicht zurückgesetzt.').toBe(toepfe)
  }
})

/**
 * Nach dem Entfernen und Neuanlegen heisst keine Pflanze wie eine andere.
 *
 * <b>Der Anlass (28.08.2026).</b> Gemeldet: „Der user hat eine pflanze
 * gelöscht und wieder hinzugefügt und da taucht diese doppelt auf."
 * Nachgestellt am laufenden Stand:
 * <pre>
 *   Ausgangslage:       Pflanze 1@Topf1 | Pflanze 2@Topf2 | Pflanze 3@Topf3 | Pflanze 4@Topf4
 *   nach dem Entfernen: Pflanze 1@Topf1 | Pflanze 2@Topf2 | Pflanze 4@Topf4
 *   nach dem Anlegen:   Pflanze 1@Topf1 | Pflanze 2@Topf2 | Pflanze 4@Topf4 | Pflanze 4@Topf3
 * </pre>
 *
 * <b>Die Ursache.</b> Der Name kam aus der ANZAHL
 * (<code>Pflanze ${plants.length + 1}</code>), der Topf aus der ersten freien
 * Lücke. Nach einer Löschung laufen die beiden auseinander: drei Pflanzen
 * ergeben „Pflanze 4", und die gibt es schon.
 *
 * <b>Die Regel.</b> Der Name folgt dem TOPF, nicht der Anzahl. Ein Topf trägt
 * eine Pflanze, also ist seine Nummer eindeutig — und wenn der Topf wechselt,
 * zieht der Name mit. Genau das hat der Nutzer verlangt: „dass er automatisch
 * durchzählt und wenn sich was ändert, er die Zahl automatisch zieht".
 */
test('entfernen und neu anlegen gibt keiner Pflanze den Namen einer anderen', async ({ page, request }) => {
  darfUeberspringen(!(await request.get('/api/grows')).ok(), 'Kein Backend.')

  /* <b>Ein EIGENER Grow.</b> Dieser Fall entfernt eine Pflanze und legt eine
     neue an — die bekommt eine neue Id, und ein Fall, der danach die Ids mit
     denen von vorher vergleicht, schlägt fehl, ohne dass an der App etwas
     falsch wäre. Gemessen: erster voller Lauf grün, zweiter rot mit
     „Aufräumen fehlgeschlagen — der Bestand hat andere Pflanzen als vorher".

     Wer den geteilten Bestand umbaut, baut sich seinen eigenen. */
  const vorlage = await (await request.get('/api/grows/1')).json()
  darfUeberspringen(vorlage.systemId == null, 'Der Muster-Grow hängt an keinem System.')

  const angelegterGrow = await request.post('/api/grows', {
    data: {
      name: 'Prüflauf Pflanzennamen (Testdaten)',
      tentId: vorlage.tentId,
      systemId: vorlage.systemId,
      hydroStyle: vorlage.hydroStyle ?? 'RDWC',
      plantCount: 4,
      strainId: vorlage.strainId,
      startDate: new Date().toISOString().slice(0, 10),
      status: 'Planning',
      entryPoint: 'Germination',
    },
  })
  expect(angelegterGrow.ok(), `Prüf-Grow liess sich nicht anlegen: `
    + `${angelegterGrow.status()} ${await angelegterGrow.text()}`).toBe(true)
  const growId = (await angelegterGrow.json() as { id: number }).id

  const stand = async () => (await request.get(`/api/plants?growId=${growId}`)).json() as
    Promise<Array<{ id: number; label: string; siteIndex: number | null }>>

  try {
    const vorher = await stand()
    /* Der Grow legt seine Pflanzen beim Anlegen selbst an (siehe
       `GrowPflanzen`) — das ist zugleich die Probe darauf. */
    expect(vorher.length, 'Der neue Grow hat keine Pflanzen bekommen — dann prüft '
      + 'dieser Fall nichts.').toBeGreaterThanOrEqual(3)

    await page.goto(`/grows/${growId}`, { waitUntil: 'networkidle' })
    const karte = page.locator('[data-audit="grow-plants"]')
    await karte.scrollIntoViewIfNeeded()

    /* Die MITTLERE entfernen, nicht die letzte: nur dann entsteht eine Lücke,
       und nur dann laufen Anzahl und Topfnummer auseinander. Hätte der Fall
       die letzte genommen, wäre er zufällig grün geblieben. */
    const mitte = Math.floor(vorher.length / 2)
    page.once('dialog', (dialog) => void dialog.accept())
    await karte.locator('.gp-weg').nth(mitte).click()
    await expect.poll(async () => (await stand()).length).toBe(vorher.length - 1)

    await karte.locator('.gp-neu button').click()
    await expect.poll(async () => (await stand()).length).toBe(vorher.length)

    const nachher = await stand()
    const namen = nachher.map((p) => p.label)
    const doppelt = namen.filter((n, i) => namen.indexOf(n) !== i)
    expect(doppelt, `Diese Namen kommen mehrfach vor: ${[...new Set(doppelt)].join(', ')}\n`
      + nachher.map((p) => `  ${p.label} auf Topf ${p.siteIndex}`).join('\n')).toEqual([])

    /* Und der Name PASST zum Topf. Zwei Nummern nebeneinander, die
       verschiedene Dinge sagen, sind schlimmer als eine falsche: „Pflanze 4"
       auf „Topf 3" lässt den Leser raten, welche gilt. */
    const unpassend = nachher.filter((p) =>
      p.siteIndex != null && !p.label.includes(String(p.siteIndex)))
    expect(unpassend, 'Name und Topfnummer widersprechen sich:\n'
      + unpassend.map((p) => `  „${p.label}" sitzt auf Topf ${p.siteIndex}`).join('\n'))
      .toEqual([])
  } finally {
    await request.delete(`/api/grows/${growId}`)
  }
})

/**
 * Der Topf wird GEWÄHLT, nicht getippt — und steht nur einmal da.
 *
 * <b>Der Anlass (28.08.2026).</b> Gemeldet: „Die Topf durchzählung ist
 * fehlerhaft und die Prüfung ob der Topf belegt ist ist etwas komisch, kannst
 * du das angenehmer und verständlicher für den user machen."
 *
 * <b>Was daran komisch war.</b> Die Zeile las sich
 * <code>Pflanze 1 · TOPF [1] · White Widow</code> — der Name und die Topfzahl
 * sagten dasselbe, zweimal nebeneinander. Und der Topf war ein Zahlenfeld:
 * wer 2 eintippte, während Topf 2 belegt war, bekam eine Fehlermeldung, statt
 * die belegten Töpfe vorher zu sehen. Die App weiss, welche frei sind.
 *
 * <b>Die Regel des Nutzers dazu</b> („es soll nichts doppelt sein und die
 * abbildung hat immer vorrang"): eine Angabe, eine Stelle. Der Topf ist die
 * Kennung der Zeile, und was belegt ist, steht in der Auswahl.
 */
test('der Topf steht einmal da und wird aus den freien gewaehlt', async ({ page, request }) => {
  darfUeberspringen(!(await request.get('/api/grows')).ok(), 'Kein Backend.')

  const grow = await (await request.get('/api/grows/1')).json()
  darfUeberspringen(grow.systemId == null, 'Der Grow hängt an keinem Hydro-System.')
  const system = await (await request.get(`/api/hydro-setups/${grow.systemId}`)).json()
  const pflanzen = await (await request.get('/api/plants?growId=1')).json() as
    Array<{ siteIndex: number | null }>
  darfUeberspringen(pflanzen.length === 0, 'Keine Pflanzen — dann gibt es keine Zeile.')

  await page.goto('/grows/1', { waitUntil: 'networkidle' })
  const karte = page.locator('[data-audit="grow-plants"]')
  await karte.scrollIntoViewIfNeeded()
  await page.waitForTimeout(600)

  /* <b>Kein Zahlenfeld mehr.</b> Ein Feld, in das man eine belegte Nummer
     tippen kann, verschiebt die Prüfung auf den Moment NACH der Eingabe. */
  expect(await karte.locator('.gp-topf input[type="number"]').count(),
    'Der Topf ist noch ein Zahlenfeld — wer eine belegte Nummer tippt, erfährt es '
    + 'erst danach.').toBe(0)

  const topfWahl = karte.locator('.gp-topf select')
  await expect(topfWahl.first(), 'Es gibt keine Topf-Auswahl.').toBeVisible()

  /* Die Auswahl kennt ALLE Töpfe des Systems — sonst kann man eine Pflanze
     nicht auf einen freien schieben, den es gibt. */
  const optionen = await topfWahl.first().locator('option').count()
  expect(optionen, `Die Auswahl hat ${optionen} Einträge, das System ${system.potCount} `
    + 'Töpfe (plus „kein Topf"). Ein Topf, der fehlt, ist nicht erreichbar.')
    .toBeGreaterThanOrEqual(system.potCount)

  /* Und die Zeile nennt die Nummer nur EINMAL. Vorher stand „Pflanze 1" neben
     „TOPF 1" — dieselbe Zahl zweimal, was nach zwei Angaben aussieht. */
  /* Der SICHTBARE Text der Zeile — inklusive der gewaehlten Option. Die erste
     Fassung nahm `innerText`, und das liest den Wert eines Auswahlfelds nicht
     mit: sie war gruen, waehrend im Bild „Topf 1 · TOPF · [1]" stand. */
  const ersteZeile = await karte.locator('.gp-liste li').first().evaluate((li) => {
    const stuecke: string[] = []
    for (const el of li.querySelectorAll('*')) {
      const eigen = [...el.childNodes].filter((k) => k.nodeType === 3)
        .map((k) => k.textContent ?? '').join('').trim()
      if (eigen) stuecke.push(eigen)
      if (el instanceof HTMLSelectElement) {
        stuecke.push(el.options[el.selectedIndex]?.textContent?.trim() ?? '')
      }
    }
    return stuecke.filter(Boolean).join(' · ')
  })
  const topfNummer = String(pflanzen[0].siteIndex ?? '')
  if (topfNummer) {
    const treffer = ersteZeile.split(new RegExp(`\b${topfNummer}\b`)).length - 1
    expect(treffer, `Die Nummer ${topfNummer} steht ${treffer}-mal in derselben Zeile: `
      + `„${ersteZeile.replace(/\n/g, ' · ')}"`).toBeLessThanOrEqual(1)
  }
})
