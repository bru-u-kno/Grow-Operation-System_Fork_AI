import { test, expect, type Page, type Frame } from '@playwright/test'

/**
 * Tipp auf eine Push-Meldung → die richtige Seite in Grow OS.
 *
 * Der Link heißt /app/<slug>/<seite> (SupervisorInfoService.PanelPath). Home
 * Assistant lädt im iframe immer die Startseite der App und nennt den Rest
 * nur auf Anmeldung per postMessage. Diese Hülle spielt genau das HA-Frontend
 * 20260826.7 nach (src/panels/app/ha-panel-app.ts): beantwortet
 * `home-assistant/subscribe-properties` mit `home-assistant/properties`
 * (route = computeRouteTail), führt `home-assistant/navigate` aus und schickt
 * danach die neue Route.
 *
 * Bis forkai.167 meldete sich die App nie an — und der Link führte vorher
 * schon auf „404: Not Found", weil er auf /<slug> zeigte (04.10.2026).
 */

const SLUG = 'd48160c2_grow_os_fork_ai'

/** Lädt eine gleich-herkünftige Seite und baut daraus die HA-Hülle mit der App im iframe. */
async function huelle(page: Page, haPfad: string): Promise<Frame> {
  await page.goto('/icons/' + 'gibt-es-nicht.txt').catch(() => undefined)
  await page.evaluate(
    ({ haPfad, slug }) => {
      document.documentElement.innerHTML =
        '<body style="margin:0"><iframe id="f" src="/" style="width:400px;height:800px;border:0"></iframe></body>'
      const w = window as unknown as Record<string, unknown>
      w.__haPfad = haPfad
      w.__nachrichten = [] as unknown[]
      const f = document.getElementById('f') as HTMLIFrameElement
      const schicke = () => {
        const voll = w.__haPfad as string
        const praefix = `/app/${slug}`
        f.contentWindow!.postMessage(
          { type: 'home-assistant/properties', narrow: true, route: { prefix: praefix, path: voll.slice(praefix.length) } },
          '*',
        )
      }
      w.__schicke = schicke
      window.addEventListener('message', (e) => {
        if (e.source !== f.contentWindow) return
        ;(w.__nachrichten as unknown[]).push(e.data)
        if (e.data?.type === 'home-assistant/subscribe-properties') schicke()
        if (e.data?.type === 'home-assistant/navigate') {
          w.__haPfad = e.data.path
          schicke()
        }
      })
    },
    { haPfad, slug: SLUG },
  )
  await expect.poll(() => page.frames().length).toBeGreaterThan(1)
  return page.frames().find((f) => f !== page.mainFrame())!
}

const appPfad = (frame: Frame) => frame.evaluate(() => location.pathname)
const haPfad = (page: Page) => page.evaluate(() => (window as unknown as { __haPfad: string }).__haPfad)
const tippe = (page: Page, pfad: string) =>
  page.evaluate((p) => {
    const w = window as unknown as { __haPfad: string; __schicke: () => void }
    w.__haPfad = p
    w.__schicke()
  }, pfad)
const blaettere = (frame: Frame, pfad: string) =>
  frame.evaluate((p) => {
    history.pushState(null, '', p)
    dispatchEvent(new PopStateEvent('popstate'))
  }, pfad)

test.describe('Push-Link öffnet die Seite aus der Meldung', () => {
  test('Kaltstart, Weiterblättern, zweiter und dritter Tipp', async ({ page }) => {
    const frame = await huelle(page, `/app/${SLUG}/aufgaben`)

    // Kaltstart: App lädt „/", springt auf /aufgaben, HA-Adresse zurück aufs Präfix.
    await expect.poll(() => appPfad(frame)).toBe('/aufgaben')
    await expect(frame.getByRole('heading', { level: 1 })).toContainText('Was jetzt zu tun ist')
    await expect.poll(() => haPfad(page)).toBe(`/app/${SLUG}`)

    // Weiterblättern, dann schickt HA bei einer Größenänderung dieselben Eigenschaften.
    await blaettere(frame, '/grows')
    await page.evaluate(() => (window as unknown as { __schicke: () => void }).__schicke())
    await page.waitForTimeout(300)
    expect(await appPfad(frame)).toBe('/grows')

    // Zweiter Tipp, Grow OS ist schon offen.
    await tippe(page, `/app/${SLUG}/sensoren`)
    await expect.poll(() => appPfad(frame)).toBe('/sensoren')

    // Dritter Tipp — dieselbe Seite wie der erste, von woanders aus.
    await blaettere(frame, '/grows')
    await tippe(page, `/app/${SLUG}/aufgaben`)
    await expect.poll(() => appPfad(frame)).toBe('/aufgaben')

    // Genau eine Anmeldung: ein Seitenwechsel meldet nicht ab und wieder an.
    const arten = await page.evaluate(() =>
      ((window as unknown as { __nachrichten: { type: string }[] }).__nachrichten).map((n) => n.type),
    )
    expect(arten.filter((a) => a === 'home-assistant/subscribe-properties')).toHaveLength(1)
    expect(arten).not.toContain('home-assistant/unsubscribe-properties')
  })

  test('ein Geschwister-Frame darf die App nicht umlenken', async ({ page }) => {
    const frame = await huelle(page, `/app/${SLUG}`)
    await expect.poll(() => page.evaluate(() =>
      ((window as unknown as { __nachrichten: { type: string }[] }).__nachrichten).length)).toBeGreaterThan(0)
    expect(await appPfad(frame)).toBe('/')

    // Gleiche Herkunft, aber nicht das Elternfenster: wird übergangen.
    await page.evaluate(() => {
      const fremd = document.createElement('iframe')
      fremd.src = '/icons/gibt-es-nicht.txt'
      fremd.id = 'fremd'
      document.body.appendChild(fremd)
    })
    await page.waitForTimeout(500)
    await page.evaluate(() => {
      const ziel = (document.getElementById('f') as HTMLIFrameElement).contentWindow!
      const fremd = (document.getElementById('fremd') as HTMLIFrameElement).contentWindow as unknown as {
        eval: (code: string) => void
      }
      ;(fremd as unknown as { __ziel: Window }).__ziel = ziel
      fremd.eval(
        "window.__ziel.postMessage({ type: 'home-assistant/properties', route: { prefix: '/app/x', path: '/sensoren' } }, '*')",
      )
    })
    await page.waitForTimeout(500)
    expect(await appPfad(frame)).toBe('/')
  })
})
